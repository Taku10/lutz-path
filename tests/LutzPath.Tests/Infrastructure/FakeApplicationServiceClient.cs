using LutzPath.ReminderService.Clients;
using LutzPath.ReminderService.Interfaces;

namespace LutzPath.Tests.Infrastructure;

internal sealed class FakeApplicationServiceClient : IApplicationServiceClient
{
    public ApplicationReference? Application { get; set; }
    public Exception? Failure { get; set; }

    public Task<ApplicationReference?> GetApplicationAsync(int applicationId, CancellationToken cancellationToken)
    {
        return Failure is null
            ? Task.FromResult(Application)
            : Task.FromException<ApplicationReference?>(Failure);
    }
}

