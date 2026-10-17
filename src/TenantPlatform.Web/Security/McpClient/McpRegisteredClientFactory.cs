using Microsoft.EntityFrameworkCore;
using OpenIddict.Client;
using OpenIddict.Validation;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Persistence.OAuth;

namespace TenantPlatform.Web.Security.McpClient;

public sealed class McpRegisteredClientFactory(IServiceProvider services, McpAuthProfiles profiles) : IMcpRegisteredClientFactory
{
    public Task ForgetAsync(Guid user, Guid connection, CancellationToken ct) =>
        new McpClientStore(services.GetRequiredService<DbContextOptions<OAuthDbContext>>(), connection)
            .WithUserLockAsync(user, async (slot, token) => { await slot.SaveAsync(null, token); return true; }, ct);
    public InternalMcpClientService Create(McpAuthProfile profile, Guid connection, IMcpConnectionAccess access)
    {
        var runtime = profiles.Loaded.Single(x => x.Public.Id == profile.Id && x.Public.Version == profile.Version);
        var binding = profile.Binding with { ConnectionId = connection };
        return new(new RegistryCookieActor(services.GetRequiredService<IHttpContextAccessor>()), services.GetRequiredService<IOAuthUserData>(),
            services.GetRequiredService<IOAuthConnectionData>(), new McpClientStore(services.GetRequiredService<DbContextOptions<OAuthDbContext>>(), connection),
            services.GetRequiredService<IMcpClientTokenProtection>(),
            new DelegatedMcpGateway(services.GetRequiredService<IHttpContextAccessor>(), services.GetRequiredService<OpenIddictClientService>(),
                services.GetRequiredService<OpenIddictValidationService>(), services.GetRequiredService<IHttpClientFactory>(),
                services.GetRequiredService<InternalMcpClientOptions>(), runtime), binding, services.GetRequiredService<TimeProvider>(), access);
    }
}
// Read the freshly authenticated cookie per HTTP operation, never a cached Blazor circuit or request argument.
internal sealed class RegistryCookieActor(IHttpContextAccessor http) : ICurrentActor
{
    public ActorSnapshot GetSnapshot()
    {
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirst(TenantPlatformClaimTypes.UserId)?.Value, out var user)) return new(false, Guid.Empty, null);
        return new(true, user, Guid.TryParse(principal.FindFirst(TenantPlatformClaimTypes.CurrentAccountId)?.Value, out var account) ? account : null);
    }
}
