using System.Text.Json;
using LutzPath.ResumeService.Models;
using Microsoft.EntityFrameworkCore;
namespace LutzPath.ResumeService.Services;

// One worker and one service instance: no broker or automatic paid retries.
public class GenerationWorker(IServiceScopeFactory scopes, ILogger<GenerationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ResumesDbContext>();
            await db.Drafts.Where(x => x.Status == "Generating").ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Failed").SetProperty(x => x.Error, "Generation was interrupted by a restart. Start a new generation to retry."), stoppingToken);
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ResumesDbContext>();
            try
            {
                var draft = await db.Drafts.OrderBy(x => x.Id).FirstOrDefaultAsync(x => x.Status == "Queued", stoppingToken);
                if (draft is null) { await Task.Delay(1500, stoppingToken); continue; }
                draft.Status = "Generating";
                await db.SaveChangesAsync(stoppingToken);
                try
                {
                    var client = scope.ServiceProvider.GetRequiredService<ITailoringClient>();
                    var result = await client.Generate(draft, stoppingToken);
                    if (!string.IsNullOrEmpty(draft.TemplateParagraphsJson))
                    {
                        var paragraphs = JsonSerializer.Deserialize<TemplateParagraph[]>(draft.TemplateParagraphsJson)!;
                        TailoringClient.Validate(result, paragraphs.Length);
                        TailoringClient.ValidateTemplate(result, paragraphs);
                        var replacements = result.Paragraphs.ToDictionary(p => p.ParagraphId, p => p.Text);
                        draft.Content = string.Join("\n", paragraphs.Select(p => replacements.GetValueOrDefault(p.Id, p.Text)));
                    }
                    else draft.Content = string.Join("\n", result.Paragraphs.Select(x => x.Text));
                    draft.ReviewNotes = result.ReviewNotes + "\n\nSource references (before manual edits):\n" + string.Join("\n", result.Paragraphs.Select((p, i) => $"Paragraph {(p.ParagraphId > 0 ? p.ParagraphId : i + 1)}: master lines {string.Join(", ", p.SourceLines)}")) + "\n\nReview every claim against the master. Source references do not prove factual accuracy. Confirm contact details, dates, metrics and skills before exporting.";
                    draft.Status = "Ready";
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    log.LogWarning("Resume generation {Id} failed: {Type}", draft.Id, ex.GetType().Name);
                    draft.Status = "Failed";
                    draft.Error = ex is InvalidOperationException ? ex.Message : "The AI service could not be reached or returned an invalid response. Start a new generation to retry.";
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError("Resume queue unavailable: {Type}", ex.GetType().Name); await Task.Delay(3000, stoppingToken); }
        }
    }
}
