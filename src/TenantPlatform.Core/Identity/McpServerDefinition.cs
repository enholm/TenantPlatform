namespace TenantPlatform.Core.Identity;

public sealed class McpServerDefinition
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Name { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileVersion { get; set; } = "";
    public bool Enabled { get; set; }
    public Guid Revision { get; set; }
    public Guid CredentialEpoch { get; set; }
    public DateTimeOffset? DiscoveredUtc { get; set; }
    public bool DiscoveryComplete { get; set; }
}
public sealed class McpDiscoveredTool
{
    public Guid ConnectionId { get; set; }
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public string InputSchema { get; set; } = "{}";
    public string? OutputSchema { get; set; }
    public DateTimeOffset DiscoveredUtc { get; set; }
    public bool Approved { get; set; }
}
