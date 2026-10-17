using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Mcp;

public sealed class McpRegistryService(ICurrentActor actor, IAuthorizationService authorization, IMcpRegistryData data,
    IMcpAuthProfiles profiles, IMcpRegisteredClientFactory clients, TimeProvider clock)
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
        return new(await authorization.CanAsync(Permissions.McpConnections.Manage, new(ResourceKind.Account, AccountId: account), ct),
            await data.ListAsync(account, ct), profiles.GetAll());
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
        var profile = profiles.GetAll().SingleOrDefault(x => x.Id == row?.ProfileId && x.Version == row.ProfileVersion && x.Ready);
        if (row is not { Enabled: true } || profile is null) throw new McpClientException("PROFILE_UNAVAILABLE");
        var guard = new Guard(async token =>
        {
            if (await Require(Permissions.McpConnections.Use, token) != account) throw new UnauthorizedAccessException();
            var fresh = await data.ReadAsync(account, connection, token);
            if (fresh is not { Enabled: true } || fresh.ProfileId != row.ProfileId || fresh.ProfileVersion != row.ProfileVersion || fresh.CredentialEpoch != row.CredentialEpoch ||
                !profiles.GetAll().Any(x => x.Id == profile.Id && x.Version == profile.Version && x.Ready)) throw new McpClientException("PROFILE_UNAVAILABLE");
            if (expectedTool is not null && (!fresh.DiscoveryComplete || (fresh.DiscoveredUtc is not { } discoveryDate || discoveryDate < clock.GetUtcNow().AddHours(-24)) ||
                !(await data.ToolsAsync(account, connection, token)).Any(x => x.Name == expectedTool.Name && x.Version == expectedTool.Version && x.Approved)))
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
        catch (McpClientException)
        {
            await clients.ForgetAsync(actor.GetSnapshot().UserId, connection, ct);
            return false; // Erased locally. Never forward an old token through a changed/disabled profile.
        }
        return await client.DisconnectAsync(ct);
    }
    public async Task<IReadOnlyList<McpDiscoveredTool>> ToolsAsync(Guid connection, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        if (await data.ReadAsync(account, connection, ct) is null) throw new UnauthorizedAccessException();
        return await data.ToolsAsync(account, connection, ct);
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
                Approved = previous.Any(old => old.Name == x.Name && old.Version == version && old.Approved) };
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
        await data.ApproveAsync(account, connection, row.Revision, name, version, approved, ct);
    }
    // Backend use case for later consumers; deliberately no HTTP arbitrary-tool console.
    public async Task<string> CallAsync(Guid connection, string name, string arguments, CancellationToken ct = default)
    {
        var account = await Require(Permissions.McpConnections.Use, ct);
        var row = await data.ReadAsync(account, connection, ct) ?? throw new UnauthorizedAccessException();
        if (!row.DiscoveryComplete || row.DiscoveredUtc is not { } last || last < clock.GetUtcNow().AddHours(-24)) throw new McpClientException("DISCOVERY_REQUIRED");
        var tool = (await data.ToolsAsync(account, connection, ct)).SingleOrDefault(x => x.Name == name && x.Approved) ?? throw new McpClientException("TOOL_NOT_APPROVED");
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
