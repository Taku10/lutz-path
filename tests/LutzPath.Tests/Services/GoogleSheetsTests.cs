using System.Net;
using System.Text;
using System.Text.Json;
using LutzPath.JobApplicationService.Controllers;
using LutzPath.JobApplicationService.Data;
using LutzPath.JobApplicationService.Integrations;
using LutzPath.JobApplicationService.Models;
using LutzPath.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LutzPath.Tests.Services;

public class GoogleSheetsTests
{
    private static List<string> Header() => ["f","City","Role","OPT Friendly","H-1B Visa","Posting Link","Status","Date Found","Applied Date","Follow-up Date","Priority","Comments","Likely Interview Process","Interview Prep Notes","Resume Tips","DSA Topics to Know","Posting Availability","Last Availability Check"];
    private static List<string> Row(string title = "Graduate") =>
        ["Example","Remote",title,"Conditional OPT","Role dependent","https://example.com/job","Applied","10/25/2026","","","Medium","Sheet comment","Recruiter interview","Practice","Highlight projects","Arrays","Closed","2026-10-25 CT - closed"];
    private sealed class FakeReader(List<List<string>> rows) : IGoogleSheetsReader
    {
        public Task<List<List<string>>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(rows);
    }
    private sealed class FailedReader : IGoogleSheetsReader
    {
        public Task<List<List<string>>> ReadAsync(CancellationToken cancellationToken) => throw new HttpRequestException("Google Sheets returned HTTP 403.");
    }
    private sealed class FakeTokens : IGoogleSheetsTokenProvider
    {
        public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult("test-token");
    }
    private static IOptions<GoogleSheetsOptions> Settings() => Options.Create(new GoogleSheetsOptions { SpreadsheetId="test", SheetName="Sheet1", SheetId=655736542 });
    private static async Task Seed(JobApplicationsDbContext db)
    {
        db.Jobs.Add(new Job { Company="Example", Title="Graduate", Location="Remote", JobUrl="https://example.com/job", PostingAvailability="Open" });
        await db.SaveChangesAsync();
        db.Applications.Add(new JobApplication { UserId=1, JobId=1, Status=ApplicationStatus.Saved, Notes="Keep my manual notes" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Preview_MatchesCompositeIdentity_AndNeverWritesPostingOrApplicationChanges()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        await Seed(database.Context);
        var row = Row(); row[0] = " example ";
        var service = new GoogleSheetsPreviewService(database.Context, new FakeReader([Header(),row,Row("Different role")]), Settings());
        var report = await service.PreviewAsync(1, default);
        Assert.Equal(2,report.SourceRows);
        Assert.Equal(1,report.MatchedRows);
        Assert.Equal(1,report.UnmatchedRows);
        Assert.False(report.DatabaseModified);
        Assert.False(report.AutomaticUpdatesEnabled);
        Assert.Contains(report.Rows[0].Changes,c=>c.Field=="PostingAvailability" && !c.Protected && c.SheetValue=="Closed");
        Assert.Contains(report.Rows[0].Changes,c=>c.Field=="Notes" && c.Protected && c.DatabaseValue=="Keep my manual notes");
        database.Context.ChangeTracker.Clear();
        Assert.Equal("Open",(await database.Context.Jobs.SingleAsync()).PostingAvailability);
        Assert.Equal("Keep my manual notes",(await database.Context.Applications.SingleAsync()).Notes);
        Assert.Equal(ApplicationStatus.Saved,(await database.Context.Applications.SingleAsync()).Status);
        Assert.Equal(1,await database.Context.Applications.CountAsync());
    }

    [Fact]
    public async Task Preview_DoesNotGuessDuplicateMatchesOrUseAnotherProfilesApplications()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        await Seed(database.Context);
        database.Context.Applications.Add(new JobApplication { UserId=1, JobId=1 });
        await database.Context.SaveChangesAsync();
        var service = new GoogleSheetsPreviewService(database.Context,new FakeReader([Header(),Row()]),Settings());
        Assert.Equal(1,(await service.PreviewAsync(1,default)).AmbiguousRows);
        var other = await service.PreviewAsync(2,default);
        Assert.Equal(0,other.MatchedRows);
        Assert.Equal(1,other.UnmatchedRows);
    }

    [Fact]
    public async Task Preview_ValidatesHeadersAndKeepsFutureDatesAndOrphanCellsAsWarnings()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        await Seed(database.Context);
        var row = Row(); row[8] = "1/1/2099";
        var report = await new GoogleSheetsPreviewService(database.Context,new FakeReader([Header(),row,["","","","","","","","","","","","","","","","","","Standalone evidence"]]),Settings()).PreviewAsync(1,default);
        Assert.Equal(1,report.SourceRows);
        Assert.Contains(report.Rows[0].Warnings,w=>w.Contains("future"));
        Assert.Contains(report.Warnings,w=>w.Contains("Row 3"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>
            new GoogleSheetsPreviewService(database.Context,new FakeReader([["Wrong headers"]]),Settings()).PreviewAsync(1,default));
    }

    [Fact]
    public async Task Controller_DependencyFailureReturns503WithoutChangingData()
    {
        using var database = new TestDatabase<JobApplicationsDbContext>(options => new(options));
        await Seed(database.Context);
        var controller = new GoogleSheetsController(new GoogleSheetsPreviewService(database.Context,new FailedReader(),Settings()),Settings());
        var response = await controller.Preview(1,default);
        Assert.Equal(503,Assert.IsType<ObjectResult>(response.Result).StatusCode);
        Assert.Equal(1,await database.Context.Applications.CountAsync());
        Assert.Equal("Open",(await database.Context.Jobs.SingleAsync()).PostingAvailability);
    }

    [Fact]
    public async Task Reader_UsesExactTabAndReadOnlyGetRequests()
    {
        var temp = Path.GetTempFileName();
        try {
            var calls = new List<string>();
            var handler = new StubHttpMessageHandler((request, token)=>{
                Assert.Equal(HttpMethod.Get,request.Method);
                Assert.Equal("sheets.googleapis.com",request.RequestUri!.Host);
                Assert.Equal("test-token",request.Headers.Authorization!.Parameter);
                calls.Add(request.RequestUri.ToString());
                var body = calls.Count==1
                    ? """{"sheets":[{"properties":{"sheetId":20260906,"title":"Dashboard","gridProperties":{"rowCount":701}}},{"properties":{"sheetId":655736542,"title":"Sheet1","gridProperties":{"rowCount":946}}}]}"""
                    : JsonSerializer.Serialize(new { values = new[]{Header(),Row()} });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(body,Encoding.UTF8,"application/json") });
            });
            var options = Options.Create(new GoogleSheetsOptions { Enabled=true, SpreadsheetId="test", CredentialsPath=temp });
            var rows = await new GoogleSheetsReader(new HttpClient(handler),new FakeTokens(),options).ReadAsync(default);
            Assert.Equal(2,rows.Count);
            Assert.Contains("Sheet1",Uri.UnescapeDataString(calls[1]));
            Assert.Contains("A1:Z946",Uri.UnescapeDataString(calls[1]));
            Assert.Contains("FORMATTED_VALUE",calls[1]);
        } finally { File.Delete(temp); }
    }
}
