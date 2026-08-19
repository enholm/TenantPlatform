namespace TenantPlatform.Core.Services;

public class ServiceRequestFieldValue
{
    public Guid Id { get; set; }

    public Guid ServiceRequestId { get; set; }

    public Guid ServiceDefinitionFieldId { get; set; }

    public string? Value { get; set; }
}
