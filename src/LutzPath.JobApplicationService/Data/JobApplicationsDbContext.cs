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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
