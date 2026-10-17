namespace TenantPlatform.Application.ServiceRequests;

public class CreateServiceRequestFieldValueRequest
{
    public Guid ServiceDefinitionFieldId { get; init; }

    public string? Value { get; init; }
}

