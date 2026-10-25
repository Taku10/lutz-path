using System.Net;
using System.Text;
using LutzPath.ReminderService.Clients;
using LutzPath.ReminderService.Exceptions;
using LutzPath.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace LutzPath.Tests.Clients;

public class ApplicationServiceClientTests
{
    [Fact]
    public async Task GetApplicationAsync_ReadsOnlyItsRequiredContract()
    {
        using var http = CreateHttp((request, _) =>
        {
            Assert.Equal("/api/applications/11", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse("{\"id\":11,\"userId\":7,\"status\":\"Interview\"}"));
        });
        var client = new ApplicationServiceClient(http, NullLogger<ApplicationServiceClient>.Instance);

        var result = await client.GetApplicationAsync(11, CancellationToken.None);

        Assert.Equal(new ApplicationReference(11, 7), result);
    }

    [Fact]
    public async Task GetApplicationAsync_NotFound_ReturnsNull()
    {
        using var http = CreateHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        var client = new ApplicationServiceClient(http, NullLogger<ApplicationServiceClient>.Instance);

        Assert.Null(await client.GetApplicationAsync(11, CancellationToken.None));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"id\":11}")]
    [InlineData("{\"id\":12,\"userId\":7}")]
    [InlineData("{\"id\":11,\"userId\":0}")]
    public async Task GetApplicationAsync_InvalidPayload_IsDependencyFailure(string json)
    {
        using var http = CreateHttp((_, _) => Task.FromResult(JsonResponse(json)));
        var client = new ApplicationServiceClient(http, NullLogger<ApplicationServiceClient>.Instance);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.GetApplicationAsync(11, CancellationToken.None));
    }

    [Fact]
    public async Task GetApplicationAsync_ServerFailure_IsDependencyFailure()
    {
        using var http = CreateHttp((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        var client = new ApplicationServiceClient(http, NullLogger<ApplicationServiceClient>.Instance);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.GetApplicationAsync(11, CancellationToken.None));
    }

    [Fact]
    public async Task GetApplicationAsync_HttpTimeout_IsDependencyFailure()
    {
        using var http = CreateHttp(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return JsonResponse("{}");
        });
        http.Timeout = TimeSpan.FromMilliseconds(100);
        var client = new ApplicationServiceClient(http, NullLogger<ApplicationServiceClient>.Instance);

        var exception = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.GetApplicationAsync(11, CancellationToken.None));

        Assert.Contains("did not respond in time", exception.Message);
    }

    [Fact]
    public async Task GetApplicationAsync_CallerCancellation_RemainsCancellation()
    {
        using var http = CreateHttp((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new ApplicationServiceClient(http, NullLogger<ApplicationServiceClient>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetApplicationAsync(11, cancellation.Token));
    }

    private static HttpClient CreateHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHttpMessageHandler(send)) { BaseAddress = new Uri("http://dependency/") };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
}

