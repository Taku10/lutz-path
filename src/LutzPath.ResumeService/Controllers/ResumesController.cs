using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using LutzPath.ResumeService.Models;
using LutzPath.ResumeService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.ResumeService.Controllers;

public record ApproveMaster([Required, StringLength(60000, MinimumLength = 10)] string Content);
public record ProfileRequest([Required, StringLength(80)] string Name, [Required, StringLength(20000)] string Instructions, [Range(1, 2)] int TargetPages, string Font);
public record GenerateRequest([Range(1, int.MaxValue)] int UserId, [Range(1, int.MaxValue)] int ApplicationId, [Range(1, int.MaxValue)] int MasterId, [Range(1, int.MaxValue)] int ProfileId, Guid RequestId, [Required, StringLength(40000, MinimumLength = 80)] string JobDescription);
public record DraftEdit([Required, StringLength(60000, MinimumLength = 10)] string Content);

[ApiController, Route("api/resumes")]
public class ResumesController(ResumesDbContext db, IConfiguration config, IHttpClientFactory clients) : ControllerBase
{
    private string Root => config["Storage:Path"] ?? Path.Combine(AppContext.BaseDirectory, "data");
    private string Storage(string id) => Path.Combine(Root, id + ".docx");
    private bool Configured => !string.IsNullOrWhiteSpace(config["AI:Model"]) && (!string.IsNullOrWhiteSpace(config["AI:ApiKey"]) || System.IO.File.Exists(config["AI:ApiKeyFile"]));
    private const string WordMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    [HttpGet("settings")]
    public IActionResult Settings() => Ok(new { configured = Configured, provider = "OpenAI", model = config["AI:Model"] ?? "" });

    [HttpGet]
    public async Task<IActionResult> List([FromQuery, Range(1, int.MaxValue)] int userId)
    {
        var masters = await db.Masters.Where(x => x.UserId == userId).OrderByDescending(x => x.Id).Select(x => new { x.Id, x.FileName, x.Content, x.Approved, x.CreatedAt }).ToListAsync();
        var profiles = await db.Profiles.Where(x => x.UserId == userId).OrderBy(x => x.Id).ToListAsync();
        var drafts = await db.Drafts.Where(x => x.UserId == userId).OrderByDescending(x => x.Id).Select(x => new { x.Id, x.ApplicationId, x.JobTitle, x.Status, x.Error, x.CreatedAt }).ToListAsync();
        var documents = await db.Documents.Where(x => x.UserId == userId).OrderByDescending(x => x.Id).Select(x => new { x.Id, x.DraftId, x.Submitted, x.CreatedAt }).ToListAsync();
        return Ok(new { masters, profiles, drafts, documents });
    }

