using LutzPath.JobApplicationService.DTOs;

namespace LutzPath.JobApplicationService.Interfaces;

public interface IJobService
{
    Task<List<JobResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<JobResponse?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<JobResponse> CreateAsync(JobRequest request, CancellationToken cancellationToken);
    Task<JobResponse?> UpdateAsync(int id, JobRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
