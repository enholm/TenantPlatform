using Microsoft.Extensions.Http;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Client;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Web.Security.OAuth;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace TenantPlatform.Web.Security.McpClient;

public static class InternalMcpClientComposition
{
    public static IServiceCollection AddInternalMcpClient(this IServiceCollection services, InternalMcpClientOptions options, OAuthIssuerOptions issuer)
    {
        options.Inspect(issuer); services.AddSingleton(options);
        if (!options.Available) return services; // Ordinary GUI login remains available with missing/disabled client configuration.
        var certificates = issuer.Validate();
        services.AddSingleton(new McpClientBinding(options.ClientId, options.Issuer, options.Resource));
        services.AddScoped<IMcpClientStore, McpClientStore>();
        services.AddScoped<IMcpClientTokenProtection, McpClientTokenProtection>();
        services.AddScoped<IDelegatedMcpGateway, DelegatedMcpGateway>();
        services.AddScoped<InternalMcpClientService>(sp => new(new OAuthActor(sp.GetRequiredService<IHttpContextAccessor>()),
            sp.GetRequiredService<IOAuthUserData>(), sp.GetRequiredService<IOAuthConnectionData>(), sp.GetRequiredService<IMcpClientStore>(),
            sp.GetRequiredService<IMcpClientTokenProtection>(), sp.GetRequiredService<IDelegatedMcpGateway>(),
            sp.GetRequiredService<McpClientBinding>(), sp.GetRequiredService<TimeProvider>()));
        services.AddOpenIddict().AddClient(client =>
        {
            client.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().SetStateTokenLifetime(TimeSpan.FromMinutes(10));
            client.SetRedirectionEndpointUris(options.Callback);
            client.AddSigningCertificate(certificates.Signing).AddEncryptionCertificate(certificates.Encryption);
            foreach (var previous in certificates.PreviousSigning) client.AddSigningCertificate(previous);
            foreach (var previous in certificates.PreviousEncryption) client.AddEncryptionCertificate(previous);
            client.UseAspNetCore().EnableRedirectionEndpointPassthrough().EnableErrorPassthrough();
            client.UseSystemNetHttp(http => http.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10))
                .ConfigureHttpClientHandler(h => { h.AllowAutoRedirect = false; h.UseCookies = false; }).SetHttpResiliencePipeline(_ => { })); // No retry of consumed refresh/code.
            var registration = new OpenIddictClientRegistration
            {
                RegistrationId = InternalMcpClientOptions.Registration, ClientId = options.ClientId, ClientSecret = options.ClientSecret,
                Issuer = new Uri(options.Issuer), RedirectUri = new Uri(options.Callback)
            };
            registration.Scopes.UnionWith([OAuthIssuerOptions.ReadScope, Scopes.OfflineAccess]);
            registration.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256);
            registration.ClientAuthenticationMethods.Add(ClientAuthenticationMethods.ClientSecretPost);
            registration.ResponseModes.Add(ResponseModes.Query);
            client.AddRegistration(registration);
            client.Configure(configuration => { configuration.CodeChallengeMethods.Clear(); configuration.CodeChallengeMethods.Add(CodeChallengeMethods.Sha256); });
            client.AddEventHandler<OpenIddictClientEvents.ProcessChallengeContext>(handler => handler
                .SetOrder(OpenIddictClientHandlers.AttachChallengeParameters.Descriptor.Order - 500)
                .UseInlineHandler(context => { context.Resources.Clear(); context.Resources.Add(options.Resource); return ValueTask.CompletedTask; }));
            client.AddEventHandler<OpenIddictClientEvents.PrepareTokenRequestContext>(handler => handler
                .UseInlineHandler(context => { context.Request.Resources = [options.Resource]; return ValueTask.CompletedTask; }));
        })
        .AddValidation(validation =>
        {
            validation.SetIssuer(new Uri(options.Issuer)).AddAudiences(options.Resource);
            validation.UseSystemNetHttp(http => http.ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(10)).ConfigureHttpClientHandler(h => { h.AllowAutoRedirect = false; h.UseCookies = false; }));
            validation.Configure(configuration =>
            {
                var p = configuration.TokenValidationParameters;
                p.ValidIssuers = [options.Issuer]; p.ValidAlgorithms = [SecurityAlgorithms.RsaSha256]; p.ClockSkew = TimeSpan.FromSeconds(30);
                p.RequireSignedTokens = true; p.RequireExpirationTime = true; p.ValidateLifetime = true; p.ValidateIssuerSigningKey = true;
            });
        });
        services.AddTransient<PinnedMcpEndpointHandler>();
        services.AddHttpClient(DelegatedMcpGateway.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .AddHttpMessageHandler<PinnedMcpEndpointHandler>();
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new PinnedOAuthOriginFilter(new Uri(options.Issuer)));
        services.AddLogging(logging =>
        {
            foreach (var category in new[] { "OpenIddict.Client", "OpenIddict.Validation", "ModelContextProtocol", "System.Net.Http.HttpClient.OpenIddict", "System.Net.Http.HttpClient." + DelegatedMcpGateway.HttpClientName })
                logging.AddFilter(category, LogLevel.None);
            // Callback query carries protocol code/state. Never log request-start URLs at Information.
            logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        });
        return services;
    }
}

sealed class PinnedOAuthOriginFilter(Uri issuer) : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
    {
        next(builder);
        if (builder.Name?.StartsWith("OpenIddict.Client.SystemNetHttp", StringComparison.Ordinal) == true ||
            builder.Name?.StartsWith("OpenIddict.Validation.SystemNetHttp", StringComparison.Ordinal) == true)
            builder.AdditionalHandlers.Insert(0, new PinnedOAuthOriginHandler(issuer));
    };
}
sealed class PinnedOAuthOriginHandler(Uri issuer) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { } uri || uri.Scheme != issuer.Scheme || uri.Authority != issuer.Authority || uri.UserInfo.Length != 0)
            throw new HttpRequestException("OAuth endpoint rejected.");
        return base.SendAsync(request, ct);
    }
}
public sealed class PinnedMcpEndpointHandler(InternalMcpClientOptions options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.AbsoluteUri != options.Resource) throw new HttpRequestException("MCP endpoint rejected.");
        var response = await base.SendAsync(request, ct);
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            using var data = new MemoryStream(); var buffer = new byte[4096]; int read;
            while ((read = await source.ReadAsync(buffer, ct)) != 0)
            {
                if (data.Length + read > 64 * 1024) throw new HttpRequestException("MCP response limit exceeded.");
                await data.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            var content = new ByteArrayContent(data.ToArray());
            foreach (var header in response.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Dispose(); response.Content = content; return response;
        }
        catch { response.Dispose(); throw; }
    }
}
