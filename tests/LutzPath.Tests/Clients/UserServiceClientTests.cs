using System.Net;
using LutzPath.JobApplicationService.Clients;
using LutzPath.JobApplicationService.Exceptions;
using LutzPath.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace LutzPath.Tests.Clients;

public class UserServiceClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task UserExistsAsync_MapsLookupStatus(HttpStatusCode status, bool expected)
    {
        using var http = CreateHttp((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/users/7", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status));
        });
        var client = new UserServiceClient(http, NullLogger<UserServiceClient>.Instance);

        Assert.Equal(expected, await client.UserExistsAsync(7, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task UserExistsAsync_UnexpectedStatus_IsDependencyFailure(HttpStatusCode status)
    {
        using var http = CreateHttp((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        var client = new UserServiceClient(http, NullLogger<UserServiceClient>.Instance);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.UserExistsAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task UserExistsAsync_ConnectionFailure_IsDependencyFailure()
    {
        using var http = CreateHttp((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline")));
        var client = new UserServiceClient(http, NullLogger<UserServiceClient>.Instance);

        await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.UserExistsAsync(7, CancellationToken.None));
    }

    [Fact]
    public async Task UserExistsAsync_HttpTimeout_IsDependencyFailure()
    {
        using var http = CreateHttp(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        http.Timeout = TimeSpan.FromMilliseconds(100);
        var client = new UserServiceClient(http, NullLogger<UserServiceClient>.Instance);

        var exception = await Assert.ThrowsAsync<ServiceUnavailableException>(() =>
            client.UserExistsAsync(7, CancellationToken.None));

        Assert.Contains("did not respond in time", exception.Message);
    }

    [Fact]
    public async Task UserExistsAsync_CallerCancellation_RemainsCancellation()
    {
        using var http = CreateHttp((_, token) => Task.FromCanceled<HttpResponseMessage>(token));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var client = new UserServiceClient(http, NullLogger<UserServiceClient>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.UserExistsAsync(7, cancellation.Token));
    }

    private static HttpClient CreateHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new StubHttpMessageHandler(send)) { BaseAddress = new Uri("http://dependency/") };
}

