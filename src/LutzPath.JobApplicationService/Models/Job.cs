namespace LutzPath.JobApplicationService.Models;

public class Job
{
    public int Id { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? JobUrl { get; set; }
    public string? Description { get; set; }
    public DateOnly? DatePosted { get; set; }
    public string? OptFriendlyInformation { get; set; }

    public string? H1bVisaInformation { get; set; }

    public string? LikelyInterviewProcess { get; set; }

    public string PostingAvailability { get; set; } = "Unknown";

    public DateTime? LastAvailabilityCheck { get; set; }

    public string? AvailabilityCheckNotes { get; set; }
}
