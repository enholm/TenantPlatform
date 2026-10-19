using System.Collections.Immutable;
using System.Text.Json;
using OpenIddict.Abstractions;
using TenantPlatform.Application.Authentication;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Infrastructure.Authentication;

public sealed class OAuthConnectionData(IOpenIddictAuthorizationManager grants, IOpenIddictApplicationManager clients,
    IOpenIddictTokenManager tokens) : IOAuthConnectionData
{
    public async Task<OAuthConnection> CreateAsync(Guid userId, string clientId, DateTimeOffset now, DateTimeOffset end, bool refresh, CancellationToken ct, bool pages = false)
    {
        var client = await clients.FindByClientIdAsync(clientId, ct) ?? throw new InvalidOperationException("Unknown OAuth client.");
        var descriptor = new OpenIddictAuthorizationDescriptor
        {
            ApplicationId = await clients.GetIdAsync(client, ct), Subject = userId.ToString("D"),
            Status = Statuses.Valid, Type = AuthorizationTypes.Permanent, CreationDate = now
        };
        descriptor.Properties["end"] = JsonSerializer.SerializeToElement(end);
        descriptor.Properties["refresh"] = JsonSerializer.SerializeToElement(refresh);
        descriptor.Scopes.Add("tenantplatform.agreements.read");
        if (pages) descriptor.Scopes.Add(TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage);
        if (refresh) descriptor.Scopes.Add(Scopes.OfflineAccess);
        var grant = await grants.CreateAsync(descriptor, ct);
        return await Map(grant, ct);
    }
    public async Task<IReadOnlyList<OAuthConnection>> ListAsync(Guid userId, CancellationToken ct)
    {
        var rows = new List<OAuthConnection>();
        await foreach (var grant in grants.FindAsync(subject: userId.ToString("D"), client: null, status: Statuses.Valid, type: AuthorizationTypes.Permanent, scopes: null, cancellationToken: ct))
            rows.Add(await Map(grant, ct));
        return rows.OrderByDescending(x => x.CreatedUtc).ToArray();
    }
    public async Task<OAuthConnection?> ReadAsync(string id, CancellationToken ct)
    {
        var grant = await grants.FindByIdAsync(id, ct);
        return grant is null ? null : await Map(grant, ct);
    }
    public async Task RevokeAsync(Guid userId, string id, CancellationToken ct)
    {
        var grant = await grants.FindByIdAsync(id, ct);
        if (grant is null || await grants.GetSubjectAsync(grant, ct) != userId.ToString("D")) throw new UnauthorizedAccessException();
        if (!await grants.TryRevokeAsync(grant, ct)) throw new InvalidOperationException("OAuth grant could not be revoked.");
        await tokens.RevokeByAuthorizationIdAsync(id, ct);
    }
    private async Task<OAuthConnection> Map(object grant, CancellationToken ct)
    {
        var props = await grants.GetPropertiesAsync(grant, ct);
        var client = await clients.FindByIdAsync((await grants.GetApplicationIdAsync(grant, ct))!, ct);
        return new((await grants.GetIdAsync(grant, ct))!, Guid.Parse((await grants.GetSubjectAsync(grant, ct))!),
            client is null ? "" : (await clients.GetClientIdAsync(client, ct))!,
            client is null ? "" : (await clients.GetDisplayNameAsync(client, ct)) ?? "",
            (await grants.GetCreationDateAsync(grant, ct)) ?? DateTimeOffset.MinValue,
            props.TryGetValue("end", out var end) ? end.GetDateTimeOffset() : DateTimeOffset.MinValue,
            props.TryGetValue("refresh", out var refresh) && refresh.GetBoolean(),
            await grants.HasStatusAsync(grant, Statuses.Valid, ct));
    }
}
