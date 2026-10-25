using LutzPath.ReminderService.Clients;
using LutzPath.ReminderService.Data;
using LutzPath.ReminderService.DTOs;
using LutzPath.ReminderService.Exceptions;
using LutzPath.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using ReminderServiceImplementation = LutzPath.ReminderService.Services.ReminderService;

namespace LutzPath.Tests.Services;

public class ReminderServiceTests
{
    [Fact]
    public async Task CreateAsync_MatchingApplicationOwner_PersistsReminder()
    {
        using var database = CreateDatabase();
        var client = new FakeApplicationServiceClient { Application = new ApplicationReference(11, 7) };
        var service = new ReminderServiceImplementation(database.Context, client);

        var result = await service.CreateAsync(Request(), CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Reminders.SingleAsync();
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal(7, stored.UserId);
        Assert.Equal(11, stored.ApplicationId);
        Assert.Equal("Prepare for interview", stored.Title);
        Assert.Equal(DateTimeKind.Utc, stored.DueDate.Kind);
        Assert.False(stored.Completed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public async Task CreateAsync_MissingApplicationOrWrongOwner_DoesNotSave(int applicationOwner)
    {
        using var database = CreateDatabase();
        var client = new FakeApplicationServiceClient
        {
            Application = applicationOwner == 0 ? null : new ApplicationReference(11, applicationOwner)
        };
        var service = new ReminderServiceImplementation(database.Context, client);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(Request(), CancellationToken.None));

        Assert.Empty(await database.Context.Reminders.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_ApplicationServiceUnavailable_DoesNotSave()
    {
        using var database = CreateDatabase();
        var client = new FakeApplicationServiceClient
        {
            Failure = new ServiceUnavailableException("Application API is unavailable.", new HttpRequestException())
        };
        var service = new ReminderServiceImplementation(database.Context, client);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            service.CreateAsync(Request(), CancellationToken.None));

        Assert.Empty(await database.Context.Reminders.ToListAsync());
    }

    [Fact]
    public async Task UpdateAsync_WrongOwner_LeavesExistingReminderUnchanged()
    {
        using var database = CreateDatabase();
        var client = new FakeApplicationServiceClient { Application = new ApplicationReference(11, 7) };
        var service = new ReminderServiceImplementation(database.Context, client);
        var original = await service.CreateAsync(Request(), CancellationToken.None);
        var update = Request();
        update.UserId = 8;
        update.Title = "Must not be saved";
        update.Completed = true;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(original.Id, update, CancellationToken.None));

        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Reminders.SingleAsync();
        Assert.Equal(original.UserId, stored.UserId);
        Assert.Equal(original.Title, stored.Title);
        Assert.False(stored.Completed);
    }

    private static TestDatabase<RemindersDbContext> CreateDatabase() =>
        new(options => new RemindersDbContext(options));

    private static ReminderRequest Request() => new()
    {
        UserId = 7,
        ApplicationId = 11,
        Title = "Prepare for interview",
        DueDate = new DateTime(2026, 11, 4, 15, 0, 0, DateTimeKind.Utc)
    };
}
