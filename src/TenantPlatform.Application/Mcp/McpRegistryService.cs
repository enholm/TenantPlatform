using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Mcp;

public sealed class McpRegistryService(ICurrentActor actor, IAuthorizationService authorization, IMcpRegistryData data,
    IMcpAuthProfiles profiles, IMcpRegisteredClientFactory clients, TimeProvider clock, IMcpAccessDirectory directory)
{
    private async Task<Guid> Require(string permission, CancellationToken ct)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId is not Guid account ||
            !await authorization.CanAsync(permission, new(ResourceKind.Account, AccountId: account), ct)) throw new UnauthorizedAccessException();
        return account;
    }
    public async Task<McpRegistryPage> ListAsync(CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var manage = await authorization.CanAsync(Permissions.McpConnections.Manage, new(ResourceKind.Account, AccountId: account), ct);
        var subject = await authorization.GetAccountActorScopeAsync(account, ct);
        var rows = (await data.ListAsync(account, ct)).Where(row => manage || McpAccessPolicy.Connection(row.Audience, row.AccessGrants, subject)).ToArray();
        return new(manage, rows.Select(row => new McpServerView(row.Id, row.Name, row.ProfileId, row.ProfileVersion, row.Enabled,
            manage ? row.Revision : Guid.Empty, row.DiscoveryComplete, row.DiscoveredUtc,
            manage ? row.Audience : McpConnectionAudience.Closed, manage ? row.AccessGrants : "[]")).ToArray(),
            profiles.GetAll().Where(p => manage || rows.Any(row => row.ProfileId == p.Id)).ToArray());
    }

    public async Task<Guid> SaveAsync(Guid? id, Guid? revision, string name, string profileId, bool enabled, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Manage, ct);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100 || name.Any(char.IsControl)) throw new McpClientException("INVALID_ARGUMENT");
        var profile = profiles.GetAll().SingleOrDefault(x => x.Id == profileId) ?? throw new McpClientException("PROFILE_UNAVAILABLE");
        var row = id.HasValue ? await data.ReadAsync(account, id.Value, ct) ?? throw new UnauthorizedAccessException()
            : new McpServerDefinition { Id = Guid.NewGuid(), AccountId = account };
        var changed = row.ProfileId != profile.Id || row.ProfileVersion != profile.Version;
        row.Name = name.Trim(); row.ProfileId = profile.Id; row.ProfileVersion = profile.Version; row.Enabled = enabled;
        if (changed) { row.CredentialEpoch = Guid.NewGuid(); row.DiscoveryComplete = false; row.DiscoveredUtc = null; }
        row.Revision = Guid.NewGuid();
        await data.SaveAsync(row, id.HasValue ? revision ?? throw new McpClientException("INVALID_ARGUMENT") : null, changed, ct);
        return row.Id;
    }
    public async Task<McpClientStatus> StatusAsync(Guid connection, CancellationToken ct = default) => await (await ClientAsync(connection, ct)).StatusAsync(ct);
    public async Task<McpConnectRequest> BeginAsync(Guid connection, CancellationToken ct = default) => await (await ClientAsync(connection, ct)).BeginAsync(ct);
    public async Task CompleteAsync(Guid connection, CancellationToken ct = default) => await (await ClientAsync(connection, ct)).CompleteAsync(ct);
    private async Task<InternalMcpClientService> ClientAsync(Guid connection, CancellationToken ct = default)
    {
        return await ClientForAsync(connection, null, ct);
    }
    private async Task<InternalMcpClientService> ClientForAsync(Guid connection, McpDiscoveredTool? expectedTool, CancellationToken ct)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var row = await data.ReadAsync(account, connection, ct);
        if (row is null) throw new UnauthorizedAccessException();
        await RequireConnectionAccess(account, row, ct);
        var profile = profiles.GetAll().SingleOrDefault(x => x.Id == row?.ProfileId && x.Version == row.ProfileVersion && x.Ready);
        if (row is not { Enabled: true } || profile is null) throw new McpClientException("PROFILE_UNAVAILABLE");
        var guard = new Guard(async token =>
        {
            if (await Require(Permissions.McpConnections.Use, token) != account) throw new UnauthorizedAccessException();
            var fresh = await data.ReadAsync(account, connection, token);
            if (fresh is not { Enabled: true } || fresh.ProfileId != row.ProfileId || fresh.ProfileVersion != row.ProfileVersion || fresh.CredentialEpoch != row.CredentialEpoch ||
                !profiles.GetAll().Any(x => x.Id == profile.Id && x.Version == profile.Version && x.Ready)) throw new McpClientException("PROFILE_UNAVAILABLE");
            await RequireConnectionAccess(account, fresh, token);
            var currentSubject = await authorization.GetAccountActorScopeAsync(account, token);
            if (expectedTool is not null && (!fresh.DiscoveryComplete || (fresh.DiscoveredUtc is not { } discoveryDate || discoveryDate < clock.GetUtcNow().AddHours(-24)) ||
                !(await data.ToolsAsync(account, connection, token)).Any(x => x.Name == expectedTool.Name && x.Version == expectedTool.Version && x.Approved && x.UseClass == McpToolUse.AutomaticRead && McpAccessPolicy.Tool(x.Audience, x.AccessGrants, currentSubject))))
                throw new McpClientException("TOOL_NOT_APPROVED");
        });
        return clients.Create(profile with { Binding = profile.Binding with { ProfileVersion = profile.Version + ":" + row.CredentialEpoch.ToString("N") } }, connection, guard);
    }
    public async Task<bool> DisconnectAsync(Guid connection, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        if (await data.ReadAsync(account, connection, ct) is null) throw new UnauthorizedAccessException();
        InternalMcpClientService client;
        try { client = await ClientAsync(connection, ct); }
        catch (Exception ex) when (ex is McpClientException or UnauthorizedAccessException)
        {
            await clients.ForgetAsync(actor.GetSnapshot().UserId, connection, ct);
            return false; // Erased locally. Never forward an old token through a changed/disabled profile.
        }
        return await client.DisconnectAsync(ct);
    }
    public async Task<IReadOnlyList<McpDiscoveredTool>> ToolsAsync(Guid connection, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        if (await authorization.CanAsync(Permissions.McpConnections.Manage, new(ResourceKind.Account, AccountId: account), ct))
            return await data.ToolsAsync(account, connection, ct);
        await RequireConnectionAccess(account, row, ct);
        return (await AvailableToolsAsync(ct)).Where(t => t.ConnectionId == connection).Select(t => new McpDiscoveredTool {
            ConnectionId = t.ConnectionId, Name = t.Name, Version = t.Version, Description = t.Description,
            InputSchema = t.InputSchema, OutputSchema = t.OutputSchema, Approved = true, UseClass = t.UseClass,
            Audience = McpToolAudience.Closed, AccessGrants = "[]" }).ToArray();
    }
    private async Task RequireConnectionAccess(Guid account, McpServerDefinition row, CancellationToken ct)
    {
        var subject = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!McpAccessPolicy.Connection(row.Audience, row.AccessGrants, subject)) throw new UnauthorizedAccessException();
    }
    public async Task<McpAccessDirectory> AccessDirectoryAsync(CancellationToken ct = default) =>
        await directory.ReadAsync(await Require(Permissions.McpConnections.Manage, ct), ct);
    private async Task<string> ValidateGrants(Guid account, IReadOnlyList<McpAccessGrant> grants, bool selected, CancellationToken ct)
    {
        if (grants is null || grants.Count > 100 || !grants.All(McpAccessPolicy.Valid) || !selected && grants.Count != 0) throw new McpClientException("INVALID_ARGUMENT");
        var candidates = await directory.ReadAsync(account, ct);
        foreach (var grant in grants)
        {
            if (grant.UserId.HasValue && !candidates.Members.Any(x => x.UserId == grant.UserId) ||
                grant.Role.HasValue && (grant.OrganizationId.HasValue || grant.BuildingId.HasValue) &&
                !candidates.Roles.Any(x => x.Role == grant.Role && x.OrganizationId == grant.OrganizationId && x.BuildingId == grant.BuildingId))
                throw new McpClientException("INVALID_ARGUMENT");
        }
        var json = JsonSerializer.Serialize(grants.Distinct().ToArray());
        if (Encoding.UTF8.GetByteCount(json) > 20000) throw new McpClientException("INVALID_ARGUMENT");
        return json;
    }
    public async Task SetConnectionAccessAsync(Guid connection, Guid revision, McpConnectionAudience audience, IReadOnlyList<McpAccessGrant> grants, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Manage, ct);
        if (!Enum.IsDefined(audience)) throw new McpClientException("INVALID_ARGUMENT");
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        row.AccessGrants = await ValidateGrants(account, grants, audience == McpConnectionAudience.Selected, ct);
        row.Audience = audience; row.Revision = Guid.NewGuid();
        await data.SaveAsync(row, revision, false, ct);
    }
    public async Task SetToolPolicyAsync(Guid connection, string name, string version, McpToolAudience audience, IReadOnlyList<McpAccessGrant> grants, McpToolUse useClass, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Manage, ct);
        if (!Enum.IsDefined(audience) || !Enum.IsDefined(useClass)) throw new McpClientException("INVALID_ARGUMENT");
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        var json = await ValidateGrants(account, grants, audience == McpToolAudience.Selected, ct);
        await data.SetToolPolicyAsync(account, connection, row.Revision, name, version, audience, json, useClass, ct);
    }
    public async Task<McpConnectionState> DescribeAsync(Guid connection, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        var subject = await authorization.GetAccountActorScopeAsync(account, ct);
        if (!McpAccessPolicy.Connection(row.Audience, row.AccessGrants, subject))
        {
            if (!await authorization.CanAsync(Permissions.McpConnections.Manage, new(ResourceKind.Account, AccountId: account), ct)) throw new UnauthorizedAccessException();
            return new(null, McpAvailability.AccessDenied);
        }
        if (!row.Enabled) return new(null, McpAvailability.Disabled);
        if (!profiles.GetAll().Any(p => p.Id == row.ProfileId && p.Version == row.ProfileVersion && p.Ready)) return new(null, McpAvailability.ProfileUnavailable);
        var status = await StatusAsync(connection, ct);
        if (!status.Connected) return new(status, McpAvailability.AuthorizationRequired);
        if (!row.DiscoveryComplete || row.DiscoveredUtc is not { } last || last < clock.GetUtcNow().AddHours(-24)) return new(status, McpAvailability.DiscoveryRequired);
        var allowed = (await data.ToolsAsync(account, connection, ct)).Where(t => t.Approved && McpAccessPolicy.Tool(t.Audience, t.AccessGrants, subject)).ToArray();
        if (allowed.Any(t => t.UseClass == McpToolUse.AutomaticRead)) return new(status, McpAvailability.Available);
        return new(status, allowed.Any(t => t.UseClass == McpToolUse.ConfirmationRequired) ? McpAvailability.ConfirmationRequired : McpAvailability.NoApprovedTools);
    }
    public async Task<IReadOnlyList<McpAvailableTool>> AvailableToolsAsync(CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var result = new List<McpAvailableTool>();
        foreach (var row in await data.ListAsync(account, ct))
        {
            var subject = await authorization.GetAccountActorScopeAsync(account, ct);
            if (!McpAccessPolicy.Connection(row.Audience, row.AccessGrants, subject)) continue;
            var state = await DescribeAsync(row.Id, ct);
            if (state.Reason is not (McpAvailability.Available or McpAvailability.ConfirmationRequired)) continue;
            var client = await ClientAsync(row.Id, ct); // Fresh definition/user checks, no network or initialization.
            if (!(await client.StatusAsync(ct)).Connected) continue;
            var fresh = await data.ReadAsync(account, row.Id, ct);
            if (fresh is null || !fresh.Enabled || fresh.ProfileId != row.ProfileId || fresh.ProfileVersion != row.ProfileVersion ||
                fresh.CredentialEpoch != row.CredentialEpoch || !fresh.DiscoveryComplete || fresh.DiscoveredUtc < clock.GetUtcNow().AddHours(-24)) continue;
            subject = await authorization.GetAccountActorScopeAsync(account, ct);
            if (!McpAccessPolicy.Connection(fresh.Audience, fresh.AccessGrants, subject)) continue;
            var tools = await data.ToolsAsync(account, row.Id, ct);
            var final = await data.ReadAsync(account, row.Id, ct);
            if (final is null || final.Revision != fresh.Revision || await Require(Permissions.McpConnections.Use, ct) != account) continue;
            subject = await authorization.GetAccountActorScopeAsync(account, ct);
            if (!McpAccessPolicy.Connection(final.Audience, final.AccessGrants, subject)) continue;
            result.AddRange(tools.Where(t => t.Approved &&
                t.UseClass is McpToolUse.AutomaticRead or McpToolUse.ConfirmationRequired && McpAccessPolicy.Tool(t.Audience, t.AccessGrants, subject))
                .Select(t => new McpAvailableTool(row.Id, row.Name, t.Name, t.Version, t.Description, t.InputSchema, t.OutputSchema, t.UseClass)));
        }
        return result;
    }
    public async Task DiscoverAsync(Guid connection, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Manage, ct);
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        var client = await ClientAsync(connection, ct);
        var previous = await data.ToolsAsync(account, connection, ct);
        // Mark failed/interrupted discovery explicitly; never leave an older catalogue looking current.
        row.Revision = await data.SaveDiscoveryAsync(account, connection, row.Revision, [], false, clock.GetUtcNow(), ct);
        var result = await client.DiscoverAsync(ct);
        if (result.Tools.Count > 100 || result.Tools.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != result.Tools.Count)
            throw new McpClientException("RESULT_TOO_LARGE");
        var tools = result.Tools.Select(x =>
        {
            if (string.IsNullOrEmpty(x.Name) || x.Name.Length > 128 || x.Name.Any(char.IsControl) || x.Description?.Length > 4000 ||
                x.InputSchema.Length > 16000 || x.OutputSchema?.Length > 16000) throw new McpClientException("RESULT_TOO_LARGE");
            using var input = JsonDocument.Parse(x.InputSchema);
            if (input.RootElement.ValueKind != JsonValueKind.Object) throw new McpClientException("INVALID_METADATA");
            if (x.OutputSchema is not null) { using var output = JsonDocument.Parse(x.OutputSchema); if (output.RootElement.ValueKind != JsonValueKind.Object) throw new McpClientException("INVALID_METADATA"); }
            var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { row.ProfileVersion, x.Name, x.Description, x.InputSchema, x.OutputSchema }))));
            return new McpDiscoveredTool { ConnectionId = connection, Name = x.Name, Version = version, Description = x.Description,
                InputSchema = x.InputSchema, OutputSchema = x.OutputSchema, DiscoveredUtc = clock.GetUtcNow(),
                Approved = previous.Any(old => old.Name == x.Name && old.Version == version && old.Approved),
                Audience = previous.SingleOrDefault(old => old.Name == x.Name)?.Audience ?? McpToolAudience.Inherit,
                AccessGrants = previous.SingleOrDefault(old => old.Name == x.Name)?.AccessGrants ?? "[]",
                UseClass = previous.SingleOrDefault(old => old.Name == x.Name && old.Version == version)?.UseClass ?? McpToolUse.Unclassified };
        }).ToArray();
        if (Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(tools)) > 256 * 1024) throw new McpClientException("RESULT_TOO_LARGE");
        if (await Require(Permissions.McpConnections.Manage, ct) != account) throw new UnauthorizedAccessException();
        await data.SaveDiscoveryAsync(account, connection, row.Revision, tools, result.Complete, clock.GetUtcNow(), ct);
    }
    public async Task ApproveAsync(Guid connection, string name, string version, bool approved, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Manage, ct);
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        if (approved && (!row.DiscoveryComplete || row.DiscoveredUtc is not { } discovered || discovered < clock.GetUtcNow().AddHours(-24))) throw new McpClientException("DISCOVERY_REQUIRED");
        var tool = (await data.ToolsAsync(account, connection, ct)).SingleOrDefault(t => t.Name == name && t.Version == version) ?? throw new McpClientException("CONFIGURATION_CHANGED");
        if (approved && tool.UseClass is not (McpToolUse.AutomaticRead or McpToolUse.ConfirmationRequired)) throw new McpClientException("TOOL_CLASSIFICATION_REQUIRED");
        await data.ApproveAsync(account, connection, row.Revision, name, version, approved, ct);
    }
    // Backend use case for later consumers; deliberately no HTTP arbitrary-tool console.
    public Task<string> CallAsync(Guid connection, string name, string arguments, CancellationToken ct = default) => CallVersionAsync(connection, name, null, arguments, ct);
    // Consumers of AvailableToolsAsync bind the call to the metadata contract they actually received.
    public Task<string> CallAsync(Guid connection, string name, string version, string arguments, CancellationToken ct = default) => CallVersionAsync(connection, name, version, arguments, ct);
    private async Task<string> CallVersionAsync(Guid connection, string name, string? version, string arguments, CancellationToken ct)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        if (!row.DiscoveryComplete || row.DiscoveredUtc is not { } last || last < clock.GetUtcNow().AddHours(-24)) throw new McpClientException("DISCOVERY_REQUIRED");
        await RequireConnectionAccess(account, row, ct);
        var subject = await authorization.GetAccountActorScopeAsync(account, ct);
        var tool = (await data.ToolsAsync(account, connection, ct)).SingleOrDefault(x => x.Name == name && (version is null || x.Version == version) && x.Approved && McpAccessPolicy.Tool(x.Audience, x.AccessGrants, subject)) ?? throw new McpClientException("TOOL_NOT_APPROVED");
        if (tool.UseClass == McpToolUse.ConfirmationRequired) throw new McpClientException("CONFIRMATION_REQUIRED");
        if (tool.UseClass != McpToolUse.AutomaticRead) throw new McpClientException("TOOL_NOT_APPROVED");
        if (Encoding.UTF8.GetByteCount(arguments) > 16 * 1024) throw new McpClientException("INVALID_ARGUMENT");
        try
        {
            using var json = JsonDocument.Parse(arguments);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new McpClientException("INVALID_ARGUMENT");
        }
        catch (JsonException) { throw new McpClientException("INVALID_ARGUMENT"); }
        return await (await ClientForAsync(connection, tool, ct)).ExecuteApprovedAsync(name, arguments, ct);
    }
    private sealed class Guard(Func<CancellationToken, Task> verify) : IMcpConnectionAccess { public Task VerifyAsync(CancellationToken ct) => verify(ct); }
}
