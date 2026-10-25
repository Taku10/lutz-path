namespace LutzPath.ReminderService.Models;

public class Reminder
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int ApplicationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public bool Completed { get; set; }
}
