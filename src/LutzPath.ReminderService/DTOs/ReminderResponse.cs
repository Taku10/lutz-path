namespace LutzPath.ReminderService.DTOs;

public record ReminderResponse(
    int Id,
    int UserId,
    int ApplicationId,
    string Title,
    DateTime DueDate,
    bool Completed);
