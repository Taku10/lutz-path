using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Interfaces;
using LutzPath.JobApplicationService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace LutzPath.JobApplicationService.Services;

public class JobService : IJobService
{
    private readonly JobApplicationsDbContext _db;

    public JobService(JobApplicationsDbContext db)
    {
        _db = db;
    }

    public async Task<List<JobResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Jobs.AsNoTracking().OrderBy(entity => entity.Id)
            .Select(entity => new JobResponse(entity.Id, entity.Company, entity.Title, entity.Location, entity.JobUrl, entity.Description, entity.DatePosted, entity.OptFriendlyInformation, entity.H1bVisaInformation, entity.LikelyInterviewProcess, entity.PostingAvailability, entity.LastAvailabilityCheck, entity.AvailabilityCheckNotes)).ToListAsync(cancellationToken);
    }

    public async Task<JobResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _db.Jobs.AsNoTracking().Where(entity => entity.Id == id)
            .Select(entity => new JobResponse(entity.Id, entity.Company, entity.Title, entity.Location, entity.JobUrl, entity.Description, entity.DatePosted, entity.OptFriendlyInformation, entity.H1bVisaInformation, entity.LikelyInterviewProcess, entity.PostingAvailability, entity.LastAvailabilityCheck, entity.AvailabilityCheckNotes)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobResponse> CreateAsync(JobRequest request, CancellationToken cancellationToken)
    {
        var entity = new Job();
        ApplyRequest(entity, request);
        _db.Jobs.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<JobResponse?> UpdateAsync(int id, JobRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.Jobs.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        ApplyRequest(entity, request);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await _db.Jobs.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        if (await _db.Applications.AnyAsync(application => application.JobId == id, cancellationToken))
        {
            throw new InvalidOperationException("Delete applications for this job before deleting the job.");
        }

        _db.Jobs.Remove(entity);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            // The database also protects the relationship if another request creates an application.
            throw new InvalidOperationException("This job is referenced by an application.", exception);
        }
        return true;
    }

    private static void ApplyRequest(Job entity, JobRequest request)
    {
        entity.Company = request.Company.Trim();
        entity.Title = request.Title.Trim();
        entity.Location = request.Location?.Trim();
        entity.JobUrl = request.JobUrl?.Trim();
        entity.Description = request.Description?.Trim();
        entity.DatePosted = request.DatePosted;
        entity.OptFriendlyInformation = request.OptFriendlyInformation?.Trim();
        entity.H1bVisaInformation = request.H1bVisaInformation?.Trim();
        entity.LikelyInterviewProcess = request.LikelyInterviewProcess?.Trim();
        entity.PostingAvailability = request.PostingAvailability;
        entity.LastAvailabilityCheck = request.LastAvailabilityCheck;
        entity.AvailabilityCheckNotes = request.AvailabilityCheckNotes?.Trim();
    }

    private static JobResponse ToResponse(Job entity) =>
        new JobResponse(entity.Id, entity.Company, entity.Title, entity.Location, entity.JobUrl, entity.Description, entity.DatePosted, entity.OptFriendlyInformation, entity.H1bVisaInformation, entity.LikelyInterviewProcess, entity.PostingAvailability, entity.LastAvailabilityCheck, entity.AvailabilityCheckNotes);
}
