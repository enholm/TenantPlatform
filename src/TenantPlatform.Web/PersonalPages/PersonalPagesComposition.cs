using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Infrastructure.PersonalPages;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.McpClient;
namespace TenantPlatform.Web.PersonalPages;

public static class PersonalPagesComposition
{
    public static void AddPersonalPages(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<PersonalPageRuns>();
        services.AddScoped<IPersonalPageData>(s => new PersonalPageData(s.GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>()));
        services.AddScoped<PersonalPageService>(s => Create(s, new RegistryCookieActor(s.GetRequiredService<IHttpContextAccessor>())));
        services.AddScoped<PersonalPageRunner>(s => new(s.GetRequiredService<PersonalPageService>(), s.GetService<McpRegistryService>(), s.GetRequiredService<PersonalPageRuns>(), s.GetRequiredService<TimeProvider>()));
        services.AddPersonalPagesMcp(configuration);
    }
    public static PersonalPageService Create(IServiceProvider sp, ICurrentActor actor, IPersonalPageData? data = null)
    {
        var auth = new AuthorizationService(actor, sp.GetRequiredService<IAuthorizationData>(), sp.GetRequiredService<TimeProvider>());
        // Same registry/client implementation and personal credential store, bound to this operation's verified actor.
        McpRegistryService? registry = sp.GetService<IMcpRegistryData>() is { } directory ? new(actor, auth, directory,
            sp.GetRequiredService<IMcpAuthProfiles>(), sp.GetRequiredService<IMcpRegisteredClientFactory>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IMcpAccessDirectory>()) : null;
        return new(actor, auth, data ?? sp.GetRequiredService<IPersonalPageData>(), registry);
    }
}
