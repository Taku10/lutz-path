namespace LutzPath.JobApplicationService.Interfaces;

public interface IUserServiceClient
{
    Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken);
}
