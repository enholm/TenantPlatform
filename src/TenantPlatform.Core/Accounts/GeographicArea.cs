namespace TenantPlatform.Core.Accounts;

public sealed class GeographicArea
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
