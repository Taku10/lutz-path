using System.ComponentModel.DataAnnotations;
using LutzPath.JobApplicationService.Models;
using LutzPath.JobApplicationService.Validation;

namespace LutzPath.JobApplicationService.DTOs;

public class ApplicationRequest
{
    [Range(1, int.MaxValue)]
    public int UserId { get; set; }

    [Range(1, int.MaxValue)]
    public int JobId { get; set; }

    [EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;

    [UtcDateTime]
    public DateTime? DateApplied { get; set; }

    [UtcDateTime]
    public DateTime? InterviewDate { get; set; }

    [StringLength(10000)]
    public string? Notes { get; set; }
}
