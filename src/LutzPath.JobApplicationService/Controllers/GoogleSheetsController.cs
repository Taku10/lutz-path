using LutzPath.JobApplicationService.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using LutzPath.JobApplicationService.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LutzPath.JobApplicationService.Controllers;

[ApiController]
[Route("api/integrations/google-sheets")]
public sealed class GoogleSheetsController(GoogleSheetsPreviewService preview, IOptions<GoogleSheetsOptions> options,
    GoogleSheetsSyncService? sync = null, JobApplicationsDbContext? db = null) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var state = db is null ? null : await db.GoogleSheetSyncStates.AsNoTracking().SingleOrDefaultAsync(e => e.Id == 1, cancellationToken);
        return Ok(new {
        enabled = options.Value.Enabled, spreadsheetId = options.Value.SpreadsheetId,
        sheetName = options.Value.SheetName,
        automaticUpdatesEnabled = options.Value.Enabled && options.Value.AutomaticSyncEnabled,
        mode = options.Value.AutomaticSyncEnabled ? "AutomaticPostingSync" : "ReadOnlyPreview",
        intervalSeconds = Math.Clamp(options.Value.IntervalSeconds,30,86400), userId = options.Value.UserId,
        state?.LastAttemptUtc, state?.LastSuccessUtc, state?.LastError, state?.Created, state?.Updated, state?.Skipped,
        warnings = state is null ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(state.WarningsJson)
    });
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        if (sync is null || !options.Value.Enabled) return Problem(statusCode:503,title:"Google Sheets is disabled.");
        try { return Ok(await sync.RunAsync(cancellationToken)); }
        catch (InvalidOperationException exception) { return Problem(statusCode:409,title:"Sync needs attention",detail:exception.Message); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        { return Problem(statusCode:503,title:"Google Sheets sync failed",detail:"Saved data is retained. Check sheet access, headers and IDs, then retry."); }
    }

    [HttpGet("preview")]
    public async Task<ActionResult<SheetPreview>> Preview([FromQuery] int userId, CancellationToken cancellationToken)
    {
        if (userId < 1) return Problem(statusCode: 400, title: "A positive userId is required.");
        try { return Ok(await preview.PreviewAsync(userId, cancellationToken)); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Problem(statusCode: 503, title: "Google Sheets timed out", detail: "Your saved data is unchanged. Try again."); }
        catch (HttpRequestException exception)
        { return Problem(statusCode: 503, title: "Google Sheets unavailable", detail: exception.Message); }
        catch (InvalidOperationException exception)
        { return Problem(statusCode: 503, title: "Google Sheets setup needs attention", detail: exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Google.Apis.Auth.OAuth2.Responses.TokenResponseException or System.Security.Cryptography.CryptographicException or ArgumentException)
        { return Problem(statusCode: 503, title: "Google Sheets authentication failed", detail: "Check the mounted service-account JSON file and sheet sharing. Your saved data is unchanged."); }
    }
}
