using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.Integrations;
using LutzPath.JobApplicationService.Models;
using LutzPath.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LutzPath.Tests.Services;

public class GoogleSheetsSyncTests
{
    private const string SourceId="11111111111111111111111111111111";
    private sealed class Source : IGoogleSheetsReader, IGoogleSheetsIdWriter
    {
        public List<List<string>> Rows {get;set;}=[Header(),Row()];
        public bool Fail {get;set;}
        public int Assignments {get;private set;}
        public Task<List<List<string>>> ReadAsync(CancellationToken token) =>
            Fail?throw new HttpRequestException("Offline"):Task.FromResult(Rows);
        public Task AssignAsync(List<SheetIdAssignment> assignments,CancellationToken token) {
            foreach(var item in assignments) {
                var row=Rows[item.RowNumber-1];while(row.Count<20)row.Add("");
                row[19]=item.SourceId;Assignments++;
            }
            return Task.CompletedTask;
        }
    }
    private static List<string> Header()=>["f","City","Role","OPT Friendly","H-1B Visa","Posting Link","Status","Date Found","Applied Date","Follow-up Date","Priority","Comments","Likely Interview Process","Interview Prep Notes","Resume Tips","DSA Topics to Know","Posting Availability","Last Availability Check","","LutzPath ID"];
    private static List<string> Row(string sourceId=SourceId)=>["Example","Remote","Graduate","Conditional OPT","Role dependent","https://example.com/job","Applied","10/25/2026","","","Medium","Source notes","Recruiter screen","Practice","Highlight projects","Arrays","Closed","10/25/2026 09:00 AM CDT - closed","",sourceId];
    private static GoogleSheetsSyncService Service(JobApplicationsDbContext db,Source source,GoogleSheetsSyncCoordinator? coordinator=null) =>
        new(db,source,source,new FakeUserServiceClient(),coordinator??new(),Options.Create(new GoogleSheetsOptions {
            Enabled=true,AssignMissingIds=true,SpreadsheetId="test",SheetId=655736542,UserId=1
        }));
    private static async Task Seed(JobApplicationsDbContext db)
    {
        db.Jobs.Add(new Job {Company="Example",Title="Graduate",Location="Remote",JobUrl="https://example.com/job",PostingAvailability="Open",Description="Manual description"});
        await db.SaveChangesAsync();
        db.Applications.Add(new JobApplication {JobId=1,UserId=1,Status=ApplicationStatus.Interview,Notes="Personal notes",Priority="Very high",DateFound=new DateOnly(2026,9,1),ResumeTips="Personal resume notes"});
        await db.SaveChangesAsync();
        db.GoogleSheetBindings.Add(new GoogleSheetBinding {SpreadsheetId="test",SheetId=655736542,UserId=1,SourceId=SourceId,JobId=1,ApplicationId=1});
        await db.SaveChangesAsync();
    }
    [Fact]
    public async Task ExistingId_UpdatesPostingAfterReorderAndUrlChange_ProtectsApplicationAndIsIdempotent()
    {
        using var database=new TestDatabase<JobApplicationsDbContext>(o=>new(o));var db=database.Context;
        await Seed(db);var source=new Source();source.Rows[1][5]="https://example.com/new-url";
        source.Rows.Insert(1,[]);
        var service=Service(db,source);
        var first=await service.RunAsync(default);
        Assert.Equal(1,first.Updated);Assert.Equal(0,first.Created);
        db.ChangeTracker.Clear();
        var job=await db.Jobs.SingleAsync();var app=await db.Applications.SingleAsync();
        Assert.Equal("Closed",job.PostingAvailability);Assert.Equal("https://example.com/new-url",job.JobUrl);
        Assert.Equal(DateTimeKind.Utc,job.LastAvailabilityCheck!.Value.Kind);
        Assert.Equal("Manual description",job.Description);
        Assert.Equal("Personal notes",app.Notes);Assert.Equal("Very high",app.Priority);
        Assert.Equal(ApplicationStatus.Interview,app.Status);Assert.Equal(new DateOnly(2026,9,1),app.DateFound);
        Assert.Equal("Personal resume notes",app.ResumeTips);
        var second=await service.RunAsync(default);
        Assert.Equal(0,second.Updated);Assert.Equal(0,second.Created);
        Assert.Equal(1,await db.Jobs.CountAsync());Assert.Equal(1,await db.Applications.CountAsync());
    }
    [Fact]
    public async Task NewRow_GetsId_ImportsOnce_AndFutureEditsPreservePersonalNotes()
    {
        using var database=new TestDatabase<JobApplicationsDbContext>(o=>new(o));var source=new Source();
        source.Rows[1][19]="";var service=Service(database.Context,source);
        var first=await service.RunAsync(default);
        Assert.Equal(1,first.Created);Assert.Equal(1,source.Assignments);
        Assert.True(Guid.TryParseExact(source.Rows[1][19],"N",out _));
        Assert.Equal(ApplicationStatus.Applied,(await database.Context.Applications.SingleAsync()).Status);
        Assert.Equal("Source notes",(await database.Context.Applications.SingleAsync()).Notes);
        source.Rows[1][11]="Changed source notes";
        source.Rows[1][16]="Open";
        var second=await service.RunAsync(default);
        Assert.Equal(1,second.Updated);Assert.Equal(0,second.Created);
        Assert.Equal("Source notes",(await database.Context.Applications.SingleAsync()).Notes);
        Assert.Equal(1,await database.Context.GoogleSheetBindings.CountAsync());
    }
    [Fact]
    public async Task DuplicateOrMissingIds_NeverGuessOrCreateDuplicates()
    {
        using var database=new TestDatabase<JobApplicationsDbContext>(o=>new(o));await Seed(database.Context);
        var source=new Source();source.Rows.Add(Row());var report=await Service(database.Context,source).RunAsync(default);
        Assert.Equal(2,report.Skipped);Assert.Equal("Open",(await database.Context.Jobs.SingleAsync()).PostingAvailability);
        source.Rows=[Header(),Row("")];report=await Service(database.Context,source).RunAsync(default);
        Assert.Equal(1,report.Skipped);Assert.Equal(0,source.Assignments);Assert.Equal(1,await database.Context.Jobs.CountAsync());
    }
    [Fact]
    public async Task MissingRowsAndLocalDeletion_AreNotRecreatedOrDeletedBySync()
    {
        using var database=new TestDatabase<JobApplicationsDbContext>(o=>new(o));var db=database.Context;
        await Seed(db);var source=new Source {Rows=[Header()]};
        await Service(db,source).RunAsync(default);Assert.Equal(1,await db.Jobs.CountAsync());
        db.Applications.Remove(await db.Applications.SingleAsync());await db.SaveChangesAsync();
        source.Rows=[Header(),Row()];var result=await Service(db,source).RunAsync(default);
        Assert.Equal(1,result.Skipped);Assert.Empty(await db.Applications.ToListAsync());
        Assert.Equal(1,await db.GoogleSheetBindings.CountAsync());
    }
    [Fact]
    public async Task FailurePreservesJobsAndLastSuccess_AndIsReportedForRetry()
    {
        using var database=new TestDatabase<JobApplicationsDbContext>(o=>new(o));var db=database.Context;
        await Seed(db);var source=new Source();var service=Service(db,source);
        await service.RunAsync(default);var last=(await db.GoogleSheetSyncStates.SingleAsync()).LastSuccessUtc;
        source.Fail=true;await Assert.ThrowsAsync<HttpRequestException>(()=>service.RunAsync(default));
        db.ChangeTracker.Clear();var state=await db.GoogleSheetSyncStates.SingleAsync();
        Assert.Equal(last,state.LastSuccessUtc);Assert.NotNull(state.LastError);
        Assert.Equal(1,await db.Jobs.CountAsync());Assert.Equal("Personal notes",(await db.Applications.SingleAsync()).Notes);
    }
    [Fact]
    public async Task InvalidNewRowIsSkipped_AndConcurrentRunIsRejected()
    {
        using var database=new TestDatabase<JobApplicationsDbContext>(o=>new(o));var source=new Source();
        source.Rows[1][8]="not-a-date";var gate=new GoogleSheetsSyncCoordinator();var service=Service(database.Context,source,gate);
        var result=await service.RunAsync(default);Assert.Equal(1,result.Skipped);Assert.Empty(await database.Context.Jobs.ToListAsync());
        await gate.Gate.WaitAsync();
        try {await Assert.ThrowsAsync<InvalidOperationException>(()=>service.RunAsync(default));}
        finally{gate.Gate.Release();}
    }
}
