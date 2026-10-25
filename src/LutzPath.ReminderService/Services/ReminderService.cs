using LutzPath.ReminderService.Data;
using LutzPath.ReminderService.DTOs;
using LutzPath.ReminderService.Interfaces;
using LutzPath.ReminderService.Models;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.ReminderService.Services;

public class ReminderService : IReminderService
{
    private readonly RemindersDbContext _db;
    private readonly IApplicationServiceClient _applicationServiceClient;

    public ReminderService(RemindersDbContext db, IApplicationServiceClient applicationServiceClient)
    {
        _db = db;
        _applicationServiceClient = applicationServiceClient;
    }

    public async Task<List<ReminderResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Reminders.AsNoTracking().OrderBy(entity => entity.Id)
            .Select(entity => new ReminderResponse(entity.Id, entity.UserId, entity.ApplicationId, entity.Title, entity.DueDate, entity.Completed)).ToListAsync(cancellationToken);
    }

    public async Task<ReminderResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _db.Reminders.AsNoTracking().Where(entity => entity.Id == id)
            .Select(entity => new ReminderResponse(entity.Id, entity.UserId, entity.ApplicationId, entity.Title, entity.DueDate, entity.Completed)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<List<ReminderResponse>> GetByUserIdAsync(int userId, CancellationToken cancellationToken)
    {
        return await _db.Reminders.AsNoTracking().Where(entity => entity.UserId == userId)
            .OrderBy(entity => entity.DueDate)
            .Select(entity => new ReminderResponse(entity.Id, entity.UserId, entity.ApplicationId, entity.Title, entity.DueDate, entity.Completed)).ToListAsync(cancellationToken);
    }

    public async Task<ReminderResponse> CreateAsync(ReminderRequest request, CancellationToken cancellationToken)
    {
        await ValidateApplicationAsync(request, cancellationToken);
        var entity = new Reminder();
        ApplyRequest(entity, request);
        _db.Reminders.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<ReminderResponse?> UpdateAsync(int id, ReminderRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.Reminders.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        await ValidateApplicationAsync(request, cancellationToken);
        ApplyRequest(entity, request);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var entity = await _db.Reminders.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _db.Reminders.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ValidateApplicationAsync(ReminderRequest request, CancellationToken cancellationToken)
    {
        var application = await _applicationServiceClient.GetApplicationAsync(request.ApplicationId, cancellationToken);
        if (application is null)
        {
            throw new ArgumentException("The selected application does not exist.", nameof(request.ApplicationId));
        }

        if (application.UserId != request.UserId)
        {
            throw new ArgumentException("The application does not belong to the selected user.", nameof(request.UserId));
        }
    }

    private static void ApplyRequest(Reminder entity, ReminderRequest request)
    {
        entity.UserId = request.UserId;
        entity.ApplicationId = request.ApplicationId;
        entity.Title = request.Title.Trim();
        entity.DueDate = request.DueDate!.Value;
        entity.Completed = request.Completed;
    }

    private static ReminderResponse ToResponse(Reminder entity) =>
        new ReminderResponse(entity.Id, entity.UserId, entity.ApplicationId, entity.Title, entity.DueDate, entity.Completed);
}
