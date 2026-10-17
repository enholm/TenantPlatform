using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web;
using TenantPlatform.Web.Security;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Security.OAuth;

// Shared TEST-ONLY issuer harness. Never compiled into either production host.
internal static class OAuthTestHost
{
public static async Task<WebApplication> Start(OAuthIssuerOptions settings, Factory factory, AdjustableClock clock, LogCapture logs)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", ApplicationName = typeof(OAuthEndpoints).Assembly.FullName });
    builder.WebHost.UseTestServer();
    builder.Logging.ClearProviders(); builder.Logging.AddProvider(logs); builder.Logging.AddFilter("OpenIddict", LogLevel.Warning);
    builder.Services.AddRazorComponents(); builder.Services.AddLocalization(x => x.ResourcesPath = "Resources");
    builder.Services.AddSingleton<TimeProvider>(clock); builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<IDbContextFactory<TenantPlatformDbContext>>(factory);
    builder.Services.AddSingleton<PasswordService>(); builder.Services.AddScoped<ILocalAuthenticationService, LocalAuthenticationService>();
    builder.Services.AddScoped<IAuthenticationSessionData, AuthenticationSessionData>(); builder.Services.AddScoped<AuthenticationSessionService>();
    builder.Services.AddScoped<ICurrentUserContextService, HttpCurrent>();
    builder.Services.AddScoped<ICurrentActor, TenantPlatform.Web.Security.Application.CurrentActorAdapter>();
    builder.Services.AddScoped<IAuthorizationData, AuthorizationData>();
    builder.Services.AddScoped<TenantPlatform.Application.Security.IAuthorizationService, TenantPlatform.Application.Security.AuthorizationService>();
    builder.Services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();
    builder.Services.AddScoped<AuthenticationCookieService>();
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(x => { x.Cookie.Name = "TenantPlatform.Auth"; x.Cookie.SecurePolicy = CookieSecurePolicy.Always; });
    builder.Services.AddAuthorization(); builder.Services.AddTenantOAuth(settings);
    if (settings.Enabled) builder.Services.AddOpenIddict().AddServer(server =>
        server.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(handler => handler
            .SetOrder(OpenIddictServerHandlers.PrepareAccessTokenPrincipal.Descriptor.Order - 500)
            .UseInlineHandler(context =>
            {
                if (clock.DelayNextPreparation)
                {
                    clock.DelayNextPreparation = false;
                    clock.Advance(TimeSpan.FromSeconds(10));
                }
                return ValueTask.CompletedTask;
            })));
    var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.UseAntiforgery();
    app.MapTenantOAuth(settings); app.MapTenantLogin(settings);
    app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
    {
        antiforgery.GetAndStoreTokens(context);
        return new RazorComponentResult<TenantPlatform.Web.Components.Pages.Login>(new { OAuthInteraction = context.Request.Query["oauth"].ToString() });
    });
    await app.StartAsync(); return app;
}
}
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{ public TenantPlatformDbContext CreateDbContext() => new(options); public Task<TenantPlatformDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext()); }
sealed class HttpCurrent(IHttpContextAccessor accessor) : ICurrentUserContextService
{
    public CurrentUserContext Current => new() { IsAuthenticated = accessor.HttpContext?.User.Identity?.IsAuthenticated == true,
        UserId = Guid.TryParse(accessor.HttpContext?.User.FindFirst(TenantPlatformClaimTypes.UserId)?.Value, out var id) ? id : Guid.Empty };
}
sealed class Browser(HttpClient client, Dictionary<string,string>? cookies = null)
{
    public Dictionary<string,string> Cookies { get; } = cookies ?? [];
    public Task<HttpResponseMessage> Get(string path) => Send(new(HttpMethod.Get, path));
    public Task<HttpResponseMessage> Post(string path, Dictionary<string,string> form) => Send(new(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(form) });
    private async Task<HttpResponseMessage> Send(HttpRequestMessage request)
    {
        request.RequestUri = new Uri(new Uri("https://tenant.test"), request.RequestUri!);
        if (Cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(x => x.Key + "=" + x.Value)));
        var response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out var set)) foreach (var line in set)
        {
            var pair = line.Split(';')[0].Split('=', 2); Cookies[pair[0]] = pair[1];
        }
        return response;
    }
}
sealed class LogCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Capture(Messages);
    public void Dispose() { }
    sealed class Capture(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception) + exception?.ToString());
    }
}

sealed class AdjustableClock : TimeProvider
{ private TimeSpan offset; public bool DelayNextPreparation; public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset; public void Advance(TimeSpan span) => offset += span; }
