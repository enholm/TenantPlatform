namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestValidationException : Exception
{
    public ServiceRequestValidationException(string message)
        : base(message)
    {
    }
}

