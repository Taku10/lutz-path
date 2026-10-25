using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Interfaces;
using LutzPath.JobApplicationService.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.JobApplicationService.Services;

public class ApplicationService : IApplicationService
{
    private readonly JobApplicationsDbContext _db;
    private readonly IUserServiceClient _userServiceClient;

    public ApplicationService(JobApplicationsDbContext db, IUserServiceClient userServiceClient)
    {
        _db = db;
        _userServiceClient = userServiceClient;
    }

    public async Task<List<ApplicationResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Applications.AsNoTracking().OrderBy(application => application.Id)
            .Select(application => new ApplicationResponse(application.Id, application.UserId,
                application.JobId, application.Status, application.DateApplied,
                application.InterviewDate, application.Notes, application.DateFound, application.Priority, application.InterviewPrepNotes, application.ResumeTips, application.DsaTopicsToKnow, application.AdditionalSourceNotes))
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var application = await _db.Applications.AsNoTracking()
            .SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        return application is null ? null : ToResponse(application);
    }

    public async Task<List<ApplicationResponse>> GetByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        return await _db.Applications.AsNoTracking().Where(application => application.UserId == userId)
            .OrderBy(application => application.Id)
            .Select(application => new ApplicationResponse(application.Id, application.UserId,
                application.JobId, application.Status, application.DateApplied,
                application.InterviewDate, application.Notes, application.DateFound, application.Priority, application.InterviewPrepNotes, application.ResumeTips, application.DsaTopicsToKnow, application.AdditionalSourceNotes))
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationResponse> CreateAsync(ApplicationRequest request, CancellationToken cancellationToken)
    {
        await ValidateJobAsync(request.JobId, cancellationToken);
        await ValidateUserAsync(request.UserId, cancellationToken);
        var application = new JobApplication();
        ApplyRequest(application, request);
        _db.Applications.Add(application);
        await SaveApplicationAsync(cancellationToken);
        return ToResponse(application);
    }

    public async Task<ApplicationResponse?> UpdateAsync(int id, ApplicationRequest request, CancellationToken cancellationToken)
    {
        var application = await _db.Applications.SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        if (application is null)
        {
            return null;
        }

        if (request.JobId != application.JobId || request.UserId != application.UserId)
        {
            throw new InvalidOperationException("An existing application cannot be moved to another job or profile. Create a separate application instead.");
        }

        await ValidateJobAsync(request.JobId, cancellationToken);
        await ValidateUserAsync(request.UserId, cancellationToken);
        ApplyRequest(application, request);
        await SaveApplicationAsync(cancellationToken);
        return ToResponse(application);
    }

    public async Task<ApplicationResponse?> UpdateStatusAsync(int id, ApplicationStatus status, CancellationToken cancellationToken)
    {
        var application = await _db.Applications.SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        if (application is null)
        {
            return null;
        }

        application.Status = status;
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(application);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var application = await _db.Applications.SingleOrDefaultAsync(application => application.Id == id, cancellationToken);
        if (application is null)
        {
            return false;
        }

        _db.Applications.Remove(application);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ValidateUserAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await _userServiceClient.UserExistsAsync(userId, cancellationToken))
        {
            throw new ArgumentException("The selected user does not exist.", nameof(userId));
        }
    }

    private async Task ValidateJobAsync(int jobId, CancellationToken cancellationToken)
    {
        if (!await _db.Jobs.AnyAsync(job => job.Id == jobId, cancellationToken))
        {
            throw new ArgumentException("The selected job does not exist.", nameof(jobId));
        }
    }

    private async Task SaveApplicationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            throw new ArgumentException("The selected job no longer exists.", exception);
        }
    }

    private static void ApplyRequest(JobApplication application, ApplicationRequest request)
    {
        application.UserId = request.UserId;
        application.JobId = request.JobId;
        application.Status = request.Status;
        application.DateApplied = request.DateApplied;
        application.InterviewDate = request.InterviewDate;
        application.Notes = request.Notes?.Trim();
        application.DateFound = request.DateFound;
        application.Priority = request.Priority?.Trim();
        application.InterviewPrepNotes = request.InterviewPrepNotes?.Trim();
        application.ResumeTips = request.ResumeTips?.Trim();
        application.DsaTopicsToKnow = request.DsaTopicsToKnow?.Trim();
        application.AdditionalSourceNotes = request.AdditionalSourceNotes?.Trim();
    }

    private static ApplicationResponse ToResponse(JobApplication application) =>
        new(application.Id, application.UserId, application.JobId, application.Status,
            application.DateApplied, application.InterviewDate, application.Notes, application.DateFound, application.Priority, application.InterviewPrepNotes, application.ResumeTips, application.DsaTopicsToKnow, application.AdditionalSourceNotes);
}
