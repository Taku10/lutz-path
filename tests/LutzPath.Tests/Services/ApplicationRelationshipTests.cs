using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Models;
using LutzPath.JobApplicationService.Services;
using LutzPath.JobApplicationService.Controllers;
using LutzPath.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace LutzPath.Tests.Services;

public class ApplicationRelationshipTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_CannotMoveApplicationToAnotherJobOrProfile(bool changeJob)
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        var db = database.Context;
        var first = new Job { Company = "First", Title = "Graduate" };
        var second = new Job { Company = "Second", Title = "Graduate" };
        db.Jobs.AddRange(first, second);
        await db.SaveChangesAsync();
        var original = new JobApplication { UserId = 7, JobId = first.Id, Notes = "Keep my notes", Status = ApplicationStatus.Interview };
        db.Applications.Add(original);
        await db.SaveChangesAsync();
        var users = new FakeUserServiceClient();
        var controller = new ApplicationsController(new ApplicationService(db, users));
        var response = await controller.Update(original.Id, new ApplicationRequest {
            UserId = changeJob ? 7 : 99, JobId = changeJob ? second.Id : first.Id,
            Status = ApplicationStatus.Rejected, Notes = "Wrong replacement"
        }, default);
        var conflict = Assert.IsType<ObjectResult>(response.Result);
        Assert.Equal(409, conflict.StatusCode);
        Assert.Equal(0, users.Calls);
        db.ChangeTracker.Clear();
        var stored = await db.Applications.SingleAsync();
        Assert.Equal(first.Id, stored.JobId);
        Assert.Equal(7, stored.UserId);
        Assert.Equal("Keep my notes", stored.Notes);
        Assert.Equal(ApplicationStatus.Interview, stored.Status);
    }
}
