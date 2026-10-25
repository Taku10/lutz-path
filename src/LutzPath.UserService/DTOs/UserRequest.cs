using System.ComponentModel.DataAnnotations;

namespace LutzPath.UserService.DTOs;

// POST and PUT accept editable fields only; the database generates Id.
public class UserRequest
{
    [Required, StringLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;


    public DateOnly? GraduationDate { get; set; }
}
