namespace LutzPath.JobApplicationService.Models;

public class JobApplication
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int JobId { get; set; }
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Saved;
    public DateTime? DateApplied { get; set; }
    public DateTime? InterviewDate { get; set; }
    public string? Notes { get; set; }
    public DateOnly? DateFound { get; set; }

    public string? Priority { get; set; }

    public string? InterviewPrepNotes { get; set; }

    public string? ResumeTips { get; set; }

    public string? DsaTopicsToKnow { get; set; }

    public string? AdditionalSourceNotes { get; set; }
}
