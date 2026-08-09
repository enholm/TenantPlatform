namespace TenantPlatform.Core.Networking;

public class SsidRequestDetails
{
    public Guid Id { get; set; }

    public Guid ServiceRequestId { get; set; }

    public Guid NetworkEnvironmentId { get; set; }

    public SsidRequestAction Action { get; set; }

    public Guid? ExistingNetworkSsidId { get; set; }

    public string? RequestedName { get; set; }

    public int? RequestedVlanId { get; set; }

    public SsidSecurityType? RequestedSecurityType { get; set; }

    public bool? RequestedIsBroadcast { get; set; }
}
