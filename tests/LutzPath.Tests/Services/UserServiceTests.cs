using LutzPath.Tests.Infrastructure;
using LutzPath.UserService.Data;
using LutzPath.UserService.DTOs;
using Microsoft.EntityFrameworkCore;
using UserServiceImplementation = LutzPath.UserService.Services.UserService;

namespace LutzPath.Tests.Services;

public class UserServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsProfileAndTrimsNames()
    {
        using var database = new TestDatabase<UsersDbContext>(options => new UsersDbContext(options));
        var service = new UserServiceImplementation(database.Context);
        var request = new UserRequest
        {
            FirstName = " Taylor ",
            LastName = " Student ",
            Email = "taylor@example.com",
            GraduationDate = new DateOnly(2027, 5, 15)
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        database.Context.ChangeTracker.Clear();
        var stored = await database.Context.Users.SingleAsync();
        Assert.True(result.Id > 0);
        Assert.Equal(result.Id, stored.Id);
        Assert.Equal("Taylor", stored.FirstName);
        Assert.Equal("Student", stored.LastName);
        Assert.Equal(request.Email, stored.Email);
        Assert.Equal(request.GraduationDate, stored.GraduationDate);
    }
}
