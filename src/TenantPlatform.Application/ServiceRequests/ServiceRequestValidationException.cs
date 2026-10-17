namespace TenantPlatform.Application.ServiceRequests;

public class ServiceRequestValidationException : Exception
{
    public ServiceRequestValidationException(string message)
        : base(message)
    {
    }
}