    [HttpPost("masters"), RequestSizeLimit(3_000_000)]
    public async Task<IActionResult> Upload([FromQuery, Range(1, int.MaxValue)] int userId, IFormFile file)
    {
        if (file.Length is < 1 or > 2_000_000 || !Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return Problem("Choose a .docx file up to 2 MB.", statusCode: 400);
        // Validate the profile over HTTP before storing a personal document.
        try
        {
            using var response = await clients.CreateClient("users").GetAsync($"api/users/{userId}");
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return Problem("Profile not found.", statusCode: 404);
            if (!response.IsSuccessStatusCode) return Problem("User Service is unavailable.", statusCode: 503);
        }
        catch (HttpRequestException) { return Problem("User Service is unavailable.", statusCode: 503); }
        catch (TaskCanceledException) { return Problem("User Service timed out.", statusCode: 503); }
        string content;
        try
        {
            using var stream = new MemoryStream(); await file.CopyToAsync(stream);
            content = string.Join("\n", WordTemplates.Describe(stream.ToArray()).Select(p => p.Text));
            if (content.Length is < 10 or > 60000) throw new InvalidDataException("Invalid resume text length.");
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException) { return Problem("The Word file could not be read. Use a .docx with selectable text, without macros, up to 10 MB uncompressed.", statusCode: 400); }
        var master = new MasterResume { UserId = userId, FileName = Path.GetFileName(file.FileName), StorageId = Guid.NewGuid().ToString("N"), Content = content };
        Directory.CreateDirectory(Root);
        await using (var output = System.IO.File.Create(Storage(master.StorageId))) await file.CopyToAsync(output);
        db.Masters.Add(master);
        await db.SaveChangesAsync();
        return Ok(new { master.Id, master.FileName, master.Content, master.Approved });
    }

    [HttpPost("masters/{id:int}/approve")]
    public async Task<IActionResult> Approve(int id, [FromQuery, Range(1, int.MaxValue)] int userId, ApproveMaster request)
    {
        var master = await db.Masters.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (master is null) return NotFound();
        if (master.Approved) return Problem("This version is already approved. Upload a new master to change it.", statusCode: 409);
        var content = request.Content.Replace("\r\n", "\n");
        try { WordTemplates.WithApprovedContent(await System.IO.File.ReadAllBytesAsync(Storage(master.StorageId)), content); }
        catch (InvalidDataException ex) { return Problem(ex.Message, statusCode: 400); }
        catch (IOException) { return Problem("The original Word file is unavailable. Upload your master again.", statusCode: 409); }
        master.Content = content; master.Approved = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("masters/{id:int}/download")]
    public async Task<IActionResult> Original(int id, [FromQuery, Range(1, int.MaxValue)] int userId)
    {
        var master = await db.Masters.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        return master is null ? NotFound() : PhysicalFile(Path.GetFullPath(Storage(master.StorageId)), WordMime, "master-resume.docx");
    }

    [HttpPost("profiles")]
    public async Task<IActionResult> Profile([FromQuery, Range(1, int.MaxValue)] int userId, ProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Instructions)) return Problem("Enter a profile name and instructions.", statusCode: 400);
        if (!new[] { "Calibri", "Arial", "Aptos" }.Contains(request.Font)) return Problem("Choose Calibri, Arial or Aptos.", statusCode: 400);
        var profile = new TailoringProfile { UserId = userId, Name = request.Name.Trim(), Instructions = request.Instructions.Trim(), Font = request.Font, TargetPages = request.TargetPages };
        db.Profiles.Add(profile); await db.SaveChangesAsync(); return Ok(profile);
    }

    [HttpPut("profiles/{id:int}")]
    public async Task<IActionResult> UpdateProfile(int id, [FromQuery, Range(1, int.MaxValue)] int userId, ProfileRequest request)
    {
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (profile is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Instructions) || !new[] { "Calibri", "Arial", "Aptos" }.Contains(request.Font))
            return Problem("Enter a name, instructions and a supported font.", statusCode: 400);
        profile.Name = request.Name.Trim(); profile.Instructions = request.Instructions.Trim(); profile.TargetPages = request.TargetPages; profile.Font = request.Font;
        await db.SaveChangesAsync(); return NoContent();
    }

    [HttpPost("drafts")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Generate(GenerateRequest request)
    {
        if (request.RequestId == Guid.Empty) return Problem("A request ID is required.", statusCode: 400);
        var existing = await db.Drafts.FirstOrDefaultAsync(x => x.UserId == request.UserId && x.RequestId == request.RequestId);
        if (existing is not null) return Ok(new { existing.Id, existing.Status });
        if (!Configured) return Problem("AI generation needs a server-side API key and model. Master uploads and profiles work without AI.", statusCode: 503);
        var master = await db.Masters.SingleOrDefaultAsync(x => x.Id == request.MasterId && x.UserId == request.UserId && x.Approved);
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.Id == request.ProfileId && x.UserId == request.UserId);
        if (master is null || profile is null) return Problem("Choose an approved master and a tailoring profile belonging to this user.", statusCode: 400);
        TemplateParagraph[] template;
        try { template = WordTemplates.WithApprovedContent(await System.IO.File.ReadAllBytesAsync(Storage(master.StorageId)), master.Content); }
        catch (InvalidDataException ex) { return Problem(ex.Message, statusCode: 409); }
        catch (IOException) { return Problem("The original Word file is unavailable. Upload and approve your master again.", statusCode: 409); }
        if (!template.Any(p => p.Editable)) return Problem("This template has no safely editable paragraphs. Use normal Word paragraphs for the bullets you want tailored; complex formatting stays protected.", statusCode: 400);
        string jobTitle;
        try
        {
            var client = clients.CreateClient("applications");
            using var applicationResponse = await client.GetAsync($"api/applications/{request.ApplicationId}");
            if (applicationResponse.StatusCode == System.Net.HttpStatusCode.NotFound) return NotFound();
            if (!applicationResponse.IsSuccessStatusCode) return Problem("Application Service is unavailable.", statusCode: 503);
            using var application = JsonDocument.Parse(await applicationResponse.Content.ReadAsStringAsync());
            if (application.RootElement.GetProperty("userId").GetInt32() != request.UserId) return NotFound();
            var jobId = application.RootElement.GetProperty("jobId").GetInt32();
            using var jobResponse = await client.GetAsync($"api/jobs/{jobId}");
            if (!jobResponse.IsSuccessStatusCode) return Problem("The job could not be loaded. Try again later.", statusCode: 503);
            using var job = JsonDocument.Parse(await jobResponse.Content.ReadAsStringAsync());
            jobTitle = $"{job.RootElement.GetProperty("company").GetString()} · {job.RootElement.GetProperty("title").GetString()}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return Problem("Application Service is unavailable. No generation was started.", statusCode: 503); }
        var draft = new ResumeDraft { UserId = request.UserId, ApplicationId = request.ApplicationId, RequestId = request.RequestId, MasterSnapshot = master.Content, ProfileSnapshot = JsonSerializer.Serialize(profile), JobDescription = request.JobDescription.Trim(), JobTitle = jobTitle, Model = config["AI:Model"]! };
        db.Drafts.Add(draft);
        draft.TemplateStorageId = master.StorageId;
        draft.TemplateParagraphsJson = JsonSerializer.Serialize(template);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); var duplicate = await db.Drafts.FirstOrDefaultAsync(x => x.UserId == request.UserId && x.RequestId == request.RequestId); if (duplicate is null) throw; return Ok(new { duplicate.Id, duplicate.Status }); }
        return Accepted($"/api/resumes/drafts/{draft.Id}?userId={request.UserId}", new { draft.Id, draft.Status });
    }

    [HttpGet("drafts/{id:int}")]
    public async Task<IActionResult> Draft(int id, [FromQuery, Range(1, int.MaxValue)] int userId)
    {
        var draft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        return draft is null ? NotFound() : Ok(draft);
    }
    [HttpPut("drafts/{id:int}")]
    public async Task<IActionResult> Edit(int id, [FromQuery, Range(1, int.MaxValue)] int userId, DraftEdit request)
    {
        var draft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (draft is null) return NotFound();
        if (draft.Status != "Ready") return Problem("Only ready drafts can be edited.", statusCode: 409);
        var content = request.Content.Replace("\r\n", "\n");
        if (!string.IsNullOrEmpty(draft.TemplateParagraphsJson))
        {
            try { WordTemplates.ValidateContent(JsonSerializer.Deserialize<TemplateParagraph[]>(draft.TemplateParagraphsJson)!, content); }
            catch (InvalidDataException ex) { return Problem(ex.Message, statusCode: 400); }
        }
        draft.Content = content; await db.SaveChangesAsync(); return NoContent();
    }
    [HttpPost("drafts/{id:int}/export")]
    public async Task<IActionResult> Export(int id, [FromQuery, Range(1, int.MaxValue)] int userId)
    {
        var draft = await db.Drafts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (draft is null) return NotFound();
        if (draft.Status != "Ready") return Problem("The draft is not ready.", statusCode: 409);
        var profile = JsonSerializer.Deserialize<TailoringProfile>(draft.ProfileSnapshot)!;
        var document = new ResumeDocument { UserId = userId, DraftId = id, StorageId = Guid.NewGuid().ToString("N") };
        Directory.CreateDirectory(Root);
        byte[] bytes;
        if (!string.IsNullOrEmpty(draft.TemplateStorageId))
        {
            try { bytes = WordTemplates.Apply(await System.IO.File.ReadAllBytesAsync(Storage(draft.TemplateStorageId)), JsonSerializer.Deserialize<TemplateParagraph[]>(draft.TemplateParagraphsJson)!, draft.Content); }
            catch (InvalidDataException ex) { return Problem(ex.Message, statusCode: 409); }
            catch (IOException) { return Problem("The original Word template is unavailable. Upload the master and generate a new draft.", statusCode: 409); }
        }
        else bytes = WordDocuments.Create(draft.Content, profile.Font); // Older drafts retain their original export behavior.
        await System.IO.File.WriteAllBytesAsync(Storage(document.StorageId), bytes);
        db.Documents.Add(document); await db.SaveChangesAsync();
        return Ok(new { document.Id });
    }
    [HttpGet("documents/{id:int}/download")]
    public async Task<IActionResult> Download(int id, [FromQuery, Range(1, int.MaxValue)] int userId)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        return document is null ? NotFound() : PhysicalFile(Path.GetFullPath(Storage(document.StorageId)), WordMime, $"LutzPath-resume-{id}.docx");
    }
    [HttpPost("documents/{id:int}/submitted")]
    public async Task<IActionResult> Submit(int id, [FromQuery, Range(1, int.MaxValue)] int userId)
    {
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (document is null) return NotFound();
        document.Submitted = true; await db.SaveChangesAsync(); return NoContent();
    }
}
