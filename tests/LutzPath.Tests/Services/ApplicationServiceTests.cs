using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Exceptions;
using LutzPath.JobApplicationService.Models;
using LutzPath.JobApplicationService.Services;
using LutzPath.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.Tests.Services;

public class ApplicationServiceTests
{
    [Fact]
    public async Task CreateAsync_ValidUserAndJob_PersistsApplication()
    {
        using var database = CreateDatabase();
        var job = await AddJobAsync(database.Context);
        var users = new FakeUserServiceClient();
        var service = new ApplicationService(database.Context, users);

        var result = await service.CreateAsync(Request(job.Id), CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Applications.SingleAsync();
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal(job.Id, stored.JobId);
        Assert.Equal(7, stored.UserId);
        Assert.Equal(ApplicationStatus.Applied, stored.Status);
        Assert.Equal(7, users.LastUserId);
        Assert.Equal(DateTimeKind.Utc, stored.DateApplied!.Value.Kind);
    }

    [Fact]
    public async Task CreateAsync_UnknownUser_DoesNotSaveApplication()
    {
        using var database = CreateDatabase();
        var job = await AddJobAsync(database.Context);
        var service = new ApplicationService(database.Context, new FakeUserServiceClient { UserExists = false });

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Request(job.Id), CancellationToken.None));

        Assert.Contains("user does not exist", exception.Message);
        Assert.Empty(await database.Context.Applications.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_UnknownJob_DoesNotCallUserService()
    {
        using var database = CreateDatabase();
        var users = new FakeUserServiceClient();
        var service = new ApplicationService(database.Context, users);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Request(999), CancellationToken.None));

        Assert.Equal(0, users.Calls);
        Assert.Empty(await database.Context.Applications.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_UserServiceUnavailable_DoesNotSaveApplication()
    {
        using var database = CreateDatabase();
        var job = await AddJobAsync(database.Context);
        var users = new FakeUserServiceClient
        {
            Failure = new ServiceUnavailableException("User Service is unavailable.", new HttpRequestException())
        };
        var service = new ApplicationService(database.Context, users);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            service.CreateAsync(Request(job.Id), CancellationToken.None));

        Assert.Empty(await database.Context.Applications.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_DifferentUser_LeavesOriginalValuesUnchanged()
    {
        using var database = CreateDatabase();
        var job = await AddJobAsync(database.Context);
        var users = new FakeUserServiceClient();
        var service = new ApplicationService(database.Context, users);
        var original = await service.CreateAsync(Request(job.Id), CancellationToken.None);
        users.UserExists = false;
        var update = Request(job.Id);
        update.UserId = 999;
        update.Status = ApplicationStatus.Withdrawn;
        update.Notes = "Must not be saved";

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(original.Id, update, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Applications.SingleAsync();
        Assert.Equal(original.UserId, stored.UserId);
        Assert.Equal(original.Status, stored.Status);
        Assert.Equal(original.Notes, stored.Notes);
    }

    [Fact]
    public async Task UpdateStatusAsync_DoesNotNeedUserService()
    {
        using var database = CreateDatabase();
        var job = await AddJobAsync(database.Context);
        database.Context.Applications.Add(new JobApplication { UserId = 7, JobId = job.Id });
        await database.Context.SaveChangesAsync();
        var application = await database.Context.Applications.SingleAsync();
        var users = new FakeUserServiceClient { Failure = new HttpRequestException("Offline") };
        var service = new ApplicationService(database.Context, users);

        var result = await service.UpdateStatusAsync(application.Id, ApplicationStatus.Interview, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ApplicationStatus.Interview, result.Status);
        Assert.Equal(0, users.Calls);
    }

    private static TestDatabase<JobApplicationsDbContext> CreateDatabase() =>
        new(options => new JobApplicationsDbContext(options));

    private static async Task<Job> AddJobAsync(JobApplicationsDbContext db)
    {
        var job = new Job { Company = "Example Company", Title = "Graduate Developer" };
        db.Jobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static ApplicationRequest Request(int jobId) => new()
    {
        UserId = 7,
        JobId = jobId,
        Status = ApplicationStatus.Applied,
        DateApplied = new DateTime(2026, 10, 24, 15, 0, 0, DateTimeKind.Utc),
        Notes = "Submitted online"
    };
}

