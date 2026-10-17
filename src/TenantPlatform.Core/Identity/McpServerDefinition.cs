namespace TenantPlatform.Core.Identity;

public enum McpConnectionAudience { Closed = 0, AccountRoles = 1, Selected = 2 }
public enum McpToolAudience { Closed = 0, Inherit = 1, Selected = 2 }
public enum McpToolUse { Unclassified = 0, AutomaticRead = 1, ConfirmationRequired = 2 }

public sealed class McpServerDefinition
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Name { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ProfileVersion { get; set; } = "";
    public McpConnectionAudience Audience { get; set; }
    public string AccessGrants { get; set; } = "[]";
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
    public McpToolAudience Audience { get; set; } = McpToolAudience.Inherit;
    public string AccessGrants { get; set; } = "[]";
    public McpToolUse UseClass { get; set; }
}
