using System.ComponentModel.DataAnnotations;
using LutzPath.ReminderService.Validation;

namespace LutzPath.ReminderService.DTOs;

// POST and PUT accept editable fields only; the database generates Id.
public class ReminderRequest
{
    [Range(1, int.MaxValue)]
    public int UserId { get; set; }

    [Range(1, int.MaxValue)]
    public int ApplicationId { get; set; }

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, UtcDateTime]
    public DateTime? DueDate { get; set; }


    public bool Completed { get; set; }
}
