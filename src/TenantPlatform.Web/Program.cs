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
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using Microsoft.Extensions.Options;
using System.Globalization;
using Microsoft.AspNetCore.Localization;
using TenantPlatform.Web.Services.Buildings;
using TenantPlatform.Web.Services.Organizations;
using TenantPlatform.Web.Services.Units;
using TenantPlatform.Web.Services.Occupancies;
using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Web.Security.Auditing;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContextFactory<TenantPlatformDbContext>(
    (serviceProvider, options) =>
    {
        options.UseNpgsql(connectionString);

        options.AddInterceptors(
            serviceProvider.GetRequiredService<
                AuditSaveChangesInterceptor>());
    },
    ServiceLifetime.Scoped);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddLocalization(options =>
    options.ResourcesPath = "Resources");


builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = new[]
    {
        new CultureInfo("nb-NO"),
        new CultureInfo("en-GB")
    };

    options.DefaultRequestCulture =
        new RequestCulture("nb-NO");

    options.SupportedCultures = cultures;
    options.SupportedUICultures = cultures;
});

builder.Services.AddSingleton<PasswordService>();
builder.Services.AddScoped<ILocalAuthenticationService, LocalAuthenticationService>();
builder.Services.AddScoped<AuthenticationCookieService>();
builder.Services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();
builder.Services.AddScoped<IBuildingService, BuildingService>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<IUnitService, UnitService>();
builder.Services.AddScoped<IOccupancyService, OccupancyService>();
builder.Services.AddScoped<IAuditUserContext, AuditUserContext>();

builder.Services.AddScoped<AuditSaveChangesInterceptor>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<
    ICurrentUserContextService,
    CurrentUserContextService>();

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

var localizationOptions = app.Services
    .GetRequiredService<
        IOptions<RequestLocalizationOptions>>()
    .Value;

app.UseRequestLocalization(localizationOptions);


// Initialize database and add platform data (and also demo data if dev environment)
using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider
        .GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>();
    var logger = scope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();
    var passwordService = scope.ServiceProvider
        .GetRequiredService<PasswordService>();
    await using var dbContext =
        await dbContextFactory.CreateDbContextAsync();
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


/***************
**  ENDPOINTS **
****************/

//---------------
// Endpoint login
// --------------
app.MapPost("/auth/login", async (
    HttpContext httpContext,
    ILocalAuthenticationService authenticationService,
    AuthenticationCookieService cookieService,
    TenantPlatformDbContext dbContext,
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

    var user = result.User;

    var accountIds = await dbContext.UserAccounts
        .Where(x => x.UserId == user.Id)
        .Select(x => x.AccountId)
        .ToListAsync(cancellationToken);

    if (accountIds.Count == 0)
    {
        return Results.Redirect(
            "/login?error=no-account-access");
    }

    var loginAccount = await dbContext.LoginAccounts
        .SingleAsync(
            x => x.UserId == user.Id,
            cancellationToken);

    Guid? selectedAccountId = null;

    if (accountIds.Count == 1)
    {
        // Brukeren har bare én Account.
        selectedAccountId = accountIds[0];

        loginAccount.LastAccountId = selectedAccountId;

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }
    else if (
        loginAccount.LastAccountId.HasValue &&
        accountIds.Contains(
            loginAccount.LastAccountId.Value))
    {
        // Brukeren har flere Accounts,
        // men vi kjenner siste gyldige valg.
        selectedAccountId =
            loginAccount.LastAccountId.Value;
    }

    await cookieService.SignInAsync(
        httpContext,
        user,
        selectedAccountId,
        rememberMe);

    cookieService.SetCulture(
        httpContext,
        user.PreferredLanguage);

    if (selectedAccountId.HasValue)
    {
        return Results.Redirect("/");
    }

    return Results.Redirect("/select-account");
});

//----------------
// Endpoint logout
// ---------------
app.MapPost("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(
        CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/login");
});

//------------------------
// Endpoint select-account
// -----------------------
app.MapPost("/auth/select-account", async (
    HttpContext httpContext,
    AuthenticationCookieService cookieService,
    TenantPlatformDbContext dbContext,
    IFormCollection form,
    CancellationToken cancellationToken) =>
{
    var accountIdValue =
        form["accountId"].ToString();

    if (!Guid.TryParse(
            accountIdValue,
            out var accountId))
    {
        return Results.BadRequest(
            "Invalid account ID.");
    }

    var userIdValue =
        httpContext.User.FindFirstValue(
            TenantPlatformClaimTypes.UserId);

    if (!Guid.TryParse(
            userIdValue,
            out var userId))
    {
        return Results.Unauthorized();
    }

    var hasAccess = await dbContext.UserAccounts
        .AnyAsync(
            x => x.UserId == userId &&
                 x.AccountId == accountId,
            cancellationToken);

    if (!hasAccess)
    {
        return Results.Forbid();
    }

    var loginAccount = await dbContext.LoginAccounts
        .SingleAsync(
            x => x.UserId == userId,
            cancellationToken);

    loginAccount.LastAccountId = accountId;

    await dbContext.SaveChangesAsync(
        cancellationToken);

    var user = await dbContext.Users
        .SingleAsync(
            x => x.Id == userId,
            cancellationToken);

    // Behold "Husk meg"-egenskapen fra eksisterende cookie.
    var authenticationResult =
        await httpContext.AuthenticateAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);

    var rememberMe =
        authenticationResult.Properties?.IsPersistent
        ?? false;

    await cookieService.SignInAsync(
        httpContext,
        user,
        accountId,
        rememberMe);

    return Results.Redirect("/");
});

//------------------------------
// Endpoint preferences/language
// -----------------------------
app.MapPost("/preferences/language", async (
    HttpContext httpContext,
    AuthenticationCookieService cookieService,
    IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
    IFormCollection form,
    CancellationToken cancellationToken) =>
{
    var culture = form["culture"].ToString();

    if (culture is not ("nb-NO" or "en-GB"))
    {
        return Results.BadRequest("Unsupported language.");
    }

    var userIdValue = httpContext.User.FindFirstValue(
        TenantPlatformClaimTypes.UserId);

    if (!Guid.TryParse(userIdValue, out var userId))
    {
        return Results.Unauthorized();
    }

    await using var dbContext =
        await dbContextFactory.CreateDbContextAsync(cancellationToken);

    var user = await dbContext.Users
        .SingleOrDefaultAsync(
            x => x.Id == userId,
            cancellationToken);

    if (user is null)
    {
        return Results.Unauthorized();
    }

    user.PreferredLanguage = culture;

    await dbContext.SaveChangesAsync(cancellationToken);

    cookieService.SetCulture(
        httpContext,
        culture);

    return Results.Redirect("/");
});
// ----------------------------------------------------------------------

app.Run();


public record LoginRequest(
    string Email,
    string Password,
    bool RememberMe);

