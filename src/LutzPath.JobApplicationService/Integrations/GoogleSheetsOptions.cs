namespace LutzPath.JobApplicationService.Integrations;

public sealed class GoogleSheetsOptions
{
    public bool Enabled { get; set; }
    public bool AutomaticSyncEnabled { get; set; }
    public bool AssignMissingIds { get; set; }
    public int IntervalSeconds { get; set; } = 300;
    public int UserId { get; set; } = 1;
    public string SpreadsheetId { get; set; } = "";
    public string SheetName { get; set; } = "Sheet1";
    public int SheetId { get; set; } = 655736542;
    public int MaxRows { get; set; } = 5000;
    public string CredentialsPath { get; set; } = "";
}
public interface IGoogleSheetsReader
{
    Task<List<List<string>>> ReadAsync(CancellationToken cancellationToken);
}
public record SheetFieldChange(string Field, string? DatabaseValue, string? SheetValue, bool Protected);
public record SheetPreviewRow(int RowNumber, string Company, string Title, string Match,
    int? JobId, int? ApplicationId, List<SheetFieldChange> Changes, List<string> Warnings);
public record SheetPreview(string SpreadsheetId, string SheetName, DateTime CheckedAtUtc,
    int SourceRows, int MatchedRows, int UnmatchedRows, int AmbiguousRows,
    int RowsWithDifferences, bool AutomaticUpdatesEnabled, bool DatabaseModified,
    List<string> Warnings, List<SheetPreviewRow> Rows);
