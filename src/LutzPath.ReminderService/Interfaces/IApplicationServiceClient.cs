using LutzPath.ReminderService.Clients;

namespace LutzPath.ReminderService.Interfaces;

public interface IApplicationServiceClient
{
    Task<ApplicationReference?> GetApplicationAsync(int applicationId, CancellationToken cancellationToken);
}
