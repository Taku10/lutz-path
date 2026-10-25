using Microsoft.EntityFrameworkCore;
namespace LutzPath.ResumeService.Models;

public class MasterResume
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FileName { get; set; } = "";
    public string StorageId { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Approved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class TailoringProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = "Default";
    public string Instructions { get; set; } = "Use concise language. Preserve every factual detail. Emphasize relevant experience.";
    public int TargetPages { get; set; } = 1;
    public string Font { get; set; } = "Calibri";
}
public class ResumeDraft
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ApplicationId { get; set; }
    public Guid RequestId { get; set; }
    public string JobTitle { get; set; } = "";
    public string MasterSnapshot { get; set; } = "";
    public string TemplateStorageId { get; set; } = "";
    public string TemplateParagraphsJson { get; set; } = "";
    public string ProfileSnapshot { get; set; } = "";
    public string JobDescription { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public string Content { get; set; } = "";
    public string ReviewNotes { get; set; } = "";
    public string Error { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ResumeDocument
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int DraftId { get; set; }
    public string StorageId { get; set; } = "";
    public bool Submitted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public class ResumesDbContext(DbContextOptions<ResumesDbContext> options) : DbContext(options)
{
    public DbSet<MasterResume> Masters => Set<MasterResume>();
    public DbSet<TailoringProfile> Profiles => Set<TailoringProfile>();
    public DbSet<ResumeDraft> Drafts => Set<ResumeDraft>();
    public DbSet<ResumeDocument> Documents => Set<ResumeDocument>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<ResumeDraft>().HasIndex(x => new { x.UserId, x.RequestId }).IsUnique();
        model.Entity<ResumeDocument>().HasOne<ResumeDraft>().WithMany().HasForeignKey(x => x.DraftId);
    }
}
