using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Mcp;

// Profiles contain no secrets/SDK types. They are approved by the operator, not supplied by GUI URLs.
public sealed record McpAuthProfile(string Id, string Version, string Name, string Endpoint, McpClientBinding Binding, bool Ready);
public interface IMcpAuthProfiles { IReadOnlyList<McpAuthProfile> GetAll(); }
public sealed record McpToolMetadata(string Name, string? Description, string InputSchema, string? OutputSchema);
public sealed record McpToolDiscovery(IReadOnlyList<McpToolMetadata> Tools, bool Complete);
public sealed record McpServerView(Guid Id, string Name, string ProfileId, string ProfileVersion, bool Enabled, Guid Revision,
    bool DiscoveryComplete, DateTimeOffset? DiscoveredUtc, McpConnectionAudience Audience, string AccessGrants);
public sealed record McpRegistryPage(bool CanManage, IReadOnlyList<McpServerView> Servers, IReadOnlyList<McpAuthProfile> Profiles);
public sealed record McpAvailableTool(Guid ConnectionId, string ConnectionName, string Name, string Version, string? Description,
    string InputSchema, string? OutputSchema, McpToolUse UseClass);
public enum McpAvailability { Available, AuthorizationRequired, Disabled, ProfileUnavailable, DiscoveryRequired, NoApprovedTools, ConfirmationRequired, AccessDenied }
public sealed record McpConnectionState(McpClientStatus? Personal, McpAvailability Reason);
public interface IMcpRegistryData
{
    Task<IReadOnlyList<McpServerDefinition>> ListAsync(Guid account, CancellationToken ct);
    Task<McpServerDefinition?> ReadAsync(Guid account, Guid connection, CancellationToken ct);
    Task SaveAsync(McpServerDefinition value, Guid? expectedRevision, bool invalidateTools, CancellationToken ct);
    Task<IReadOnlyList<McpDiscoveredTool>> ToolsAsync(Guid account, Guid connection, CancellationToken ct);
    Task<Guid> SaveDiscoveryAsync(Guid account, Guid connection, Guid revision, IReadOnlyList<McpDiscoveredTool> tools, bool complete, DateTimeOffset now, CancellationToken ct);
    Task SetToolPolicyAsync(Guid account, Guid connection, Guid revision, string name, string version, McpToolAudience audience, string grants, McpToolUse useClass, CancellationToken ct);
    Task ApproveAsync(Guid account, Guid connection, Guid revision, string name, string version, bool approved, CancellationToken ct);
}
public interface IMcpRegisteredClientFactory
{
    Task ForgetAsync(Guid user, Guid connection, CancellationToken ct);
    InternalMcpClientService Create(McpAuthProfile profile, Guid connection, IMcpConnectionAccess access, TenantPlatform.Application.Security.ICurrentActor actor);
}
