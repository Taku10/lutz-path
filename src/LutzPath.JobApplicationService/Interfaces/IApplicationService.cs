using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Models;

namespace LutzPath.JobApplicationService.Interfaces;

public interface IApplicationService
{
    Task<List<ApplicationResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<ApplicationResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<List<ApplicationResponse>> GetByUserIdAsync(int userId, CancellationToken cancellationToken);
    Task<ApplicationResponse> CreateAsync(ApplicationRequest request, CancellationToken cancellationToken);
    Task<ApplicationResponse?> UpdateAsync(int id, ApplicationRequest request, CancellationToken cancellationToken);
    Task<ApplicationResponse?> UpdateStatusAsync(int id, ApplicationStatus status, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
