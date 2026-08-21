using TenantPlatform.Core.Services;

namespace TenantPlatform.Web.Services.ServiceRequests;

public class ServiceRequestFieldValueDto
{
    public Guid FieldId { get; init; }

    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public ServiceFieldType FieldType { get; init; }

    public int SortOrder { get; init; }

    public string? Value { get; init; }
}

