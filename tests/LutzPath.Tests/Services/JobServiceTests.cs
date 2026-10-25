using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Models;
using LutzPath.JobApplicationService.Services;
using LutzPath.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.Tests.Services;

public class JobServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsJobWithoutOptionalFields()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new JobApplicationsDbContext(options));
        var service = new JobService(database.Context);

        var result = await service.CreateAsync(new JobRequest
        {
            Company = " Example Company ",
            Title = " Graduate Developer "
        }, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Jobs.SingleAsync();
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal("Example Company", stored.Company);
        Assert.Equal("Graduate Developer", stored.Title);
        Assert.Null(stored.JobUrl);
        Assert.Null(stored.DatePosted);
    }

    [Fact]
    public async Task DeleteAsync_RejectsJobReferencedByApplication()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new JobApplicationsDbContext(options));
        var job = new Job { Company = "Example Company", Title = "Developer" };
        database.Context.Jobs.Add(job);
        await database.Context.SaveChangesAsync();
        database.Context.Applications.Add(new JobApplication { JobId = job.Id, UserId = 7 });
        await database.Context.SaveChangesAsync();
        var service = new JobService(database.Context);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(job.Id, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        Assert.True(await database.Context.Jobs.AnyAsync(entity => entity.Id == job.Id));
        Assert.Single(await database.Context.Applications.ToListAsync());
    }
}

