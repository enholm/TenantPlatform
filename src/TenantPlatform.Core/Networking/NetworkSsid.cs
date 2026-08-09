namespace TenantPlatform.Core.Networking;

public class NetworkSsid
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid TenantOrganizationId { get; set; }

    public Guid NetworkEnvironmentId { get; set; }

    public Guid? UnitId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int? VlanId { get; set; }

    public SsidSecurityType SecurityType { get; set; }

    public bool IsBroadcast { get; set; } = true;

    public NetworkSsidStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
