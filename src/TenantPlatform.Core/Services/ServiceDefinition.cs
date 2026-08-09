namespace TenantPlatform.Core.Services;

public class ServiceDefinition
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string Code { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
