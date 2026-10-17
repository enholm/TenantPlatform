namespace TenantPlatform.Application.ServiceDefinitions;

public class ServiceDefinitionDeleteNotAllowedException : Exception
{
    public ServiceDefinitionDeleteNotAllowedException(string message)
        : base(message)
    {
    }
}

