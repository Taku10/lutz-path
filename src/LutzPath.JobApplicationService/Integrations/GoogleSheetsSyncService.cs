using System.Text.Json;
using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.Interfaces;
using LutzPath.JobApplicationService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LutzPath.JobApplicationService.Integrations;

public sealed class GoogleSheetsSyncCoordinator { public SemaphoreSlim Gate { get; } = new(1,1); }
public record GoogleSheetsSyncResult(int Created, int Updated, int Skipped, DateTime CompletedAtUtc, List<string> Warnings);

public sealed class GoogleSheetsSyncService(JobApplicationsDbContext db, IGoogleSheetsReader reader,
    IGoogleSheetsIdWriter writer, IUserServiceClient users, GoogleSheetsSyncCoordinator coordinator,
    IOptions<GoogleSheetsOptions> options)
{
    public async Task<GoogleSheetsSyncResult> RunAsync(CancellationToken cancellationToken)
    {
        var settings=options.Value;
        if(!settings.Enabled)throw new InvalidOperationException("Google Sheets is disabled.");
        if(settings.UserId<1)throw new InvalidOperationException("A sync profile is required.");
        if(!await coordinator.Gate.WaitAsync(0,cancellationToken))throw new InvalidOperationException("A sync is already running.");
        try {
            var state=await db.GoogleSheetSyncStates.SingleOrDefaultAsync(e=>e.Id==1,cancellationToken);
            if(state is null){ state=new();db.GoogleSheetSyncStates.Add(state); }
            state.LastAttemptUtc=DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            try {
                var values=await reader.ReadAsync(cancellationToken);
                GoogleSheetRow.ValidateHeader(values);
                var bindings=await db.GoogleSheetBindings.Where(b=>b.SpreadsheetId==settings.SpreadsheetId && b.SheetId==settings.SheetId).ToListAsync(cancellationToken);
                var jobs=await db.Jobs.ToListAsync(cancellationToken);
                var applications=await db.Applications.AsNoTracking().ToListAsync(cancellationToken);
                var assignments=new List<SheetIdAssignment>();
                for(var i=1;i<values.Count;i++) {
                    var row=new GoogleSheetRow(i+1,values[i]);
                    if(!row.HasJob || row.SourceId!="" || !settings.AssignMissingIds)continue;
                    if(row.ValidationErrors(true,settings.UserId).Count>0)continue;
                    // Existing rows must be explicitly reconciled; do not assign a fresh ID to a record whose ID was cleared.
                    if(jobs.Any(j=>GoogleSheetRow.IdentityFor(j.Company,j.Title,j.Location,j.JobUrl)==row.Identity))continue;
                    assignments.Add(new(row.RowNumber,Guid.NewGuid().ToString("N"),row.SourceHash));
                }
                if(assignments.Count>0) {
                    await writer.AssignAsync(assignments,cancellationToken);
                    values=await reader.ReadAsync(cancellationToken);
                    GoogleSheetRow.ValidateHeader(values);
                }
                var rows=values.Skip(1).Select((v,i)=>new GoogleSheetRow(i+2,v)).Where(r=>r.HasJob).ToList();
                var duplicates=rows.Where(r=>r.SourceId!="").GroupBy(r=>r.SourceId,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var bySource=bindings.ToDictionary(b=>b.SourceId,StringComparer.OrdinalIgnoreCase);
                var warnings=new List<string>();
                int created=0,updated=0,skipped=0;
                bool userChecked=false;
                await using var transaction=await db.Database.BeginTransactionAsync(cancellationToken);
                foreach(var row in rows) {
                    void Skip(string reason){skipped++;warnings.Add($"Row {row.RowNumber}: {reason}");}
                    if(row.SourceId=="" || !Guid.TryParseExact(row.SourceId,"N",out _)){Skip("missing or invalid LutzPath ID; reconcile the row instead of guessing.");continue;}
                    if(duplicates.Contains(row.SourceId)){Skip("duplicate LutzPath ID; all rows with this ID are skipped.");continue;}
                    var binding=bySource.GetValueOrDefault(row.SourceId);
                    var errors=row.ValidationErrors(binding is null,settings.UserId);
                    if(errors.Count>0){Skip(string.Join(" ",errors));continue;}
                    if(binding is not null) {
                        if(binding.UserId!=settings.UserId){Skip("ID belongs to a different profile.");continue;}
                        var job=jobs.SingleOrDefault(j=>j.Id==binding.JobId);
                        var app=applications.SingleOrDefault(a=>a.Id==binding.ApplicationId && a.JobId==binding.JobId && a.UserId==settings.UserId);
                        if(job is null || app is null){Skip("previously linked record was removed locally; it will not be recreated.");continue;}
                        var localIdentity=GoogleSheetRow.IdentityFor(job.Company,job.Title,job.Location,job.JobUrl);
                        if(localIdentity!=row.Identity && jobs.Any(j=>j.Id!=job.Id && GoogleSheetRow.IdentityFor(j.Company,j.Title,j.Location,j.JobUrl)==row.Identity)) {
                            Skip("ID appears to have moved to a different existing job. Sort the entire row including column T, or reconcile this ID.");continue;
                        }
                        // The same source hash does not prove the local posting still matches.
                        // Reconcile sheet-owned fields even after a local edit or an older import.
                        var before=JsonSerializer.Serialize(job);
                        ApplyPosting(job,row);
                        if(before!=JsonSerializer.Serialize(job))updated++;
                        binding.LastSourceHash=row.SourceHash;
                        // Application records are never updated by this path.
                    } else {
                        if(jobs.Any(j=>GoogleSheetRow.IdentityFor(j.Company,j.Title,j.Location,j.JobUrl)==row.Identity)){
                            Skip("unbound ID matches an existing job; reconcile it to avoid a duplicate.");continue;
                        }
                        if(!userChecked) {
                            if(!await users.UserExistsAsync(settings.UserId,cancellationToken))throw new InvalidOperationException("Sync profile does not exist.");
                            userChecked=true;
                        }
                        var job=new Job(); ApplyPosting(job,row);db.Jobs.Add(job);
                        await db.SaveChangesAsync(cancellationToken);
                        var request=row.NewApplication(settings.UserId);
                        var application=new JobApplication {
                            UserId=settings.UserId, JobId=job.Id, Status=request.Status, DateApplied=request.DateApplied,
                            DateFound=request.DateFound, Priority=request.Priority, Notes=request.Notes,
                            InterviewPrepNotes=request.InterviewPrepNotes, ResumeTips=request.ResumeTips,
                            DsaTopicsToKnow=request.DsaTopicsToKnow, AdditionalSourceNotes=request.AdditionalSourceNotes
                        };
                        db.Applications.Add(application);await db.SaveChangesAsync(cancellationToken);
                        var added=new GoogleSheetBinding {
                            SpreadsheetId=settings.SpreadsheetId, SheetId=settings.SheetId, UserId=settings.UserId,
                            SourceId=row.SourceId,JobId=job.Id,ApplicationId=application.Id,LastSourceHash=row.SourceHash
                        };
                        db.GoogleSheetBindings.Add(added);bySource.Add(row.SourceId,added);
                        jobs.Add(job);applications.Add(application);created++;
                        if(row.Cell(9)!="")warnings.Add($"Row {row.RowNumber}: follow-up date retained in source notes; create its reminder in LutzPath.");
                    }
                }
                state.LastSuccessUtc=DateTime.UtcNow;state.LastError=null;
                state.Created=created;state.Updated=updated;state.Skipped=skipped;state.WarningsJson=JsonSerializer.Serialize(warnings);
                await db.SaveChangesAsync(cancellationToken);await transaction.CommitAsync(cancellationToken);
                return new(created,updated,skipped,state.LastSuccessUtc.Value,warnings);
            } catch(Exception) {
                // Rollback scopes have completed. Persist only a sanitized failure status.
                db.ChangeTracker.Clear();
                var failed=await db.GoogleSheetSyncStates.SingleAsync(e=>e.Id==1,CancellationToken.None);
                failed.LastError="Sync failed. Saved data is retained. Check Google access, sheet headers, and source IDs, then retry.";
                await db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        } finally { coordinator.Gate.Release(); }
    }
    private static void ApplyPosting(Job job,GoogleSheetRow row)
    {
        var request=row.Posting();
        job.Company=request.Company;job.Title=request.Title;job.Location=request.Location;job.JobUrl=request.JobUrl;
        job.OptFriendlyInformation=request.OptFriendlyInformation;job.H1bVisaInformation=request.H1bVisaInformation;
        job.LikelyInterviewProcess=request.LikelyInterviewProcess;job.PostingAvailability=request.PostingAvailability;
        job.AvailabilityCheckNotes=request.AvailabilityCheckNotes;job.LastAvailabilityCheck=request.LastAvailabilityCheck;
    }
}

public sealed class GoogleSheetsSyncWorker(IServiceScopeFactory scopes,IOptions<GoogleSheetsOptions> options,
    ILogger<GoogleSheetsSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Value.Enabled || !options.Value.AutomaticSyncEnabled)return;
        var interval=TimeSpan.FromSeconds(Math.Clamp(options.Value.IntervalSeconds,30,86400));
        using var timer=new PeriodicTimer(interval);
        do {
            try {
                using var scope=scopes.CreateScope();
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                await scope.ServiceProvider.GetRequiredService<GoogleSheetsSyncService>().RunAsync(timeout.Token);
            } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception){logger.LogWarning("Google Sheets sync did not complete; saved jobs are retained and the next cycle will retry.");}
        } while(await timer.WaitForNextTickAsync(stoppingToken));
    }
}
