using System.Globalization;
using System.Text.RegularExpressions;
using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LutzPath.JobApplicationService.Integrations;

// Deliberately has no SaveChanges, POST, import, or background polling path.
public sealed class GoogleSheetsPreviewService(JobApplicationsDbContext db, IGoogleSheetsReader reader,
    IOptions<GoogleSheetsOptions> options)
{
    private static string Normalize(string? value) => Regex.Replace(value?.Trim() ?? "", @"\s+", " ").ToUpperInvariant();
    private static string Identity(string? company, string? title, string? location, string? url) =>
        System.Text.Json.JsonSerializer.Serialize(new[] { Normalize(company), Normalize(title), Normalize(location), url?.Trim() ?? "" });
    private static string Cell(List<string> row, int index) => index < row.Count ? row[index].Trim() : "";
    private static readonly string[] Headers = ["f", "City", "Role", "OPT Friendly", "H-1B Visa",
        "Posting Link", "Status", "Date Found", "Applied Date", "Follow-up Date", "Priority",
        "Comments", "Likely Interview Process", "Interview Prep Notes", "Resume Tips",
        "DSA Topics to Know", "Posting Availability", "Last Availability Check"];
    private static readonly Dictionary<string, string> Statuses = new(StringComparer.OrdinalIgnoreCase) {
        ["Not Applied"]="NotApplied", ["Saved"]="Saved", ["Applied"]="Applied", ["OA / Assessment"]="Assessment",
        ["Interviewing"]="Interview", ["Offer"]="Offer", ["Rejected"]="Rejected", ["Withdrawn"]="Withdrawn"
    };

    public async Task<SheetPreview> PreviewAsync(int userId, CancellationToken cancellationToken)
    {
        var rows = await reader.ReadAsync(cancellationToken);
        if (rows.Count == 0 || Headers.Where((header, index) => index == 0
                ? Cell(rows[0], 0) != "f" && Cell(rows[0], 0) != "Company"
                : Cell(rows[0], index) != header).Any())
            throw new InvalidOperationException("Sheet1 headers changed. Review the column mapping before continuing.");
        var applications = await db.Applications.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(cancellationToken);
        var ids = applications.Select(a => a.JobId).Distinct().ToList();
        var jobs = await db.Jobs.AsNoTracking().Where(j => ids.Contains(j.Id)).ToListAsync(cancellationToken);
        var bindings = await db.GoogleSheetBindings.AsNoTracking().Where(b => b.SpreadsheetId == options.Value.SpreadsheetId && b.SheetId == options.Value.SheetId && b.UserId == userId).ToListAsync(cancellationToken);
        var duplicatedIds = rows.Skip(1).Select(r => Cell(r,19)).Where(id => id != "").GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        var candidates = (from application in applications join job in jobs on application.JobId equals job.Id
            select new { application, job }).GroupBy(item => Identity(item.job.Company, item.job.Title, item.job.Location, item.job.JobUrl))
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<SheetPreviewRow>();
        var warnings = new List<string> {
            "Read-only preview: no spreadsheet or database records are changed.",
            bindings.Count > 0 ? "Rows are matched by reconciled permanent IDs. Unknown or duplicate IDs are never guessed." : "Stable source IDs have not been reconciled. Composite matches are suggestions only; row numbers and posting links alone are not identifiers.",
            "Application fields and reminders are protected. Follow-up dates require separate Reminder Service reconciliation.",
            "Unmatched rows may be new records or renamed existing records. They are not automatically inserted."
        };
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            var company = Cell(row, 0); var title = Cell(row, 2);
            if (company == "" && title == "")
            {
                if (row.Any(value => !string.IsNullOrWhiteSpace(value))) warnings.Add($"Row {index + 1}: cells without a company or role are not mapped to a job.");
                continue;
            }
            var rowWarnings = new List<string>();
            if (company == "" || title == "") rowWarnings.Add("Company or role is missing.");
            var key = Identity(company, title, Cell(row, 1), Cell(row, 5));
            var matches = candidates.GetValueOrDefault(key);
            var sourceId = Cell(row,19);
            if (sourceId != "") {
                var binding = bindings.SingleOrDefault(b => b.SourceId == sourceId);
                matches = binding is null ? null : candidates.Values.SelectMany(list => list)
                    .Where(item => item.job.Id == binding.JobId && item.application.Id == binding.ApplicationId).ToList();
                if (duplicatedIds.Contains(sourceId)) {
                    result.Add(new(index + 1, company, title, "Ambiguous", null, null, [], ["Duplicate LutzPath ID; sync skips this row."]));
                    continue;
                }
            }
            if (matches?.Count != 1)
            {
                result.Add(new(index + 1, company, title, matches?.Count > 1 ? "Ambiguous" : "Unmatched", null, null, [], rowWarnings));
                continue;
            }
            var item = matches[0]; var job = item.job; var app = item.application;
            var changes = new List<SheetFieldChange>();
            void Compare(string field, string? stored, string? source, bool protect = false)
            {
                if ((stored?.Trim() ?? "") != (source?.Trim() ?? "")) changes.Add(new(field, stored, source, protect));
            }
            Compare("Company", job.Company, company);
            Compare("Location", job.Location, Cell(row, 1));
            Compare("Title", job.Title, title);
            Compare("JobUrl", job.JobUrl, Cell(row, 5));
            Compare("OptFriendlyInformation", job.OptFriendlyInformation, Cell(row, 3));
            Compare("H1bVisaInformation", job.H1bVisaInformation, Cell(row, 4));
            Compare("LikelyInterviewProcess", job.LikelyInterviewProcess, Cell(row, 12));
            var availability = Cell(row, 16);
            if (availability is "Unknown / Recheck" or "") availability = "Unknown";
            if (availability is not ("Open" or "Closed" or "Unknown")) rowWarnings.Add("Unknown posting availability: " + availability);
            Compare("PostingAvailability", job.PostingAvailability, availability);
            Compare("AvailabilityCheckNotes", job.AvailabilityCheckNotes, Cell(row, 17));
            if (Statuses.TryGetValue(Cell(row, 6), out var status)) Compare("Status", app.Status.ToString(), status, true);
            else rowWarnings.Add("Unknown application status: " + Cell(row, 6));
            string? DateValue(string source, string field)
            {
                if (source == "") return null;
                DateTime parsed;
                if (double.TryParse(source, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is >= 1 and <= 2958465)
                    parsed = DateTime.FromOADate(serial);
                else if (!DateTime.TryParse(source, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out parsed))
                { rowWarnings.Add("Invalid " + field + ": " + source); return source; }
                if (field == "applied date" && parsed.Date > DateTime.UtcNow.Date) rowWarnings.Add("Applied date is in the future; original value retained.");
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            Compare("DateFound", app.DateFound?.ToString("yyyy-MM-dd"), DateValue(Cell(row, 7), "date found"), true);
            var appliedDate = app.DateApplied.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(app.DateApplied.Value, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")).ToString("yyyy-MM-dd") : null;
            Compare("DateApplied", appliedDate, DateValue(Cell(row, 8), "applied date"), true);
            Compare("Priority", app.Priority, Cell(row, 10), true);
            Compare("Notes", app.Notes, Cell(row, 11), true);
            Compare("InterviewPrepNotes", app.InterviewPrepNotes, Cell(row, 13), true);
            Compare("ResumeTips", app.ResumeTips, Cell(row, 14), true);
            Compare("DsaTopicsToKnow", app.DsaTopicsToKnow, Cell(row, 15), true);
            if (Cell(row, 9) != "") rowWarnings.Add("Follow-up date retained in source; reminder comparison is not enabled.");
            if (Cell(row, 18) != "") rowWarnings.Add("Unlabelled column S contains source information; it is not automatically overwritten.");
            result.Add(new(index + 1, company, title, "Matched", job.Id, app.Id, changes, rowWarnings));
        }
        return new(options.Value.SpreadsheetId, options.Value.SheetName, DateTime.UtcNow, result.Count,
            result.Count(r => r.Match == "Matched"), result.Count(r => r.Match == "Unmatched"),
            result.Count(r => r.Match == "Ambiguous"), result.Count(r => r.Changes.Count > 0), options.Value.Enabled && options.Value.AutomaticSyncEnabled, false, warnings, result);
    }
}
