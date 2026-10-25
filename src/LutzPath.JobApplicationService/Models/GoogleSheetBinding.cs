namespace LutzPath.JobApplicationService.Models;

public class GoogleSheetBinding
{
    public int Id { get; set; }
    public string SpreadsheetId { get; set; } = "";
    public int SheetId { get; set; }
    public int UserId { get; set; }
    public string SourceId { get; set; } = "";
    public int JobId { get; set; }
    public int ApplicationId { get; set; }
    public string LastSourceHash { get; set; } = "";
}

public class GoogleSheetSyncState
{
    public int Id { get; set; } = 1;
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public string? LastError { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public string WarningsJson { get; set; } = "[]";
}
