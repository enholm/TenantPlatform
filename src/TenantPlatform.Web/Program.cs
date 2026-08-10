using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using TenantPlatform.Web.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Initialization;
using TenantPlatform.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using TenantPlatform.Web.Security;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<TenantPlatformDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<ILocalAuthenticationService, LocalAuthenticationService>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<
    ICurrentUserService,
    CurrentUserService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "TenantPlatform.Auth";
        options.Cookie.HttpOnly = true;
        if (builder.Environment.IsDevelopment())
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        }
        else
        {
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }
        options.Cookie.SameSite = SameSiteMode.Lax;

        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";

        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// Initialize database and add platform data (and also demo data if dev environment)
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<TenantPlatformDbContext>();

    var logger = scope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();
    var passwordService = scope.ServiceProvider
        .GetRequiredService<PasswordService>();
    await DatabaseInitializer.InitializeAsync(
        dbContext,
        logger,
        passwordService,
        includeDemoData: app.Environment.IsDevelopment());
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapStaticAssets();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();


app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/auth/login", async (
    HttpContext httpContext,
    ILocalAuthenticationService authenticationService,
    IFormCollection form,
    CancellationToken cancellationToken) =>
{
    var email = form["email"].ToString();
    var password = form["password"].ToString();

    var rememberMe =
        string.Equals(
            form["rememberMe"].ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);

    var result = await authenticationService.AuthenticateAsync(
        email,
        password,
        cancellationToken);

    if (!result.Succeeded || result.User is null)
    {
        return Results.Redirect(
            "/login?error=invalid-login");
    }

    var claims = new List<Claim>
    {
        new(
            TenantPlatformClaimTypes.UserId,
            result.User.Id.ToString()),

        new(
            ClaimTypes.Email,
            result.User.Email),

        new(
            ClaimTypes.Name,
            $"{result.User.FirstName} {result.User.LastName}")
    };

    var identity = new ClaimsIdentity(
        claims,
        CookieAuthenticationDefaults.AuthenticationScheme);

    var principal = new ClaimsPrincipal(identity);

    var authenticationProperties = new AuthenticationProperties
    {
        IsPersistent = rememberMe,
        AllowRefresh = true
    };

    await httpContext.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        authenticationProperties);

    return Results.Redirect("/");
});

app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(
        CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/login");
});

app.Run();


public record LoginRequest(
    string Email,
    string Password,
    bool RememberMe);

