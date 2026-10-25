using System.ComponentModel.DataAnnotations;

namespace LutzPath.JobApplicationService.DTOs;

// POST and PUT accept editable fields only; the database generates Id.
public class JobRequest
{
    [Required, StringLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [StringLength(200)]
    public string? Location { get; set; }

    [Url, StringLength(2048)]
    public string? JobUrl { get; set; }

    [StringLength(10000)]
    public string? Description { get; set; }


    public DateOnly? DatePosted { get; set; }
}
