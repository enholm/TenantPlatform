using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Mcp;

public sealed record McpAccessGrant(Guid? UserId = null, UserRole? Role = null, Guid? OrganizationId = null, Guid? BuildingId = null);
public sealed record McpAccessMember(Guid UserId, string Name);
public sealed record McpScopeLabel(Guid? OrganizationId, Guid? BuildingId, string Name);
public sealed record McpAccessDirectory(IReadOnlyList<McpAccessMember> Members, IReadOnlyList<RoleAssignment> Roles, IReadOnlyList<McpScopeLabel>? Scopes = null);
public interface IMcpAccessDirectory
{
    Task<McpAccessDirectory> ReadAsync(Guid account, CancellationToken ct);
}
// A small allow-list policy. A scoped role target must match ONE complete current assignment.
public static class McpAccessPolicy
{
    public static IReadOnlyList<McpAccessGrant>? Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<McpAccessGrant[]>(json, new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            return value is { Length: <= 100 } && value.All(Valid) ? value : null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return null; }
    }
    public static bool Valid(McpAccessGrant? grant) => grant is not null &&
        (grant.UserId is Guid user && user != Guid.Empty && grant.Role is null && grant.OrganizationId is null && grant.BuildingId is null ||
         grant.UserId is null && grant.Role is UserRole role && Enum.IsDefined(role) && grant.OrganizationId != Guid.Empty && grant.BuildingId != Guid.Empty);
    public static bool Matches(IReadOnlyList<McpAccessGrant> grants, AccountActorScope subject) => grants.Any(grant =>
        grant.UserId == subject.UserId || grant.UserId is null && subject.Assignments.Any(a =>
            a.AccountId == subject.AccountId && a.Role == grant.Role && a.OrganizationId == grant.OrganizationId && a.BuildingId == grant.BuildingId &&
            LegacyRolePermissions.Grants(a, Permissions.McpConnections.Use)));
    public static bool Connection(McpConnectionAudience policy, string json, AccountActorScope subject) => Parse(json) is { } grants && policy switch
    {
        McpConnectionAudience.AccountRoles => grants.Count == 0 && subject.Assignments.Any(a => a.AccountId == subject.AccountId && LegacyRolePermissions.Grants(a, Permissions.McpConnections.Use)),
        McpConnectionAudience.Selected => Matches(grants, subject),
        _ => false
    };
    public static bool Tool(McpToolAudience policy, string json, AccountActorScope subject) => Parse(json) is { } grants && policy switch
    {
        McpToolAudience.Inherit => grants.Count == 0,
        McpToolAudience.Selected => Matches(grants, subject),
        _ => false
    };
}
