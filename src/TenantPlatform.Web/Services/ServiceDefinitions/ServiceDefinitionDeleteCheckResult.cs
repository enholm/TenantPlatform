namespace TenantPlatform.Web.Services.ServiceDefinitions;

public class ServiceDefinitionDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public string? Reason { get; init; }

    public static ServiceDefinitionDeleteCheckResult Allowed() =>
        new()
        {
            CanDelete = true
        };

    public static ServiceDefinitionDeleteCheckResult NotAllowed(
        string reason) =>
        new()
        {
            CanDelete = false,
            Reason = reason
        };
}

