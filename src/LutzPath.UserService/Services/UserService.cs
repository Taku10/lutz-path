using LutzPath.UserService.Data;
using LutzPath.UserService.DTOs;
using LutzPath.UserService.Interfaces;
using LutzPath.UserService.Models;
using Microsoft.EntityFrameworkCore;

namespace LutzPath.UserService.Services;

public class UserService : IUserService
{
    private readonly UsersDbContext _db;

    public UserService(UsersDbContext db)
    {
        _db = db;
    }

    public async Task<List<UserResponse>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await _db.Users.AsNoTracking().OrderBy(entity => entity.Id)
            .Select(entity => new UserResponse(entity.Id, entity.FirstName, entity.LastName, entity.Email, entity.GraduationDate)).ToListAsync(cancellationToken);
    }

    public async Task<UserResponse?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        return await _db.Users.AsNoTracking().Where(entity => entity.Id == id)
            .Select(entity => new UserResponse(entity.Id, entity.FirstName, entity.LastName, entity.Email, entity.GraduationDate)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<UserResponse> CreateAsync(UserRequest request, CancellationToken cancellationToken)
    {
        var entity = new User();
        ApplyRequest(entity, request);
        _db.Users.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(entity);
    }

    public async Task<UserResponse?> UpdateAsync(int id, UserRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.Users.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
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
        var entity = await _db.Users.SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        _db.Users.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ApplyRequest(User entity, UserRequest request)
    {
        entity.FirstName = request.FirstName.Trim();
        entity.LastName = request.LastName.Trim();
        entity.Email = request.Email.Trim();
        entity.GraduationDate = request.GraduationDate;
    }

    private static UserResponse ToResponse(User entity) =>
        new UserResponse(entity.Id, entity.FirstName, entity.LastName, entity.Email, entity.GraduationDate);
}
