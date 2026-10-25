using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace LutzPath.JobApplicationService.Integrations;

public record SheetIdAssignment(int RowNumber, string SourceId, string ExpectedFingerprint);
public interface IGoogleSheetsIdWriter
{
    Task AssignAsync(List<SheetIdAssignment> assignments, CancellationToken cancellationToken);
}
public sealed class GoogleSheetsIdWriter(HttpClient http, IGoogleSheetsReader reader,
    IGoogleSheetsTokenProvider tokens, IOptions<GoogleSheetsOptions> options) : IGoogleSheetsIdWriter
{
    public async Task AssignAsync(List<SheetIdAssignment> assignments, CancellationToken cancellationToken)
    {
        if(assignments.Count==0)return;
        if(!options.Value.AssignMissingIds)throw new InvalidOperationException("ID assignment is disabled.");
        var rows=await reader.ReadAsync(cancellationToken);
        GoogleSheetRow.ValidateHeader(rows);
        foreach(var assignment in assignments) {
            if(assignment.RowNumber<2 || assignment.RowNumber>rows.Count || !Guid.TryParseExact(assignment.SourceId,"N",out _))
                throw new InvalidOperationException("Invalid ID assignment.");
            var row=new GoogleSheetRow(assignment.RowNumber,rows[assignment.RowNumber-1]);
            if(row.SourceId!="" || row.SourceHash!=assignment.ExpectedFingerprint)
                throw new InvalidOperationException("The sheet changed while assigning IDs. Retry on the next cycle.");
        }
        var tab="'"+options.Value.SheetName.Replace("'","''")+"'";
        // This writer deliberately exposes no general-purpose cell-write method.
        var data=assignments.Select(a=>new { range=$"{tab}!T{a.RowNumber}", values=new[]{new[]{a.SourceId}} }).ToArray();
        using var request=new HttpRequestMessage(HttpMethod.Post,$"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(options.Value.SpreadsheetId)}/values:batchUpdate");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await tokens.GetAsync(cancellationToken));
        request.Content=JsonContent.Create(new {valueInputOption="RAW",data});
        using var response=await http.SendAsync(request,cancellationToken);
        if(!response.IsSuccessStatusCode)throw new HttpRequestException($"Could not assign IDs (Google HTTP {(int)response.StatusCode}). Share the sheet with the service account as Editor.");
        // Readback must match both the ID and the source data before any database write.
        var confirmed=await reader.ReadAsync(cancellationToken);
        foreach(var assignment in assignments) {
            if(assignment.RowNumber>confirmed.Count)throw new InvalidOperationException("Rows moved during ID assignment. Sync paused.");
            var row=new GoogleSheetRow(assignment.RowNumber,confirmed[assignment.RowNumber-1]);
            if(row.SourceId!=assignment.SourceId || row.SourceHash!=assignment.ExpectedFingerprint)
                throw new InvalidOperationException("ID readback changed. Sync paused for review.");
        }
    }
}
