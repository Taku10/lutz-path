using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Models;
using LutzPath.JobApplicationService.Services;
using LutzPath.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LutzPath.Tests.Services;

public class TrackerFieldsTests
{
    [Fact]
    public async Task TrackerFields_RoundTripThroughCreateUpdateAndList()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        var db = database.Context;
        var jobs = new JobService(db);
        var checkedAt = new DateTime(2026, 9, 24, 10, 34, 0, DateTimeKind.Utc);
        var jobRequest = new JobRequest {
            Company = "Example", Title = "Graduate",
            OptFriendlyInformation = "Likely; confirm eligibility",
            H1bVisaInformation = "Historical sponsor; role dependent",
            PostingAvailability = "Open", LastAvailabilityCheck = checkedAt,
            AvailabilityCheckNotes = "Checked careers page; listing visible",
            LikelyInterviewProcess = "Recruiter, assessment, technical"
        };
        var job = await jobs.CreateAsync(jobRequest, default);
        jobRequest.PostingAvailability = "Closed";
        await jobs.UpdateAsync(job.Id, jobRequest, default);
        var applications = new ApplicationService(db, new FakeUserServiceClient());
        var request = new ApplicationRequest {
            UserId = 7, JobId = job.Id, Status = ApplicationStatus.NotApplied,
            DateFound = new DateOnly(2026, 9, 20), Priority = "Very High / role dependent",
            Notes = "Original comments", InterviewPrepNotes = "Practice explaining projects",
            ResumeTips = "Highlight internships", DsaTopicsToKnow = "Graphs, arrays",
            AdditionalSourceNotes = "9/24/2026 5:34 AM CT"
        };
        var application = await applications.CreateAsync(request, default);
        request.ResumeTips = "Highlight internships and capstone";
        await applications.UpdateAsync(application.Id, request, default);
        db.ChangeTracker.Clear();
        var storedJob = Assert.Single(await jobs.GetAllAsync(default));
        Assert.Equal("Closed", storedJob.PostingAvailability);
        Assert.Equal(jobRequest.OptFriendlyInformation, storedJob.OptFriendlyInformation);
        Assert.Equal(jobRequest.H1bVisaInformation, storedJob.H1bVisaInformation);
        Assert.Equal(jobRequest.LikelyInterviewProcess, storedJob.LikelyInterviewProcess);
        Assert.Equal(jobRequest.AvailabilityCheckNotes, storedJob.AvailabilityCheckNotes);
        Assert.Equal(checkedAt, storedJob.LastAvailabilityCheck);
        Assert.Equal(DateTimeKind.Utc, storedJob.LastAvailabilityCheck!.Value.Kind);
        var stored = Assert.Single(await applications.GetByUserIdAsync(7, default));
        Assert.Equal(ApplicationStatus.NotApplied, stored.Status);
        Assert.Equal(request.DateFound, stored.DateFound);
        Assert.Equal(request.Priority, stored.Priority);
        Assert.Equal(request.Notes, stored.Notes);
        Assert.Equal(request.InterviewPrepNotes, stored.InterviewPrepNotes);
        Assert.Equal(request.ResumeTips, stored.ResumeTips);
        Assert.Equal(request.DsaTopicsToKnow, stored.DsaTopicsToKnow);
        Assert.Equal(request.AdditionalSourceNotes, stored.AdditionalSourceNotes);
        Assert.Equal(stored, await applications.GetByIdAsync(application.Id, default));
    }

    [Fact]
    public async Task Migration_PreservesExistingJobAndApplication()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        var db = database.Context;
        var migrations = db.Database.GetMigrations().ToArray();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[^2]);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Jobs (Company, Title) VALUES ('Existing company', 'Existing role')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Applications (UserId, JobId, Status, Notes) VALUES (7, 1, 'Saved', 'Keep these notes')");
        await migrator.MigrateAsync();
        var job = await db.Jobs.SingleAsync();
        var application = await db.Applications.SingleAsync();
        Assert.Equal("Existing company", job.Company);
        Assert.Equal("Unknown", job.PostingAvailability);
        Assert.Null(job.OptFriendlyInformation);
        Assert.Equal("Keep these notes", application.Notes);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
        Assert.Null(application.DateFound);
        Assert.Null(application.Priority);
    }
}
