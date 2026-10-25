namespace LutzPath.JobApplicationService.Exceptions;

public sealed class ServiceUnavailableException : Exception
{
    public ServiceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
