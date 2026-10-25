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
}
