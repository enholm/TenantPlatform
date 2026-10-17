using Microsoft.Extensions.Http;
using OpenIddict.Client;
using OpenIddict.Client.SystemNetHttp;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Authentication;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Web.Security.McpClient;

public static class McpRegistryComposition
{
    public static void AddMcpRegistry(this IServiceCollection services, InternalMcpClientOptions own, McpRegistryOptions options)
    {
        var profiles = new McpAuthProfiles(own, options);
        services.AddSingleton(profiles); services.AddSingleton<IMcpAuthProfiles>(profiles);
        if (profiles.Loaded.Count == 0) return;
        services.AddScoped<IMcpRegistryData, McpRegistryData>();
        services.AddScoped<IMcpAccessDirectory, McpAccessDirectoryData>();
        services.AddScoped<IMcpRegisteredClientFactory, McpRegisteredClientFactory>();
        services.AddScoped<McpRegistryService>(sp =>
        {
            var actor = new RegistryCookieActor(sp.GetRequiredService<IHttpContextAccessor>());
            return new(actor, new AuthorizationService(actor, sp.GetRequiredService<IAuthorizationData>(), sp.GetRequiredService<TimeProvider>()),
                sp.GetRequiredService<IMcpRegistryData>(), profiles, sp.GetRequiredService<IMcpRegisteredClientFactory>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IMcpAccessDirectory>());
        });
        services.AddOpenIddict().AddClient(client =>
        {
            foreach (var profile in profiles.Loaded.Where(x => x.Public.Binding.External))
            {
                var registration = new OpenIddictClientRegistration { RegistrationId = profile.RegistrationId,
                    ClientId = profile.Public.Binding.ClientId, ClientSecret = profile.Secret,
                    Issuer = new Uri(profile.Public.Binding.Issuer), ConfigurationEndpoint = new Uri(profile.MetadataAddress!), RedirectUri = new Uri(profile.Callback) };
                registration.Scopes.UnionWith(profile.Scopes);
                registration.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
                registration.ClientAuthenticationMethods.Add(ClientAuthenticationMethods.ClientSecretPost);
                registration.ResponseModes.Add(ResponseModes.Query);
                client.AddRegistration(registration);
            }
            client.SetRedirectionEndpointUris(profiles.Loaded.Select(x => x.Callback).Distinct().ToArray());
        });
        foreach (var profile in profiles.Loaded)
        {
            // The HTTP transport pin and byte bound are shared with 5B3; each profile has a distinct HttpClient.
            services.AddHttpClient("TenantPlatform.Mcp." + profile.Public.Id, c => c.Timeout = TimeSpan.FromSeconds(15))
                .ConfigurePrimaryHttpMessageHandler(() => McpNetworkPolicy.Handler(profile.AllowedPrivateAddresses))
                .AddHttpMessageHandler(() => new PinnedMcpEndpointHandler(new() { Resource = profile.Public.Endpoint }));
        }
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new RegistryOAuthNetworkFilter(profiles));
    }
}
sealed class RegistryOAuthNetworkFilter(McpAuthProfiles profiles) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name?.StartsWith("OpenIddict.Client.SystemNetHttp", StringComparison.Ordinal) != true) return;
        var registration = OpenIddictClientSystemNetHttpContext.Current?.Registration;
        var profile = profiles.Loaded.SingleOrDefault(x => x.RegistrationId == registration?.RegistrationId);
        if (profile is null) return;
        builder.PrimaryHandler.Dispose();
        builder.PrimaryHandler = McpNetworkPolicy.Handler(profile.AllowedPrivateAddresses);
    };
}
