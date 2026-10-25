using LutzPath.JobApplicationService.Models;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.JobApplicationService.Data;

public class JobApplicationsDbContext : DbContext
{
    public JobApplicationsDbContext(DbContextOptions<JobApplicationsDbContext> options) : base(options)
    {
    }

    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<GoogleSheetBinding> GoogleSheetBindings => Set<GoogleSheetBinding>();
    public DbSet<GoogleSheetSyncState> GoogleSheetSyncStates => Set<GoogleSheetSyncState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GoogleSheetBinding>().HasIndex(e => new { e.SpreadsheetId, e.SheetId, e.SourceId }).IsUnique();
        modelBuilder.Entity<GoogleSheetBinding>().HasIndex(e => new { e.SpreadsheetId, e.SheetId, e.UserId, e.JobId }).IsUnique();
        // Keep bindings as tombstones if a user deletes a record; sync must not recreate it.
        modelBuilder.Entity<GoogleSheetSyncState>().Property(e => e.LastAttemptUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
        modelBuilder.Entity<GoogleSheetSyncState>().Property(e => e.LastSuccessUtc)
            .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : (DateTime?)null);
        modelBuilder.Entity<Job>().Property(entity => entity.LastAvailabilityCheck)
            .HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null);
        modelBuilder.Entity<JobApplication>().Property(entity => entity.DateApplied)
            .HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null);
        modelBuilder.Entity<JobApplication>().Property(entity => entity.InterviewDate)
            .HasConversion(value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : (DateTime?)null);

        modelBuilder.Entity<JobApplication>()
            .HasOne<Job>()
            .WithMany()
            .HasForeignKey(application => application.JobId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<JobApplication>().Property(application => application.Status)
            .HasConversion<string>();
        modelBuilder.Entity<JobApplication>().HasIndex(application => application.UserId);
    }
}
