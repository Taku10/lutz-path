namespace LutzPath.UserService.DTOs;

public record UserResponse(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    DateOnly? GraduationDate);
