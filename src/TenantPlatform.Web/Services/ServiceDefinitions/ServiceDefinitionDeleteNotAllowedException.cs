namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class ServiceDefinitionDeleteNotAllowedException : Exception
{
    public ServiceDefinitionDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}

