using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace LutzPath.JobApplicationService.Integrations;

public interface IGoogleSheetsTokenProvider
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

public sealed class GoogleSheetsTokenProvider(IOptions<GoogleSheetsOptions> options) : IGoogleSheetsTokenProvider
{
    private readonly Lazy<GoogleCredential> _credential = new(() => {
        using var stream = File.OpenRead(options.Value.CredentialsPath);
        var serviceAccount = ServiceAccountCredential.FromServiceAccountData(stream);
        return GoogleCredential.FromServiceAccountCredential(serviceAccount)
            .CreateScoped(options.Value.AssignMissingIds
                ? "https://www.googleapis.com/auth/spreadsheets"
                : "https://www.googleapis.com/auth/spreadsheets.readonly");
    });

    public Task<string> GetAsync(CancellationToken cancellationToken) =>
        _credential.Value.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: cancellationToken);
}

public sealed class GoogleSheetsReader(HttpClient http, IGoogleSheetsTokenProvider tokens,
    IOptions<GoogleSheetsOptions> options) : IGoogleSheetsReader
{
    public async Task<List<List<string>>> ReadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) throw new InvalidOperationException("Google Sheets is disabled. Start Compose with docker-compose.sheets.yml.");
        if (string.IsNullOrWhiteSpace(settings.SpreadsheetId) || !File.Exists(settings.CredentialsPath))
            throw new InvalidOperationException("Google Sheets credentials or spreadsheet settings are missing.");
        var token = await tokens.GetAsync(cancellationToken);
        using var metadata = await GetAsync($"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(settings.SpreadsheetId)}?fields=sheets(properties(sheetId,title,gridProperties(rowCount)))", token, cancellationToken);
        var sheet = metadata.RootElement.GetProperty("sheets").EnumerateArray()
            .Select(element => element.GetProperty("properties"))
            .SingleOrDefault(element => element.GetProperty("sheetId").GetInt32() == settings.SheetId);
        if (sheet.ValueKind == JsonValueKind.Undefined || sheet.GetProperty("title").GetString() != settings.SheetName)
            throw new InvalidOperationException("Configured Sheet1 tab does not match the live spreadsheet. Check the tab ID and name.");
        var rowCount = sheet.GetProperty("gridProperties").GetProperty("rowCount").GetInt32();
        // Refuse a truncated preview; increase the configured bound explicitly for larger sheets.
        if (settings.MaxRows < 1 || rowCount > settings.MaxRows)
            throw new InvalidOperationException("The sheet exceeds the configured preview row limit. Increase GoogleSheets:MaxRows.");
        var range = "'" + settings.SheetName.Replace("'", "''") + "'!A1:Z" + rowCount;
        using var values = await GetAsync($"https://sheets.googleapis.com/v4/spreadsheets/{Uri.EscapeDataString(settings.SpreadsheetId)}/values/{Uri.EscapeDataString(range)}?valueRenderOption=FORMATTED_VALUE", token, cancellationToken);
        if (!values.RootElement.TryGetProperty("values", out var rows)) return [];
        return rows.EnumerateArray().Select(row => row.EnumerateArray()
            .Select(cell => cell.ValueKind == JsonValueKind.String ? cell.GetString()! : cell.ToString()).ToList()).ToList();
    }

    private async Task<JsonDocument> GetAsync(string url, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Google Sheets returned HTTP {(int)response.StatusCode}. For 403, check the Sheets API is enabled and the spreadsheet is shared with the service account as Viewer.", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}
