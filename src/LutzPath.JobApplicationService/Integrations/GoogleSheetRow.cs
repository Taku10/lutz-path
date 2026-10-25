using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LutzPath.JobApplicationService.DTOs;
using LutzPath.JobApplicationService.Models;

namespace LutzPath.JobApplicationService.Integrations;

public sealed record GoogleSheetRow(int RowNumber, List<string> Values)
{
    public const int IdColumn = 19; // T; S contains existing unlabelled notes.
    public string Cell(int index) => index < Values.Count ? Values[index].Trim() : "";
    public string SourceId => Cell(IdColumn);
    public bool HasJob => Cell(0) != "" || Cell(2) != "";
    public string SourceHash => Fingerprint(Values);
    public string Identity => IdentityFor(Cell(0), Cell(2), Cell(1), Cell(5));
    public static string Fingerprint(List<string> cells) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Enumerable.Range(0,19).Select(i => i < cells.Count ? cells[i].Trim() : "")))));
    public static string IdentityFor(string company, string title, string? location, string? url) =>
        JsonSerializer.Serialize(new[] { Normalize(company), Normalize(title), Normalize(location), url?.Trim() ?? "" });
    private static string Normalize(string? value) => Regex.Replace(value?.Trim() ?? "", @"\s+", " ").ToUpperInvariant();
    public static void ValidateHeader(List<List<string>> values)
    {
        string[] headers = ["f","City","Role","OPT Friendly","H-1B Visa","Posting Link","Status","Date Found","Applied Date","Follow-up Date","Priority","Comments","Likely Interview Process","Interview Prep Notes","Resume Tips","DSA Topics to Know","Posting Availability","Last Availability Check"];
        if (values.Count == 0) throw new InvalidOperationException("Sheet1 is empty.");
        var row = new GoogleSheetRow(1,values[0]);
        for (var i=0;i<headers.Length;i++)
            if (row.Cell(i) != headers[i] && !(i==0 && row.Cell(0)=="Company"))
                throw new InvalidOperationException("Sheet1 headers changed. Sync stopped without changing jobs.");
        if (row.Cell(IdColumn) != "LutzPath ID")
            throw new InvalidOperationException("Column T must contain the reconciled LutzPath ID header. Sync is paused.");
    }
    public JobRequest Posting()
    {
        var availability = Cell(16) is "" or "Unknown / Recheck" ? "Unknown" : Cell(16);
        return new JobRequest {
            Company=Cell(0), Title=Cell(2), Location=Null(1), JobUrl=Null(5),
            OptFriendlyInformation=Null(3), H1bVisaInformation=Null(4), LikelyInterviewProcess=Null(12),
            PostingAvailability=availability, AvailabilityCheckNotes=Null(17), LastAvailabilityCheck=CheckTimestamp(Cell(17))
        };
    }
    private string? Null(int column) => Cell(column)=="" ? null : Cell(column);
    public ApplicationRequest NewApplication(int userId)
    {
        var notes = new List<string>();
        if (Cell(18)!="") notes.Add("Unlabelled source column S: "+Cell(18));
        if (Cell(9)!="") notes.Add("Follow-up date from sheet (create a reminder in LutzPath): "+Cell(9));
        var applied = Date(Cell(8));
        if (applied > DateOnly.FromDateTime(DateTime.UtcNow)) notes.Add("Future applied date retained from sheet: "+Cell(8));
        return new ApplicationRequest {
            UserId=userId, JobId=1, Status=Status(Cell(6)), DateFound=Date(Cell(7)),
            DateApplied=applied.HasValue ? TimeZoneInfo.ConvertTimeToUtc(applied.Value.ToDateTime(new TimeOnly(12),DateTimeKind.Unspecified),TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")) : null,
            Priority=Null(10), Notes=Null(11), InterviewPrepNotes=Null(13), ResumeTips=Null(14),
            DsaTopicsToKnow=Null(15), AdditionalSourceNotes=notes.Count>0?string.Join(Environment.NewLine,notes):null
        };
    }
    public List<string> ValidationErrors(bool creating, int userId)
    {
        var errors=new List<string>();
        try {
            var results = new List<ValidationResult>();
            var posting = Posting();
            Validator.TryValidateObject(posting,new ValidationContext(posting),results,true);
            errors.AddRange(results.Select(r=>r.ErrorMessage ?? "Invalid posting."));
            if (creating) {
                var app=NewApplication(userId); results.Clear();
                Validator.TryValidateObject(app,new ValidationContext(app),results,true);
                errors.AddRange(results.Select(r=>r.ErrorMessage ?? "Invalid application."));
            }
        } catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) {
            errors.Add("Invalid source date or status. Correct the row before it can be imported.");
        }
        return errors;
    }
    public static DateOnly? Date(string value)
    {
        if(value=="") return null;
        if(double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var serial) && serial is >=1 and <=2958465)
            return DateOnly.FromDateTime(DateTime.FromOADate(serial));
        return DateOnly.Parse(value,CultureInfo.GetCultureInfo("en-US"));
    }
    public static ApplicationStatus Status(string value) => value switch {
        "Not Applied" or "NotApplied" => ApplicationStatus.NotApplied, "" or "Saved" => ApplicationStatus.Saved,
        "Applied"=>ApplicationStatus.Applied, "OA / Assessment"=>ApplicationStatus.Assessment,
        "Interviewing"=>ApplicationStatus.Interview,"Offer"=>ApplicationStatus.Offer,
        "Rejected"=>ApplicationStatus.Rejected,"Withdrawn"=>ApplicationStatus.Withdrawn,
        _=>throw new FormatException("Unknown status")
    };
    public static DateTime? CheckTimestamp(string value)
    {
        var match=Regex.Match(value,@"^(?<stamp>(?:\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4})\s+\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?)\s+(?<zone>CDT|CST|CT)\b");
        if(!match.Success) return null; // Evidence text retains date-only/approximate checks.
        if(!DateTime.TryParse(match.Groups["stamp"].Value,CultureInfo.GetCultureInfo("en-US"),DateTimeStyles.None,out var date))return null;
        date=DateTime.SpecifyKind(date,DateTimeKind.Unspecified);
        return match.Groups["zone"].Value switch {
            "CDT"=>new DateTimeOffset(date,TimeSpan.FromHours(-5)).UtcDateTime,
            "CST"=>new DateTimeOffset(date,TimeSpan.FromHours(-6)).UtcDateTime,
            _=>TimeZoneInfo.ConvertTimeToUtc(date,TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"))
        };
    }
}
