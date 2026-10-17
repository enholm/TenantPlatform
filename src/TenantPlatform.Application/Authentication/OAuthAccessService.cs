using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Authentication;

public sealed record OAuthConnection(string Id, Guid UserId, string ClientId, string ClientName,
    DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc, bool RefreshAllowed, bool Valid);
public sealed record OAuthUserStatus(bool IsActive, bool LoginEnabled);
public interface IOAuthUserData
{
    Task<OAuthUserStatus?> ReadAsync(Guid userId, CancellationToken ct);
}
public interface IOAuthConnectionData
{
    Task<OAuthConnection> CreateAsync(Guid userId, string clientId, DateTimeOffset now, DateTimeOffset end, bool refresh, CancellationToken ct);
    Task<IReadOnlyList<OAuthConnection>> ListAsync(Guid userId, CancellationToken ct);
    Task<OAuthConnection?> ReadAsync(string id, CancellationToken ct);
    Task RevokeAsync(Guid userId, string id, CancellationToken ct);
}

// Host supplies either its validated cookie identity or an already validated code/refresh subject.
// No caller-supplied user identifier is accepted by these use cases.
public sealed class OAuthAccessService(ICurrentActor actor, IOAuthUserData users, IOAuthConnectionData connections,
    ILocalAuthenticationService credentials, TimeProvider clock)
{
    public async Task<User?> LoginAsync(string email, string password, CancellationToken ct)
    {
        var result = await credentials.AuthenticateAsync(email, password, ct);
        return result.Succeeded && result.User is { IsActive: true } user &&
            await users.ReadAsync(user.Id, ct) is { IsActive: true, LoginEnabled: true }
            ? user : null; // Authentication only: never selects or remembers an account.
    }
    public async Task<Guid> RequireUserAsync(CancellationToken ct)
    {
        var snapshot = actor.GetSnapshot();
        if (!snapshot.IsAuthenticated || await users.ReadAsync(snapshot.UserId, ct) is not { IsActive: true, LoginEnabled: true })
            throw new UnauthorizedAccessException();
        return snapshot.UserId;
    }
    public async Task<OAuthConnection> ApproveAsync(string clientId, bool refresh, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        var now = clock.GetUtcNow();
        return await connections.CreateAsync(user, clientId, now, now.AddHours(8), refresh, ct);
    }
    public async Task<IReadOnlyList<OAuthConnection>> ListAsync(CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        return await connections.ListAsync(user, ct);
    }
    public async Task<OAuthConnection> ValidateAsync(string id, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new UnauthorizedAccessException();
        var connection = await connections.ReadAsync(id, ct);
        if (connection is null || connection.UserId != user || !connection.Valid || connection.ExpiresUtc <= clock.GetUtcNow())
            throw new UnauthorizedAccessException();
        return connection;
    }
    public async Task RevokeAsync(string id, CancellationToken ct)
    {
        var user = await RequireUserAsync(ct);
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200) throw new UnauthorizedAccessException();
        var connection = await connections.ReadAsync(id, ct);
        if (connection is null || connection.UserId != user) throw new UnauthorizedAccessException();
        await connections.RevokeAsync(user, id, ct);
    }
}
