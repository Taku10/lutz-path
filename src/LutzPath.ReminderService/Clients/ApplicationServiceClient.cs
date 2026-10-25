using System.Net;
using System.Text.Json;
using LutzPath.ReminderService.Exceptions;
using LutzPath.ReminderService.Interfaces;

namespace LutzPath.ReminderService.Clients;

public class ApplicationServiceClient : IApplicationServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApplicationServiceClient> _logger;

    public ApplicationServiceClient(HttpClient httpClient, ILogger<ApplicationServiceClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ApplicationReference?> GetApplicationAsync(int applicationId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/applications/{applicationId}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException("Job Application Service returned an unexpected response.", null, response.StatusCode);
            }

            var application = await response.Content.ReadFromJsonAsync<ApplicationReference>(cancellationToken);
            if (application is null || application.Id != applicationId || application.UserId <= 0)
            {
                throw new JsonException("Job Application Service returned an invalid application.");
            }

            return application;
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Job Application Service could not validate application {ApplicationId}.", applicationId);
            throw new ServiceUnavailableException("Job Application Service is unavailable. Please try again later.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Job Application Service timed out.");
            throw new ServiceUnavailableException("Job Application Service did not respond in time. Please try again later.", exception);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Job Application Service returned an invalid response.");
            throw new ServiceUnavailableException("Job Application Service returned an invalid response. Please try again later.", exception);
        }
    }
}
