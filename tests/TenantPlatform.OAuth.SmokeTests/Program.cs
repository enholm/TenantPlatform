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

var connection = Environment.GetEnvironmentVariable("OAUTH_TEST_CONNECTION");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Use the isolated PostgreSQL runner with OAUTH_TEST_CONNECTION.");
var cs = new NpgsqlConnectionStringBuilder(connection);
if (cs.Database != "tenant_oauth_tests" || cs.Host?.StartsWith("/tmp/tenant-agreement-tests.", StringComparison.Ordinal) != true)
    throw new InvalidOperationException("Only the disposable OAuth test database/socket is permitted.");
var directory = Path.Combine(Path.GetTempPath(), "tenant-oauth-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var certificatePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
var logs = new LogCapture();
var clock = new AdjustableClock();
var options = new OAuthIssuerOptions
{
    Enabled = true, Issuer = "https://tenant.test/", Resource = "https://mcp.test/mcp", ConnectionString = connection,
    SigningCertificatePath = Path.Combine(directory, "signing.pfx"), SigningCertificatePassword = certificatePassword,
    EncryptionCertificatePath = Path.Combine(directory, "encryption.pfx"), EncryptionCertificatePassword = certificatePassword,
    CookieKeyDirectory = Path.Combine(directory, "keys"),
    Clients = [new() { ClientId = "protocol-fixture", DisplayName = "Protocol fixture", Secret = secret, RedirectUris = ["https://client.test/callback"] }]
};
foreach (var path in new[] { options.SigningCertificatePath, options.EncryptionCertificatePath })
{
    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest("CN=OAuth test only", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
    using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
    await File.WriteAllBytesAsync(path, cert.Export(X509ContentType.Pfx, certificatePassword));
}
var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(connection).Options);
var user = Guid.NewGuid(); var other = Guid.NewGuid(); var a = Guid.NewGuid(); var b = Guid.NewGuid();
await using (var db = await factory.CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    db.Accounts.AddRange(new Account { Id = a, Name = "OAuth A" }, new Account { Id = b, Name = "OAuth B" });
    foreach (var id in new[] { user, other })
    {
        var person = new User { Id = id, FirstName = "Test", LastName = "User", Email = id + "@example.test", IsActive = true };
        var login = new LoginAccount { Id = Guid.NewGuid(), UserId = id, Email = person.Email, CreatedUtc = DateTimeOffset.UtcNow };
        login.PasswordHash = new PasswordService().HashPassword(login, password);
        db.Users.Add(person); db.LoginAccounts.Add(login);
        db.UserAccounts.AddRange(new UserAccount { Id = Guid.NewGuid(), UserId = id, AccountId = a }, new UserAccount { Id = Guid.NewGuid(), UserId = id, AccountId = b });
    }
    await db.SaveChangesAsync();
}
var secrets = new List<string> { password, secret, certificatePassword };
WebApplication? host = null;
try
{
    host = await Start(options);
    await using (var scope = host.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<OAuthDbContext>().Database.MigrateAsync();
    await OAuthComposition.RegisterClientsAsync(host.Services, options);
    await OAuthComposition.RegisterClientsAsync(host.Services, options);
    await using (var scope = host.Services.CreateAsyncScope())
        Check(await scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>().CountAsync() == 1, "idempotent operator client registration");
    try { new ServiceCollection().AddTenantOAuth(new OAuthIssuerOptions { Enabled = true }); throw new Exception("Invalid enabled issuer configuration accepted"); }
    catch (InvalidOperationException) { Check(true, "enabled issuer fails closed without real configuration/keys"); }
    var browser = new Browser(host.GetTestClient());
    var discovery = JsonDocument.Parse(await (await browser.Get("/.well-known/openid-configuration")).Content.ReadAsStringAsync()).RootElement;
    Check(discovery.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(x => x.GetString()).SequenceEqual(["S256"]), "discovery advertises S256 only");
    Check(discovery.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(x => x.GetString()).ToHashSet().SetEquals(["client_secret_basic", "client_secret_post"]), "metadata advertises only implemented confidential-client methods");
    Check(!discovery.GetProperty("scopes_supported").EnumerateArray().Any(x => x.GetString() is "openid" or "email" or "profile") && !discovery.TryGetProperty("userinfo_endpoint", out _), "metadata does not advertise OIDC/UserInfo");
    Check((await browser.Get("/connect/authorize?client_id=unknown&response_type=code&redirect_uri=https%3A%2F%2Fclient.test%2Fcallback")).StatusCode == HttpStatusCode.BadRequest, "unknown client rejected before login");
    var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
    Dictionary<string, string> Authorization(string? scope = null, string? resource = null) => new()
    {
        ["client_id"] = "protocol-fixture", ["redirect_uri"] = "https://client.test/callback", ["response_type"] = "code",
        ["scope"] = scope ?? OAuthIssuerOptions.ReadScope + " offline_access", ["resource"] = resource ?? options.Resource,
        ["state"] = "test-state", ["code_challenge_method"] = "S256", ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
    };
    var authorizePath = "/connect/authorize" + Query(Authorization());
    var redirectToLogin = await browser.Get(authorizePath);
    Check(redirectToLogin.StatusCode == HttpStatusCode.Redirect && redirectToLogin.Headers.Location!.OriginalString.StartsWith("/login?oauth="), "anonymous authorization redirects to protected login interaction");
    var loginUrl = redirectToLogin.Headers.Location!.OriginalString;
    var loginFields = Fields(await (await browser.Get(loginUrl)).Content.ReadAsStringAsync());
    loginFields["email"] = user + "@example.test"; loginFields["password"] = password;
    var loginResult = await browser.Post("/auth/login", loginFields);
    Check(loginResult.StatusCode == HttpStatusCode.Redirect && loginResult.Headers.Location!.OriginalString.StartsWith("/connect/authorize?"), "real password login returns to same authorization");
    await using (var db = await factory.CreateDbContextAsync())
        Check(await db.LoginAccounts.Where(x => x.UserId == user).Select(x => x.LastAccountId).SingleAsync() == null, "OAuth login does not choose/store a GUI account");
    Check((await browser.Post("/auth/login", new() { ["oauth"] = "https://evil.test", ["email"] = user + "@example.test", ["password"] = password })).StatusCode == HttpStatusCode.BadRequest, "missing CSRF/open redirect input rejected");
    async Task<(string Code, Dictionary<string,string> Fields)> Approve(Browser current, string path, string decision = "approve")
    {
        var page = await current.Get(path); Check(page.StatusCode == HttpStatusCode.OK, "authenticated consent page");
        var fields = Fields(await page.Content.ReadAsStringAsync()); fields["decision"] = decision;
        var response = await current.Post("/connect/authorize", fields);
        Check(response.StatusCode == HttpStatusCode.Redirect, "consent returns protocol response");
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        var resultCode = query.TryGetValue("code", out var code) ? code.ToString() : "";
        if (resultCode.Length > 0) secrets.Add(resultCode);
        return (resultCode, fields);
    }
    var denied = await Approve(browser, authorizePath, "deny"); Check(denied.Code == "", "denied consent produces no code");
    var consent = Fields(await (await browser.Get(authorizePath)).Content.ReadAsStringAsync());
    consent["decision"] = "approve";
    var noCsrf = new Dictionary<string,string>(consent); noCsrf.Remove("__RequestVerificationToken");
    Check((await browser.Post("/connect/authorize", noCsrf)).StatusCode == HttpStatusCode.BadRequest, "consent requires CSRF");
    var changed = new Dictionary<string,string>(consent) { ["state"] = "changed" };
    Check((await browser.Post("/connect/authorize", changed)).StatusCode == HttpStatusCode.BadRequest, "sealed interaction rejects manipulated protocol fields");
    changed = new(consent) { ["interaction"] = consent["interaction"] + "x" };
    Check((await browser.Post("/connect/authorize", changed)).StatusCode == HttpStatusCode.BadRequest, "tampered interaction rejected");
    foreach (var extra in new[] { "&resource=https%3A%2F%2Fother.test", "&code_challenge_method=plain" })
    {
        var invalid = await browser.Get(authorizePath + extra);
        Check(!invalid.IsSuccessStatusCode, "multiple resources/PKCE method cannot bypass server policy");
    }
    var approved = await Approve(browser, authorizePath); secrets.Add(approved.Code);
    Dictionary<string,string> CodeRequest(string code) => new() { ["grant_type"] = "authorization_code", ["client_id"] = "protocol-fixture", ["client_secret"] = secret,
        ["code"] = code, ["redirect_uri"] = "https://client.test/callback", ["code_verifier"] = verifier, ["resource"] = options.Resource };
    async Task<JsonElement> Token(Dictionary<string,string> form, bool success)
    {
        var response = await browser.Post("/connect/token", form);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        Check(response.IsSuccessStatusCode == success, "token response " + (success ? "success" : "denial"));
        if (json.TryGetProperty("access_token", out var accessToken)) secrets.Add(accessToken.GetString()!);
        if (json.TryGetProperty("refresh_token", out var refreshToken)) secrets.Add(refreshToken.GetString()!);
        return json;
    }
    var readOnly = await Token(CodeRequest((await Approve(browser, "/connect/authorize" + Query(Authorization(OAuthIssuerOptions.ReadScope)))).Code), true);
    Check(!readOnly.TryGetProperty("refresh_token", out _), "no refresh token without explicit offline-access consent");
    var issued = await Token(CodeRequest(approved.Code), true);
    var token = issued.GetProperty("access_token").GetString()!;
    var refresh = issued.GetProperty("refresh_token").GetString()!;
    using (var anonymousClient = host.GetTestClient())
    {
        var bearer = new HttpRequestMessage(HttpMethod.Get, "https://tenant.test/oauth/connections");
        bearer.Headers.Authorization = new("Bearer", token);
        var noGuiSession = await anonymousClient.SendAsync(bearer);
        Check(noGuiSession.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized, "bearer token does not authenticate the GUI cookie scheme");
        var basicFields = CodeRequest((await Approve(browser, authorizePath)).Code);
        basicFields.Remove("client_id"); basicFields.Remove("client_secret");
        var basicSecret = Convert.ToBase64String(Encoding.UTF8.GetBytes("protocol-fixture:" + secret)); secrets.Add(basicSecret);
        var basic = new HttpRequestMessage(HttpMethod.Post, "https://tenant.test/connect/token") { Content = new FormUrlEncodedContent(basicFields) };
        basic.Headers.Authorization = new("Basic", basicSecret);
        var basicResponse = await anonymousClient.SendAsync(basic);
        Check(basicResponse.IsSuccessStatusCode, "actual client_secret_basic exchange as well as client_secret_post");
        var basicJson = JsonDocument.Parse(await basicResponse.Content.ReadAsStringAsync()).RootElement;
        secrets.Add(basicJson.GetProperty("access_token").GetString()!); secrets.Add(basicJson.GetProperty("refresh_token").GetString()!);
    }
    using var certificate = X509CertificateLoader.LoadPkcs12FromFile(options.SigningCertificatePath, certificatePassword);
    var validator = new JsonWebTokenHandler();
    var parameters = new TokenValidationParameters { ValidIssuer = options.Issuer, ValidAudience = options.Resource,
        IssuerSigningKey = new X509SecurityKey(certificate), ValidTypes = ["at+jwt"], ClockSkew = TimeSpan.FromSeconds(30) };
    var validated = await validator.ValidateTokenAsync(token, parameters);
    Check(validated.IsValid, "established token validator checks signature/type/issuer/audience/lifetime");
    var jwt = validator.ReadJsonWebToken(token);
    Check(jwt.Subject == user.ToString("D") && jwt.ValidTo - jwt.IssuedAt == TimeSpan.FromMinutes(5), "stable subject and actual five-minute expiry");
    Check(!jwt.Claims.Any(x => x.Type is "email" or "role" or "roles" or "name" or "account_id" || x.Type == OAuthTokenLifetime.EndClaim), "minimal token has no roles/email/member/business data or private lifetime metadata");
    parameters.ValidAudience = "https://wrong.test"; Check(!(await validator.ValidateTokenAsync(token, parameters)).IsValid, "wrong audience rejected by library"); parameters.ValidAudience = options.Resource;
    var bad = CodeRequest((await Approve(browser, authorizePath)).Code); bad["code_verifier"] = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48)); await Token(bad, false);
    bad = CodeRequest((await Approve(browser, authorizePath)).Code); bad["redirect_uri"] = "https://client.test/other"; await Token(bad, false);
    bad = CodeRequest((await Approve(browser, authorizePath)).Code); bad["resource"] = "https://wrong.test"; await Token(bad, false);
    bad = CodeRequest((await Approve(browser, authorizePath)).Code); bad["client_secret"] = "invalid"; await Token(bad, false);
    await Token(CodeRequest(approved.Code), false); // Reuse must not issue another token.
    foreach (var path in new[] { "/connect/authorize" + Query(Authorization("unknown")), "/connect/authorize" + Query(Authorization(resource:"https://wrong.test")) })
    {
        var response = await browser.Get(path);
        Check(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect, "invalid scope/resource produces protocol error");
        Check(!response.IsSuccessStatusCode, "invalid authorization never renders consent");
    }
    // Independent grant: the code-reuse test above may revoke the original chain by design.
    issued = await Token(CodeRequest((await Approve(browser, authorizePath)).Code), true);
    refresh = issued.GetProperty("refresh_token").GetString()!;
    Dictionary<string,string> Refresh(string value) => new() { ["grant_type"] = "refresh_token", ["client_id"] = "protocol-fixture", ["client_secret"] = secret, ["refresh_token"] = value, ["resource"] = options.Resource };
    var wrongResource = Refresh(refresh); wrongResource["resource"] = "https://wrong.test"; await Token(wrongResource, false);
    var rotated = await Token(Refresh(refresh), true);
    Check(rotated.GetProperty("refresh_token").GetString() != refresh, "refresh token rotates");
    await Token(Refresh(refresh), false);
    var renewal = Refresh(rotated.GetProperty("refresh_token").GetString()!); renewal["scope"] = "other"; await Token(renewal, false);
    issued = await Token(CodeRequest((await Approve(browser, authorizePath)).Code), true);
    var restartRefresh = issued.GetProperty("refresh_token").GetString()!;
    var cookies = browser.Cookies;
    await host.DisposeAsync(); host = await Start(options); browser = new Browser(host.GetTestClient(), cookies);
    var afterRestart = await Token(Refresh(restartRefresh), true);
    Check((await browser.Get(authorizePath)).IsSuccessStatusCode, "restart preserves signing/encryption/cookie keys and actual refresh/cookie validity");
    var ownPage = await browser.Get("/oauth/connections");
    var ownFields = Fields(await ownPage.Content.ReadAsStringAsync());
    Check(ownFields.ContainsKey("connection"), "own connections are listed");
    var stranger = new Browser(host.GetTestClient());
    await Login(stranger, other);
    var strangerHtml = await (await stranger.Get("/oauth/connections")).Content.ReadAsStringAsync();
    Check(!strangerHtml.Contains(ownFields["connection"]), "other users cannot list this connection");
    var strangerPage = Fields(await (await stranger.Get("/login")).Content.ReadAsStringAsync());
    strangerPage["connection"] = ownFields["connection"];
    Check((await stranger.Post("/oauth/connections/revoke", strangerPage)).StatusCode == HttpStatusCode.Unauthorized, "another user cannot revoke grant by ID");
    var emptyId = new Dictionary<string,string>(ownFields) { ["connection"] = "" };
    Check((await browser.Post("/oauth/connections/revoke", emptyId)).StatusCode == HttpStatusCode.Unauthorized, "invalid connection ID fails safely before storage lookup");
    Check((await browser.Post("/oauth/connections/revoke", ownFields)).StatusCode == HttpStatusCode.Redirect, "owner can revoke connection");
    await Token(Refresh(afterRestart.GetProperty("refresh_token").GetString()!), false);
    Check((await validator.ValidateTokenAsync(afterRestart.GetProperty("access_token").GetString()!, parameters)).IsValid, "offline JWT validation retains documented rest lifetime after revocation");
    async Task SetStatus(bool enabled, bool active)
    {
        await using var db = await factory.CreateDbContextAsync();
        (await db.LoginAccounts.SingleAsync(x => x.UserId == user)).IsEnabled = enabled;
        (await db.Users.SingleAsync(x => x.Id == user)).IsActive = active;
        await db.SaveChangesAsync();
    }
    foreach (var state in new[] { (Enabled:false, Active:true), (Enabled:true, Active:false) })
    {
        var code = (await Approve(browser, authorizePath)).Code;
        var pendingConsent = Fields(await (await browser.Get(authorizePath)).Content.ReadAsStringAsync()); pendingConsent["decision"] = "approve";
        await SetStatus(state.Enabled, state.Active); await Token(CodeRequest(code), false);
        var staleConsent = await browser.Post("/connect/authorize", pendingConsent);
        Check(staleConsent.StatusCode == HttpStatusCode.Redirect && !QueryHelpers.ParseQuery(staleConsent.Headers.Location!.Query).ContainsKey("code"), "status is rechecked when pending consent is approved");
        var response = await browser.Get(authorizePath); Check(!response.IsSuccessStatusCode, "stale cookie cannot approve inactive/disabled subject");
        await SetStatus(true, true);
        var live = await Token(CodeRequest((await Approve(browser, authorizePath)).Code), true);
        await SetStatus(state.Enabled, state.Active); await Token(Refresh(live.GetProperty("refresh_token").GetString()!), false);
        await SetStatus(true, true);
    }
    var gui = new Browser(host.GetTestClient()); await Login(gui, user);
    await using (var db = await factory.CreateDbContextAsync())
        Check(await db.LoginAccounts.Where(x => x.UserId == user).Select(x => x.LastAccountId).SingleAsync() == null, "multi-account login still awaits GUI account selection");
    var bounded = await Token(CodeRequest((await Approve(browser, authorizePath)).Code), true);
    DateTimeOffset grantEnd;
    await using (var scope = host.Services.CreateAsyncScope())
    {
        var grants = await scope.ServiceProvider.GetRequiredService<IOAuthConnectionData>().ListAsync(user, default);
        var latest = grants.OrderByDescending(x => x.CreatedUtc).First();
        Check(latest.ExpiresUtc - latest.CreatedUtc == TimeSpan.FromHours(8), "grant has an absolute eight-hour duration");
        grantEnd = latest.ExpiresUtc;
    }
    clock.Advance(TimeSpan.FromHours(7) + TimeSpan.FromMinutes(58));
    clock.DelayNextPreparation = true;
    var nearEnd = await Token(Refresh(bounded.GetProperty("refresh_token").GetString()!), true);
    var boundedJwt = validator.ReadJsonWebToken(nearEnd.GetProperty("access_token").GetString()!);
    Check(new DateTimeOffset(boundedJwt.ValidTo) <= grantEnd && boundedJwt.ValidTo - boundedJwt.IssuedAt <= TimeSpan.FromMinutes(2), "refresh/access expiry is capped by original grant end");
    clock.Advance(TimeSpan.FromMinutes(3));
    await Token(Refresh(nearEnd.GetProperty("refresh_token").GetString()!), false);
    Check(!logs.Messages.Any(line => secrets.Where(x => x.Length > 10).Any(line.Contains)), "captured logs contain no passwords/client secrets/codes/access/refresh tokens");
    await host.DisposeAsync(); host = await Start(new OAuthIssuerOptions());
    var disabled = new Browser(host.GetTestClient());
    Check((await disabled.Get("/connect/authorize")).StatusCode == HttpStatusCode.NotFound && (await disabled.Get("/.well-known/openid-configuration")).StatusCode == HttpStatusCode.NotFound && (await disabled.Get("/oauth/connections")).StatusCode == HttpStatusCode.NotFound, "disabled issuer exposes no OAuth routes");
    Console.WriteLine("OAuth PostgreSQL protocol checks passed.");

    async Task Login(Browser current, Guid id)
    {
        var fields = Fields(await (await current.Get("/login")).Content.ReadAsStringAsync());
        fields["email"] = id + "@example.test"; fields["password"] = password;
        var response = await current.Post("/auth/login", fields);
        Check(response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location!.OriginalString == "/select-account", "ordinary GUI login retains multi-account navigation");
    }
}
finally
{
    if (host is not null) await host.DisposeAsync();
    Directory.Delete(directory, true);
}

async Task<WebApplication> Start(OAuthIssuerOptions settings)
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
static Dictionary<string,string> Fields(string html) => Regex.Matches(html, "<input[^>]*name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\"[^>]*>")
    .GroupBy(x => WebUtility.HtmlDecode(x.Groups[1].Value)).ToDictionary(x => x.Key, x => WebUtility.HtmlDecode(x.First().Groups[2].Value));
static QueryString Query(Dictionary<string,string> values) => QueryString.Create(values.Select(x => new KeyValuePair<string,string?>(x.Key,x.Value)));
static void Check(bool value, string message) { if (!value) throw new Exception("FAIL OAuth: " + message); Console.WriteLine("PASS OAuth: " + message); }
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
