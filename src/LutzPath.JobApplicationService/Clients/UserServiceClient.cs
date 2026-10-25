using System.Net;
using LutzPath.JobApplicationService.Exceptions;
using LutzPath.JobApplicationService.Interfaces;

namespace LutzPath.JobApplicationService.Clients;

public class UserServiceClient : IUserServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<UserServiceClient> _logger;

    public UserServiceClient(HttpClient httpClient, ILogger<UserServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> UserExistsAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/users/{userId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException("User Service returned an unexpected response.", null, response.StatusCode);
            }

            return true;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "User Service could not validate user {UserId}.", userId);
            throw new ServiceUnavailableException("User Service is unavailable. Please try again later.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A client timeout is different from the caller canceling their request.
            _logger.LogWarning(exception, "User Service timed out.");
            throw new ServiceUnavailableException("User Service did not respond in time. Please try again later.", exception);
        }
    }
}
