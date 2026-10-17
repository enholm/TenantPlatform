using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Mcp;

public sealed class InternalMcpClientService(ICurrentActor actor, IOAuthUserData users, IOAuthConnectionData grants,
    IMcpClientStore store, IMcpClientTokenProtection protection, IDelegatedMcpGateway gateway, McpClientBinding binding, TimeProvider clock, IMcpConnectionAccess? access = null)
{
    public async Task<McpClientStatus> StatusAsync(CancellationToken ct = default)
    {
        var user = await RequireUser(ct);
        return await store.WithUserLockAsync(user, async (slot, token) =>
        {
            var row = await slot.ReadAsync(token);
            return new McpClientStatus(Matches(row) && row!.ProtectedTokens is not null && row.GrantExpiresUtc > clock.GetUtcNow(),
                Matches(row) && row!.PendingExpiresUtc > clock.GetUtcNow(), Matches(row) ? row!.GrantExpiresUtc : null);
        }, ct);
    }
    public async Task<McpConnectRequest> BeginAsync(CancellationToken ct = default)
    {
        var user = await RequireUser(ct);
        return await store.WithUserLockAsync(user, async (slot, token) =>
        {
            var row = await slot.ReadAsync(token);
            if (Matches(row) && row!.ProtectedTokens is not null && row.GrantExpiresUtc > clock.GetUtcNow()) throw new McpClientException("ALREADY_CONNECTED");
            row = New(user);
            row.PendingId = Guid.NewGuid(); row.PendingExpiresUtc = clock.GetUtcNow().AddMinutes(10);
            await slot.SaveAsync(row, token);
            return new McpConnectRequest(user, row.PendingId.Value);
        }, ct);
    }
    public async Task CompleteAsync(CancellationToken ct = default)
    {
        var user = await RequireUser(ct);
        var callback = await gateway.ReadCallbackAsync(ct); // SDK-validated OAuth response; own-server JWT receives additional signature/subject validation.
        if (callback.Initiator != user || (!binding.External && callback.Token.Subject != user)) throw new McpClientException("CONNECTION_REJECTED");
        await store.WithUserLockAsync(user, async (slot, token) =>
        {
            var row = await slot.ReadAsync(token);
            if (!Matches(row) || row!.PendingId != callback.PendingId || (row.PendingExpiresUtc is not { } pendingEnd || pendingEnd <= clock.GetUtcNow())) throw new McpClientException("CONNECTION_REJECTED");
            var grantId = callback.Token.GrantId;
            var grantEnd = clock.GetUtcNow().AddHours(8); // Local maximum for an external OAuth delegation.
            if (callback.Token.AccessExpiresUtc <= clock.GetUtcNow()) throw new McpClientException("CONNECTION_REJECTED");
            if (!binding.External)
            {
                var grant = await grants.ReadAsync(grantId, token);
                if (grant is null || !grant.Valid || !grant.RefreshAllowed || grant.UserId != user || grant.ClientId != binding.ClientId ||
                    grant.ExpiresUtc <= clock.GetUtcNow() || callback.Token.AccessExpiresUtc > grant.ExpiresUtc) throw new McpClientException("CONNECTION_REJECTED");
                grantId = grant.Id; grantEnd = grant.ExpiresUtc;
            }
            await VerifyActive(user, token);
            row.ProtectedTokens = protection.Protect(user, binding, callback.Token.Tokens);
            row.GrantId = grantId; row.GrantExpiresUtc = grantEnd; row.AccessExpiresUtc = callback.Token.AccessExpiresUtc;
            row.PendingId = null; row.PendingExpiresUtc = null;
            await slot.SaveAsync(row, token);
            return true;
        }, ct);
    }
    public async Task<bool> DisconnectAsync(CancellationToken ct = default)
    {
        var user = await RequireUser(ct);
        return await store.WithUserLockAsync(user, async (slot, token) =>
        {
            var row = await slot.ReadAsync(token);
            // Clear durable local credentials BEFORE network activity; cancellation/outage cannot reactivate them.
            await slot.SaveAsync(null, token);
            if (!Matches(row) || row!.ProtectedTokens is null) return true;
            try { return await gateway.RevokeAsync(protection.Unprotect(user, binding, row.ProtectedTokens), token); }
            catch (Exception) { return false; }
        }, ct);
    }
    public Task<McpClientAccounts> AccountsAsync(CancellationToken ct = default)
    {
        RequireLegacyRead();
        return Call((access, token) => gateway.AccountsAsync(access, token), ct);
    }
    public Task<McpClientAttention> AttentionAsync(Guid accountId, CancellationToken ct = default)
    {
        RequireLegacyRead();
        if (accountId == Guid.Empty) throw new McpClientException("INVALID_ARGUMENT");
        return Call((access, token) => gateway.AttentionAsync(access, accountId, token), ct);
    }
    public Task<McpClientAgreements> AgreementsAsync(McpClientQuery query, CancellationToken ct = default)
    {
        if (query.AccountId == Guid.Empty || query.Page is < 1 or > 10000 || query.Search is { } search &&
            (search.EnumerateRunes().Count() > 200 || search.Any(char.IsControl)) ||
            query.Status.HasValue && !Enum.IsDefined(query.Status.Value) || query.Type.HasValue && !Enum.IsDefined(query.Type.Value) ||
            query.Attention.HasValue && !Enum.IsDefined(query.Attention.Value)) throw new McpClientException("INVALID_ARGUMENT");
        return Call((access, token) => gateway.AgreementsAsync(access, query, token), ct);
    }
    internal Task<McpToolDiscovery> DiscoverAsync(CancellationToken ct = default) => Call((value, token) => gateway.DiscoverAsync(value, token), ct);
    internal Task<string> ExecuteApprovedAsync(string name, string arguments, CancellationToken ct) => Call((value, token) => gateway.CallToolAsync(value, name, arguments, token), ct);
    private void RequireLegacyRead()
    {
        if (binding.ConnectionId != Guid.Empty) throw new McpClientException("TOOL_NOT_APPROVED"); // Registered calls always pass through the catalogue gate.
    }
    private async Task<T> Call<T>(Func<string, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        var user = await RequireUser(ct);
        return await store.WithUserLockAsync(user, async (slot, token) =>
        {
            var row = await slot.ReadAsync(token);
            if (!Matches(row) || row!.ProtectedTokens is null || (row.GrantExpiresUtc is not { } end || end <= clock.GetUtcNow()) || row.AccessExpiresUtc is null || row.GrantId is null) throw new McpClientException("RECONNECT_REQUIRED");
            McpClientTokens credentials;
            try { credentials = protection.Unprotect(user, binding, row.ProtectedTokens); }
            catch (Exception) { await slot.SaveAsync(null, token); throw new McpClientException("RECONNECT_REQUIRED"); }
            if (row.AccessExpiresUtc <= clock.GetUtcNow().AddSeconds(30))
            {
                // Commit the consumed state first. An ambiguous HTTP error must NEVER retry this refresh token.
                row.ProtectedTokens = null;
                await slot.SaveAsync(row, token);
                McpTokenReceipt refreshed;
                try { refreshed = await gateway.RefreshAsync(credentials, token); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { throw new McpClientException("RECONNECT_REQUIRED"); }
                if ((!binding.External && refreshed.Subject != user) || refreshed.GrantId != row.GrantId || (!binding.External && refreshed.AccessExpiresUtc > row.GrantExpiresUtc) ||
                    refreshed.AccessExpiresUtc <= clock.GetUtcNow()) throw new McpClientException("RECONNECT_REQUIRED");
                await VerifyActive(user, token);
                credentials = refreshed.Tokens;
                row.ProtectedTokens = protection.Protect(user, binding, credentials); row.AccessExpiresUtc = refreshed.AccessExpiresUtc;
                await slot.SaveAsync(row, token);
            }
            await VerifyActive(user, token);
            // No data fallback: only the delegated MCP transport can supply these results.
            return await read(credentials.AccessToken, token);
        }, ct);
    }
    private async Task<Guid> RequireUser(CancellationToken ct)
    {
        var snapshot = actor.GetSnapshot();
        if (!snapshot.IsAuthenticated || snapshot.UserId == Guid.Empty) throw new UnauthorizedAccessException();
        await VerifyActive(snapshot.UserId, ct);
        return snapshot.UserId;
    }
    private async Task VerifyActive(Guid user, CancellationToken ct)
    {
        // The optional legacy guard must never turn into an unguarded registered/external client.
        if ((binding.ConnectionId != Guid.Empty || binding.External) && access is null) throw new UnauthorizedAccessException();
        if (access is not null) await access.VerifyAsync(ct);
        if (await users.ReadAsync(user, ct) is not { IsActive: true, LoginEnabled: true }) throw new UnauthorizedAccessException();
    }
    private bool Matches(McpClientCredential? row) => row is not null && row.ConnectionId == binding.ConnectionId && row.ProfileVersion == binding.ProfileVersion && row.ClientId == binding.ClientId && row.Issuer == binding.Issuer && row.Resource == binding.Resource;
    private McpClientCredential New(Guid user) => new() { UserId = user, ConnectionId = binding.ConnectionId, ProfileVersion = binding.ProfileVersion, ClientId = binding.ClientId, Issuer = binding.Issuer, Resource = binding.Resource };
}
