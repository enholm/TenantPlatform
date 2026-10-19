using Microsoft.AspNetCore.DataProtection;
using Permissions = OpenIddict.Abstractions.OpenIddictConstants.Permissions;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Web.Security.OAuth;

public static class OAuthComposition
{
    public static IServiceCollection AddTenantOAuth(this IServiceCollection services, OAuthIssuerOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<Microsoft.Extensions.Options.IOptions<OAuthIssuerOptions>>(Microsoft.Extensions.Options.Options.Create(options));
        if (!options.Enabled) return services;
        var certificates = options.Validate();
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(options.CookieKeyDirectory))
            .ProtectKeysWithCertificate(certificates.Encryption)
            .UnprotectKeysWithAnyCertificate([certificates.Encryption, .. certificates.PreviousEncryption]);
        services.AddDbContext<OAuthDbContext>(db => db.UseNpgsql(options.ConnectionString,
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "oauth")).UseOpenIddict());
        services.AddOpenIddict().AddCore(core => { core.DisableEntityCaching(); core.UseEntityFrameworkCore().UseDbContext<OAuthDbContext>(); })
            .AddServer(server =>
            {
                server.SetIssuer(new Uri(options.Issuer));
                server.SetAuthorizationEndpointUris("/connect/authorize").SetTokenEndpointUris("/connect/token")
                    .SetRevocationEndpointUris("/connect/revoke");
                server.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                server.RegisterScopes(OAuthIssuerOptions.ReadScope, TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage).RegisterResources(options.Resource);
                server.Configure(configuration =>
                {
                    configuration.Scopes.Clear();
                    configuration.Scopes.Add(OAuthIssuerOptions.ReadScope);
                    configuration.Scopes.Add(Scopes.OfflineAccess);
                    configuration.Scopes.Add(TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage);
                    configuration.CodeChallengeMethods.Clear();
                    configuration.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                    configuration.ClientAuthenticationMethods.Clear();
                    configuration.ClientAuthenticationMethods.Add(ClientAuthenticationMethods.ClientSecretBasic);
                    configuration.ClientAuthenticationMethods.Add(ClientAuthenticationMethods.ClientSecretPost);
                });
                server.AddSigningCertificate(certificates.Signing).AddEncryptionCertificate(certificates.Encryption)
                    .DisableAccessTokenEncryption().SetAccessTokenLifetime(TimeSpan.FromMinutes(5))
                    .SetRefreshTokenLifetime(TimeSpan.FromHours(8)).SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                foreach (var previous in certificates.PreviousSigning) server.AddSigningCertificate(previous);
                foreach (var previous in certificates.PreviousEncryption) server.AddEncryptionCertificate(previous);
                OAuthTokenLifetime.Configure(server);
                server.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
            });
        services.AddScoped<IOAuthUserData, OAuthUserData>();
        services.AddScoped<IOAuthConnectionData, OAuthConnectionData>();
        services.AddScoped<OAuthAccessService>(sp => new(new OAuthActor(sp.GetRequiredService<IHttpContextAccessor>()),
            sp.GetRequiredService<IOAuthUserData>(), sp.GetRequiredService<IOAuthConnectionData>(),
            sp.GetRequiredService<ILocalAuthenticationService>(), sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<OAuthInteractions>();
        return services;
    }

    // Explicit operator invocation only. Never called during ordinary Web startup.
    public static async Task RegisterClientsAsync(IServiceProvider provider, OAuthIssuerOptions options, CancellationToken ct = default)
    {
        if (!options.Enabled) throw new InvalidOperationException("OAuth is disabled.");
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in options.Clients)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = client.ClientId, DisplayName = client.DisplayName, ClientSecret = client.Secret,
                ClientType = ClientTypes.Confidential, ConsentType = ConsentTypes.Explicit
            };
            foreach (var redirect in client.RedirectUris) descriptor.RedirectUris.Add(new Uri(redirect));
            descriptor.Permissions.UnionWith([Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + OAuthIssuerOptions.ReadScope]);
            if (client.AllowPersonalPages) descriptor.Permissions.Add(Permissions.Prefixes.Scope + TenantPlatform.Application.PersonalPages.PersonalPageScopes.Manage);
            descriptor.AddResourcePermissions(options.Resource);
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            var existing = await manager.FindByClientIdAsync(client.ClientId, ct);
            if (existing is null) await manager.CreateAsync(descriptor, ct);
            else await manager.UpdateAsync(existing, descriptor, ct);
        }
    }
}

sealed class OAuthActor(IHttpContextAccessor accessor) : ICurrentActor
{
    internal const string SubjectItem = "TenantPlatform.ValidatedOAuthSubject";
    public ActorSnapshot GetSnapshot()
    {
        var http = accessor.HttpContext;
        if (http?.Items[SubjectItem] is Guid subject) return new(true, subject, null);
        var cookie = http?.User;
        return cookie?.Identity?.IsAuthenticated == true && Guid.TryParse(cookie.FindFirst(TenantPlatform.Web.Security.TenantPlatformClaimTypes.UserId)?.Value, out var user)
            ? new(true, user, null) : new(false, Guid.Empty, null);
    }
}
