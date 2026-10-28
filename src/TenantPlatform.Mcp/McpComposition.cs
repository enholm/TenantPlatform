using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Mcp;

public static class McpComposition
{
    public static IServiceCollection AddTenantMcp(this IServiceCollection services, McpOptions options)
    {
        options.Validate();
        services.AddLogging(logging =>
        {
            foreach (var category in new[] { "Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "OpenIddict", "System.Net.Http.HttpClient", "ModelContextProtocol", "Npgsql" })
                logging.AddFilter(category, LogLevel.None);
        });
        services.AddSingleton(options);
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ReadOnlyGuard>();
        services.AddDbContextFactory<TenantPlatformDbContext>((sp, db) => db.UseNpgsql(options.ConnectionString, npgsql => npgsql.CommandTimeout(8))
            .AddInterceptors(sp.GetRequiredService<ReadOnlyGuard>()));
        services.AddScoped<OperationActor>();
        services.AddScoped<ICurrentActor>(sp => sp.GetRequiredService<OperationActor>());
        services.AddScoped<IAuthorizationData, AuthorizationData>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IAccountContextData, AccountContextData>();
        services.AddScoped<AccountContextReader>();
        services.AddScoped<IAgreementListData, AgreementListData>();
        services.AddScoped<IAgreementListService, AgreementListService>();
        services.AddScoped<IAgreementReportingData, AgreementReportingData>();
        services.AddScoped<AgreementReportingReader>();
        services.AddScoped<TenantPlatform.Application.ServiceRequests.IServiceRequestReportingData, TenantPlatform.Infrastructure.ServiceRequests.ServiceRequestReportingData>();
        services.AddScoped<TenantPlatform.Application.ServiceRequests.ServiceRequestReportingReader>();
        services.AddScoped<TenantPlatform.Application.Buildings.IBuildingReportingData, TenantPlatform.Infrastructure.Buildings.BuildingReportingData>();
        services.AddScoped<TenantPlatform.Application.Buildings.BuildingReportingReader>();
        services.AddScoped<IAgreementDetailsData, AgreementDetailsData>();
        services.AddScoped<IAgreementDetailsReader, AgreementDetailsReader>();
        services.AddScoped<IAgreementReadExtensionData, AgreementReadExtensionData>();
        services.AddScoped<AgreementReadExtensions>();
        services.AddScoped<IAgreementFinanceReadData, AgreementFinanceReadData>();
        services.AddScoped<AgreementFinanceReader>();
        services.AddScoped<IAgreementIndexReadData, AgreementIndexReadData>();
        services.AddScoped<AgreementIndexReader>();
        services.AddScoped<IAgreementBasisReadData, AgreementBasisReadData>();
        services.AddScoped<AgreementBasisReader>();
        services.AddScoped<IAgreementFollowupPageData, AgreementFollowupPageData>();
        services.AddScoped<AgreementFollowupPageReader>();
        services.AddScoped<IAgreementDeadlineData, AgreementDeadlineData>();
        services.AddScoped<IAgreementDeadlineReader, AgreementDeadlineReader>();
        services.AddScoped<IAgreementDashboardData, AgreementDashboardData>();
        services.AddScoped<IAgreementDashboardService, AgreementDashboardService>();
        services.AddScoped<IAgreementInsightsData, AgreementInsightsData>();
        services.AddScoped<IAgreementInsightsService, AgreementInsightsService>();
        services.AddScoped<IAgreementAttentionData, AgreementAttentionData>();
        services.AddScoped<IAgreementAttentionService, AgreementAttentionService>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, McpAuthorizationResultHandler>();
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        services.AddOpenIddict().AddValidation(validation =>
        {
            validation.SetIssuer(new Uri(options.Issuer));
            validation.AddAudiences(options.Resource);
            validation.UseSystemNetHttp(http => http.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5)));
            validation.UseAspNetCore().DisableAccessTokenExtractionFromBodyForm().DisableAccessTokenExtractionFromQueryString();
            validation.Configure(configuration =>
            {
                var p = configuration.TokenValidationParameters;
                p.ValidIssuers = [options.Issuer]; // Override OpenIddict's optional trailing-slash equivalence.
                p.ValidAudience = options.Resource;
                p.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
                p.ClockSkew = TimeSpan.FromSeconds(30);
                p.RequireSignedTokens = true;
                p.RequireExpirationTime = true;
                p.ValidateLifetime = true;
                p.ValidateIssuerSigningKey = true;
            });
        });
        services.AddAuthorizationBuilder().AddPolicy("McpRead", policy => policy.RequireAuthenticatedUser().RequireAssertion(context =>
        {
            var user = context.User;
            var subjects = user.FindAll("sub").ToArray();
            var clients = user.FindAll("client_id").ToArray();
            var nbf = user.FindFirst("nbf")?.Value;
            return subjects.Length == 1 && Guid.TryParseExact(subjects[0].Value, "D", out var id) && id != Guid.Empty &&
                !string.IsNullOrEmpty(user.GetAuthorizationId()) && clients.Length == 1 && options.Clients.Contains(clients[0].Value, StringComparer.Ordinal) && user.HasScope(McpOptions.ReadScope) &&
                (nbf is null || long.TryParse(nbf, out var seconds) && seconds <= DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds());
        }));
        services.Configure<ForwardedHeadersOptions>(forward =>
        {
            forward.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forward.KnownIPNetworks.Clear(); forward.KnownProxies.Clear();
            foreach (var proxy in options.TrustedProxies) forward.KnownProxies.Add(IPAddress.Parse(proxy));
            // Empty lists mean trust all in middleware: do NOT install it unless explicit proxies exist.
            forward.ForwardLimit = 1;
        });
        services.AddRateLimiter(limits =>
        {
            limits.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limits.AddPolicy("McpRead", http => RateLimitPartition.GetFixedWindowLimiter(
                Guid.Parse(http.User.FindFirst("sub")!.Value).ToString("D"), _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        services.AddSingleton<ReadTools>();
        services.AddMcpServer().WithHttpTransport(http => http.SessionMode = HttpServerSessionMode.Stateless)
            .WithListToolsHandler((context, _) => ValueTask.FromResult(new ListToolsResult { Tools = ReadTools.Catalog.Where(t =>
                t.Name is not ("buildings_query" or "buildings_aggregate") || context.User?.HasScope(TenantPlatform.Application.Buildings.StructureReadScopes.Read) == true).ToList() }))
            .WithCallToolHandler((context, ct) => context.Services!.GetRequiredService<ReadTools>().CallAsync(context, ct));
        return services;
    }

    public static WebApplication MapTenantMcp(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<McpOptions>();
        if (options.TrustedProxies.Length > 0) app.UseForwardedHeaders();
        app.Use(async (http, next) =>
        {
            if (http.Request.Path != "/mcp") { await next(http); return; }
            var original = http.RequestAborted;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(original);
            budget.CancelAfter(TimeSpan.FromSeconds(10));
            http.RequestAborted = budget.Token;
            try { await next(http); }
            catch (OperationCanceledException) when (budget.IsCancellationRequested)
            { if (!http.Response.HasStarted) http.Response.StatusCode = original.IsCancellationRequested ? 499 : 504; }
            catch (Exception)
            { if (!http.Response.HasStarted) { http.Response.StatusCode = budget.IsCancellationRequested ? (original.IsCancellationRequested ? 499 : 504) : 503; http.Response.ContentLength = 0; } }
            finally { http.RequestAborted = original; }
        });
        app.UseAuthentication();
        // Sanitize challenges; never emit OpenIddict's internal failure details.
        app.Use(async (http, next) =>
        {
            http.Response.OnStarting(() =>
            {
                if (http.Response.StatusCode == 401 || http.Items.ContainsKey(McpAuthorizationResultHandler.ErrorItem))
                {
                    var error = http.Items[McpAuthorizationResultHandler.ErrorItem] as string;
                    http.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{options.MetadataUrl}\", scope=\"{McpOptions.ReadScope}\"" +
                        (error is null ? "" : $", error=\"{error}\"");
                }
                http.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
            await next(http);
        });
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseMiddleware<McpBoundary>();
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
        app.MapGet("/.well-known/oauth-protected-resource/mcp", () => Results.Json(new
        {
            resource = options.Resource, authorization_servers = new[] { options.Issuer },
            scopes_supported = new[] { TenantPlatform.Application.Buildings.StructureReadScopes.Read, McpOptions.ReadScope, TenantPlatform.Application.ServiceRequests.ServiceRequestReportScopes.Read }, bearer_methods_supported = new[] { "header" }
        })).AllowAnonymous();
        app.MapMcp("/mcp").RequireAuthorization("McpRead").RequireRateLimiting("McpRead");
        return app;
    }
}
