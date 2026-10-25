using System.Net;
using System.Text;
using System.Text.Json;
using LutzPath.ResumeService.Controllers;
using LutzPath.ResumeService.Models;
using LutzPath.ResumeService.Services;
using LutzPath.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace LutzPath.Tests.Services;

public class ResumeTests : IDisposable
{
    private readonly List<string> directories = [];
    public void Dispose() { foreach (var directory in directories) if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    private sealed class Factory(Func<HttpRequestMessage, HttpResponseMessage> send) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new StubHttpMessageHandler((r, _) => Task.FromResult(send(r)))) { BaseAddress = new Uri("http://test/") };
    }
    private IConfiguration Config(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>(extra ?? new() { ["AI:Model"] = "test-model", ["AI:ApiKey"] = "test-key-never-used" });
        if (!values.ContainsKey("Storage:Path"))
        {
            var directory = Path.Combine(Path.GetTempPath(), "lutzpath-template-test-" + Guid.NewGuid().ToString("N"));
            directories.Add(directory); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "test-template.docx"), WordDocuments.Create("Avery Parker\nBuilt a C# application in 2026.", "Calibri"));
            values["Storage:Path"] = directory;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static Factory ValidFactory() => new(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri!.AbsolutePath.Contains("applications") ? "{\"id\":7,\"userId\":1,\"jobId\":3}" : "{\"company\":\"Example\",\"title\":\"Engineer\"}") });
    private static async Task Seed(ResumesDbContext db)
    {
        db.Masters.Add(new() { UserId = 1, StorageId = "test-template", Content = "Avery Parker\nBuilt a C# application in 2026.", Approved = true });
        db.Profiles.Add(new() { UserId = 1 }); await db.SaveChangesAsync();
    }
    private static GenerateRequest Generation(Guid? id = null) => new(1, 7, 1, 1, id ?? Guid.NewGuid(), new string('a', 100));

    [Fact]
    public void WordDocumentRoundTripsSpecialCharactersAndUnicode()
    {
        const string text = "Avery Parker\nC# & ASP.NET <projects>\nRésumé — graduate 2027";
        using var stream = new MemoryStream(WordDocuments.Create(text, "Calibri"));
        Assert.Equal(text, WordDocuments.Extract(stream));
    }
    [Fact]
    public void RejectsNonWordArchiveAndMissingSourceReferences()
    {
        Assert.Throws<InvalidDataException>(() => WordDocuments.Extract(new MemoryStream(Encoding.UTF8.GetBytes("invalid"))));
        Assert.Throws<InvalidOperationException>(() => TailoringClient.Validate(new([new("Invented fact", [9])], ""), 2));
        Assert.Throws<InvalidOperationException>(() => TailoringClient.Validate(new([new("No evidence", [])], ""), 2));
    }
    [Fact]
    public async Task GenerationSnapshotsInputsAndDeduplicatesRequests()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o)); var db = database.Context;
        await Seed(db); var controller = new ResumesController(db, Config(), ValidFactory());
        var request = Generation(); Assert.IsType<AcceptedResult>(await controller.Generate(request));
        db.Masters.Single().Content = "Changed master"; db.Profiles.Single().Instructions = "Changed profile"; await db.SaveChangesAsync();
        Assert.IsType<OkObjectResult>(await controller.Generate(request));
        var draft = Assert.Single(db.Drafts);
        Assert.Contains("Avery Parker", draft.MasterSnapshot); Assert.DoesNotContain("Changed", draft.ProfileSnapshot);
        Assert.Equal("Example · Engineer", draft.JobTitle); Assert.Equal(request.JobDescription, draft.JobDescription);
    }
    [Fact]
    public async Task OwnershipAndDependencyFailurePreventGeneration()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o)); var db = database.Context; await Seed(db);
        var wrongUser = new Factory(_ => new(HttpStatusCode.OK) { Content = new StringContent("{\"userId\":2,\"jobId\":3}") });
        Assert.IsType<NotFoundResult>(await new ResumesController(db, Config(), wrongUser).Generate(Generation()));
        var unavailable = new Factory(_ => new(HttpStatusCode.ServiceUnavailable));
        var failure = Assert.IsType<ObjectResult>(await new ResumesController(db, Config(), unavailable).Generate(Generation()));
        Assert.Equal(503, failure.StatusCode); Assert.Empty(db.Drafts);
    }
    [Fact]
    public async Task UnapprovedMasterAndMissingKeyPreventPaidRequests()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o)); var db = database.Context; await Seed(db);
        db.Masters.Single().Approved = false; await db.SaveChangesAsync();
        var invalid = Assert.IsType<ObjectResult>(await new ResumesController(db, Config(), ValidFactory()).Generate(Generation()));
        Assert.Equal(400, invalid.StatusCode);
        var missing = Assert.IsType<ObjectResult>(await new ResumesController(db, Config(new()), ValidFactory()).Generate(Generation()));
        Assert.Equal(503, missing.StatusCode); Assert.Empty(db.Drafts);
    }
    [Fact]
    public async Task EditingProfilePreservesExistingSnapshotAndEnforcesOwnership()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o)); var db = database.Context; await Seed(db);
        var controller = new ResumesController(db, Config(), ValidFactory()); await controller.Generate(Generation());
        var snapshot = db.Drafts.Single().ProfileSnapshot;
        var edit = new ProfileRequest("Graduate", "Emphasize relevant projects.", 2, "Arial");
        Assert.IsType<NotFoundResult>(await controller.UpdateProfile(1, 2, edit));
        Assert.IsType<NoContentResult>(await controller.UpdateProfile(1, 1, edit));
        Assert.Equal("Graduate", db.Profiles.Single().Name); Assert.Equal(snapshot, db.Drafts.Single().ProfileSnapshot);
    }

    [Fact]
    public async Task UploadStoresOriginalAndRequiresApprovalBeforeUse()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o));
        var directory = Path.Combine(Path.GetTempPath(), "lutzpath-upload-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var controller = new ResumesController(database.Context, Config(new() { ["Storage:Path"] = directory }), ValidFactory());
            var bytes = WordDocuments.Create("Avery Parker\nBuilt a job tracker.", "Calibri");
            using var stream = new MemoryStream(bytes);
            var file = new FormFile(stream, 0, bytes.Length, "file", "master.docx");
            Assert.IsType<OkObjectResult>(await controller.Upload(1, file));
            var master = Assert.Single(database.Context.Masters);
            Assert.False(master.Approved); Assert.Equal("Avery Parker\nBuilt a job tracker.", master.Content);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory, master.StorageId + ".docx")));
            Assert.IsType<NotFoundResult>(await controller.Approve(master.Id, 2, new(master.Content)));
            Assert.IsType<NoContentResult>(await controller.Approve(master.Id, 1, new(master.Content)));
            Assert.True(master.Approved);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task ApprovedMasterCannotChangeAndExportsStayImmutable()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o)); var db = database.Context; await Seed(db);
        var directory = Path.Combine(Path.GetTempPath(), "lutzpath-resume-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var controller = new ResumesController(db, Config(new() { ["Storage:Path"] = directory }), ValidFactory());
            var frozen = Assert.IsType<ObjectResult>(await controller.Approve(1, 1, new("Changed content"))); Assert.Equal(409, frozen.StatusCode);
            var draft = new ResumeDraft { UserId = 1, Status = "Ready", Content = "Original content for export", ProfileSnapshot = JsonSerializer.Serialize(new TailoringProfile()), RequestId = Guid.NewGuid() };
            db.Drafts.Add(draft); await db.SaveChangesAsync();
            Assert.IsType<NotFoundResult>(await controller.Draft(draft.Id, 2));
            Assert.IsType<NotFoundResult>(await controller.Export(draft.Id, 2));
            await controller.Export(draft.Id, 1); var first = db.Documents.Single();
            await controller.Edit(draft.Id, 1, new("Updated resume content")); await controller.Export(draft.Id, 1);
            Assert.Equal(2, db.Documents.Count());
            using var stream = File.OpenRead(Path.Combine(directory, first.StorageId + ".docx")); Assert.Equal("Original content for export", WordDocuments.Extract(stream));
            Assert.IsType<NotFoundResult>(await controller.Download(first.Id, 2));
            await controller.Submit(first.Id, 1); Assert.True(first.Submitted);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task OpenAiTransportUsesStructuredOutputAndDoesNotStoreResponses()
    {
        var handler = new StubHttpMessageHandler(async (request, ct) => {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            var content = JsonSerializer.Serialize(new { paragraphs = new[] { new { text = "Avery Parker", sourceLines = new[] { 1 } } }, reviewNotes = "Check factual details." });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = content } } } } })) };
        });
        var result = await new TailoringClient(new HttpClient(handler), Config()).Generate(new() { Model = "test-model", MasterSnapshot = "Avery Parker" }, CancellationToken.None);
        Assert.Equal("Avery Parker", Assert.Single(result.Paragraphs).Text);
    }
    [Fact]
    public async Task NewDraftExportsFromItsOriginalTemplateAndRejectsCollapsedParagraphs()
    {
        using var database = new TestDatabase<ResumesDbContext>(o => new(o)); var db = database.Context; await Seed(db);
        var config = Config(); var originalPath = Path.Combine(config["Storage:Path"]!, "test-template.docx");
        var original = await File.ReadAllBytesAsync(originalPath);
        db.Profiles.Single().Font = "Arial"; await db.SaveChangesAsync();
        var controller = new ResumesController(db, config, ValidFactory());
        Assert.IsType<AcceptedResult>(await controller.Generate(Generation()));
        var draft = Assert.Single(db.Drafts); Assert.Equal("test-template", draft.TemplateStorageId);
        Assert.Equal(2, JsonSerializer.Deserialize<TemplateParagraph[]>(draft.TemplateParagraphsJson)!.Length);
        draft.Status = "Ready"; draft.Content = "Avery Parker\nBuilt a relevant C# application in 2026."; await db.SaveChangesAsync();
        var invalid = Assert.IsType<ObjectResult>(await controller.Edit(draft.Id, 1, new("Collapsed into one paragraph.")));
        Assert.Equal(400, invalid.StatusCode);
        Assert.IsType<OkObjectResult>(await controller.Export(draft.Id, 1));
        var document = Assert.Single(db.Documents);
        var exported = await File.ReadAllBytesAsync(Path.Combine(config["Storage:Path"]!, document.StorageId + ".docx"));
        using var stream = new MemoryStream(exported); using var zip = new System.IO.Compression.ZipArchive(stream);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = await reader.ReadToEndAsync(); Assert.Contains("Calibri", xml); Assert.DoesNotContain("Arial", xml);
        Assert.Contains("Built a relevant C# application", xml); Assert.Equal(original, await File.ReadAllBytesAsync(originalPath));
    }
    [Fact]
    public async Task TemplateAiRequestsRequireParagraphIdsAndValidateReturnedMapping()
    {
        var manifest = new[] { new TemplateParagraph(1, "Avery Parker", false, "Protected"), new TemplateParagraph(2, "Built software.", true, "") };
        var handler = new StubHttpMessageHandler(async (request, ct) => {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var item = body.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema").GetProperty("properties").GetProperty("paragraphs").GetProperty("items");
            Assert.Contains(item.GetProperty("required").EnumerateArray(), x => x.GetString() == "paragraphId");
            Assert.Contains("templateParagraphs", body.RootElement.GetProperty("input").GetString());
            var content = JsonSerializer.Serialize(new { paragraphs = new[] { new { paragraphId = 2, text = "Developed software.", sourceLines = new[] { 2 } } }, reviewNotes = "Review." });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { status = "completed", output = new[] { new { content = new[] { new { type = "output_text", text = content } } } } })) };
        });
        var result = await new TailoringClient(new HttpClient(handler), Config()).Generate(new() { Model = "test-model", MasterSnapshot = "Avery Parker\nBuilt software.", TemplateParagraphsJson = JsonSerializer.Serialize(manifest) }, CancellationToken.None);
        Assert.Equal(2, Assert.Single(result.Paragraphs).ParagraphId);
    }
    private sealed class FakeTailor : ITailoringClient
    {
        public int Calls;
        public Task<TailoredResult> Generate(ResumeDraft draft, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new TailoredResult([new("Avery Parker", [1])], "Review the draft.")); }
    }
    [Fact]
    public async Task WorkerProcessesQueueAndDoesNotRetryInterruptedGenerations()
    {
        var file = Path.Combine(Path.GetTempPath(), "lutzpath-queue-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var services = new ServiceCollection(); var fake = new FakeTailor();
            services.AddDbContext<ResumesDbContext>(o => o.UseSqlite("Data Source=" + file)); services.AddSingleton<ITailoringClient>(fake);
            await using var provider = services.BuildServiceProvider();
            using (var scope = provider.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<ResumesDbContext>(); await db.Database.MigrateAsync(); db.Drafts.AddRange(new ResumeDraft { UserId = 1, RequestId = Guid.NewGuid(), Status = "Generating" }, new ResumeDraft { UserId = 1, RequestId = Guid.NewGuid() }); await db.SaveChangesAsync(); }
            using var worker = new GenerationWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<GenerationWorker>.Instance);
            await worker.StartAsync(CancellationToken.None);
            for (var i = 0; i < 50; i++) { using var scope = provider.CreateScope(); if (await scope.ServiceProvider.GetRequiredService<ResumesDbContext>().Drafts.AnyAsync(x => x.Status == "Ready")) break; await Task.Delay(50); }
            await worker.StopAsync(CancellationToken.None);
            using var check = provider.CreateScope(); var rows = await check.ServiceProvider.GetRequiredService<ResumesDbContext>().Drafts.OrderBy(x => x.Id).ToListAsync();
            Assert.Equal("Failed", rows[0].Status); Assert.Equal("Ready", rows[1].Status); Assert.Equal(1, fake.Calls);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(file)) File.Delete(file); }
    }
}
