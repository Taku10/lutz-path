using LutzPath.JobApplicationService.Interfaces;

namespace LutzPath.Tests.Infrastructure;

internal sealed class FakeUserServiceClient : IUserServiceClient
{
    public bool UserExists { get; set; } = true;
    public Exception? Failure { get; set; }
    public int Calls { get; private set; }
    public int? LastUserId { get; private set; }

    public Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken)
    {
        Calls++;
        LastUserId = userId;
        return Failure is null ? Task.FromResult(UserExists) : Task.FromException<bool>(Failure);
    }
}

