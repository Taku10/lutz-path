namespace LutzPath.JobApplicationService.DTOs;

public record JobResponse(
    int Id,
    string Company,
    string Title,
    string? Location,
    string? JobUrl,
    string? Description,
    DateOnly? DatePosted,
    string? OptFriendlyInformation,
    string? H1bVisaInformation,
    string? LikelyInterviewProcess,
    string PostingAvailability,
    DateTime? LastAvailabilityCheck,
    string? AvailabilityCheckNotes);
