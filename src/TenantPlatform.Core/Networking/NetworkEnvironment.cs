namespace TenantPlatform.Core.Networking;

public class NetworkEnvironment
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid BuildingId { get; set; }

    public string Name { get; set; } = string.Empty;

    public NetworkVendor Vendor { get; set; }

    public bool IsActive { get; set; } = true;
}
