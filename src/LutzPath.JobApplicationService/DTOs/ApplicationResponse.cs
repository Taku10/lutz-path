using LutzPath.JobApplicationService.Models;

namespace LutzPath.JobApplicationService.DTOs;

public record ApplicationResponse(
    int Id,
    int UserId,
    int JobId,
    ApplicationStatus Status,
    DateTime? DateApplied,
    DateTime? InterviewDate,
    string? Notes,
    DateOnly? DateFound,
    string? Priority,
    string? InterviewPrepNotes,
    string? ResumeTips,
    string? DsaTopicsToKnow,
    string? AdditionalSourceNotes);
