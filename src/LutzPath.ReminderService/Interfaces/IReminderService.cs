using LutzPath.ReminderService.DTOs;

namespace LutzPath.ReminderService.Interfaces;

public interface IReminderService
{
    Task<List<ReminderResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<ReminderResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<List<ReminderResponse>> GetByUserIdAsync(int userId, CancellationToken cancellationToken);
    Task<ReminderResponse> CreateAsync(ReminderRequest request, CancellationToken cancellationToken);
    Task<ReminderResponse?> UpdateAsync(int id, ReminderRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
