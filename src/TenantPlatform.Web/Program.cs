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
using TenantPlatform.Web.Services.ServiceDefinitions;
using TenantPlatform.Core.Localization;
using TenantPlatform.Web.Services.ServiceDefinitionFields;
using TenantPlatform.Web.Services.ServiceCatalog;
using TenantPlatform.Web.Services.ServiceRequests;
using TenantPlatform.Core.Identity;
using TenantPlatform.Web.Email;
using TenantPlatform.Web.Services.Accounts;
using TenantPlatform.Web.Services.UserAdministration;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Infrastructure.Agreements;


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

builder.Services.Configure<SmtpOptions>(
    builder.Configuration.GetSection("Email"));

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var cultures = SupportedLanguages.All
        .Select(x => new CultureInfo(x.Code))
        .ToArray();

    options.DefaultRequestCulture =
        new RequestCulture(SupportedLanguages.NbNo);

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
builder.Services.AddScoped<IServiceDefinitionService, ServiceDefinitionService>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<IServiceDefinitionFieldService, ServiceDefinitionFieldService>();
builder.Services.AddScoped<IServiceCatalogService, ServiceCatalogService>();
builder.Services.AddScoped<IServiceRequestService, ServiceRequestService>();
builder.Services.AddScoped<IServiceRequestEmailAddressService, ServiceRequestEmailAddressService>();
builder.Services.AddScoped<IServiceRequestEmailComposer, ServiceRequestEmailComposer>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IInboundServiceRequestEmailService, InboundServiceRequestEmailService>();
builder.Services.AddScoped<IServiceRequestReplyAddressParser, ServiceRequestReplyAddressParser>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IUserAdministrationService, UserAdministrationService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<AgreementDocumentStorageOptions>()
    .Bind(builder.Configuration.GetSection("AgreementDocuments"))
    .PostConfigure(options =>
    {
        options.RootPath = Path.GetFullPath(options.RootPath, builder.Environment.ContentRootPath);
        var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
        if (options.RootPath.Equals(webRoot, StringComparison.OrdinalIgnoreCase) ||
            options.RootPath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Agreement document storage must be outside wwwroot.");
    })
    .Validate(options => options.MaxFileSizeBytes > 0, "AgreementDocuments:MaxFileSizeBytes must be positive.")
    .ValidateOnStart();
builder.Services.AddSingleton<IAgreementDocumentStorage, LocalAgreementDocumentStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<AgreementAnalysisOptions>().Bind(builder.Configuration.GetSection("AgreementAnalysis"));
builder.Services.AddHttpClient<IContractAnalysisClient, OpenAiContractAnalysisClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<IAgreementAnalysisService>(sp => sp.GetRequiredService<AgreementService>());
builder.Services.AddScoped<AgreementAnalysisCleanup>();
builder.Services.AddHostedService<AgreementAnalysisCleanupWorker>();
builder.Services.AddScoped<AgreementService>();
builder.Services.AddScoped<IAgreementService>(sp => sp.GetRequiredService<AgreementService>());
builder.Services.AddScoped<IAgreementFollowupService>(sp => sp.GetRequiredService<AgreementService>());
builder.Services.AddOptions<AgreementReminderOptions>()
    .Bind(builder.Configuration.GetSection("AgreementReminders"))
    .Validate(o => o.TransportMode is "Capture" or "Smtp", "AgreementReminders:TransportMode must be Capture or Smtp.")
    .PostConfigure(o =>
    {
        o.CapturePath = Path.GetFullPath(o.CapturePath, builder.Environment.ContentRootPath);
        var webRoot = Path.GetFullPath(builder.Environment.WebRootPath ?? Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));
        if (o.CapturePath.Equals(webRoot, StringComparison.OrdinalIgnoreCase) ||
            o.CapturePath.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reminder capture must be outside wwwroot.");
    }).ValidateOnStart();
builder.Services.AddScoped<IAgreementReminderTransport, AgreementReminderTransport>();
builder.Services.AddScoped<AgreementReminderProcessor>();
builder.Services.AddHostedService<AgreementReminderWorker>();
builder.Services.AddScoped<TenantPlatform.Web.Services.MeetingRooms.IMeetingRoomService,
    TenantPlatform.Web.Services.MeetingRooms.MeetingRoomService>();
builder.Services.AddScoped<TenantPlatform.Web.Services.MeetingRooms.IRoomBookingService,
    TenantPlatform.Web.Services.MeetingRooms.RoomBookingService>();
builder.Services.AddScoped<TenantPlatform.Web.Services.MeetingRooms.ICalendarIntegrationService,
    TenantPlatform.Web.Services.MeetingRooms.CalendarIntegrationService>();

builder.Services.AddScoped<CurrentUserContextService>();
builder.Services.AddScoped<ICurrentUserContextService>(services =>
    services.GetRequiredService<CurrentUserContextService>());
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler>(services =>
    services.GetRequiredService<CurrentUserContextService>());

builder.Services.AddHostedService<EmailOutboxWorker>();

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
builder.Services.Configure<ImapOptions>(
    builder.Configuration.GetSection("InboundEmail"));

builder.Services.AddHostedService<
    ImapInboundEmailWorker>();
    
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
    ITenantAuthorizationService authorizationService,
    TenantPlatformDbContext dbContext,
    IFormCollection form,
    CancellationToken cancellationToken) =>
{
    var email =
        form["email"].ToString();

    var password =
        form["password"].ToString();

    var rememberMe =
        string.Equals(
            form["rememberMe"].ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);

    var result =
        await authenticationService.AuthenticateAsync(
            email,
            password,
            cancellationToken);

    if (!result.Succeeded ||
        result.User is null)
    {
        return Results.Redirect(
            "/login?error=invalid-login");
    }

    var user =
        result.User;

    var accountIds =
        await dbContext.UserAccounts
            .AsNoTracking()
            .Where(x =>
                x.UserId == user.Id)
            .Select(x =>
                x.AccountId)
            .ToListAsync(cancellationToken);

    //
    // En ren PlatformAdmin trenger ikke være
    // knyttet til noen Account.
    //
    if (user.IsPlatformAdmin &&
        accountIds.Count == 0)
    {
        await cookieService.SignInAsync(
            httpContext,
            user,
            currentAccountId: null,
            rememberMe);

        cookieService.SetCulture(
            httpContext,
            user.PreferredLanguage);

        return Results.Redirect("/accounts");
    }

    //
    // Vanlige brukere må ha tilgang
    // til minst én Account.
    //
    if (accountIds.Count == 0)
    {
        return Results.Redirect(
            "/login?error=no-account-access");
    }

    var loginAccount =
        await dbContext.LoginAccounts
            .SingleAsync(
                x => x.UserId == user.Id,
                cancellationToken);

    Guid? selectedAccountId = null;

    if (accountIds.Count == 1)
    {
        selectedAccountId =
            accountIds[0];

        loginAccount.LastAccountId =
            selectedAccountId;

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }
    else if (
        loginAccount.LastAccountId.HasValue &&
        accountIds.Contains(
            loginAccount.LastAccountId.Value))
    {
        selectedAccountId =
            loginAccount.LastAccountId.Value;
    }

    //
    // Opprett authentication-cookie.
    //
    await cookieService.SignInAsync(
        httpContext,
        user,
        selectedAccountId,
        rememberMe);

    cookieService.SetCulture(
        httpContext,
        user.PreferredLanguage);

    //
    // Flere Accounts, men ingen tidligere
    // valgt Account.
    //
    if (!selectedAccountId.HasValue)
    {
        return Results.Redirect(
            "/select-account");
    }

    //
    // Finn rollene brukeren faktisk har
    // i valgt Account.
    //
    var roles =
        await dbContext.UserAccountRoles
            .AsNoTracking()
            .Where(x =>
                x.UserAccount.UserId == user.Id &&
                x.UserAccount.AccountId ==
                    selectedAccountId.Value)
            .Select(x => x.Role)
            .Distinct()
            .ToListAsync(cancellationToken);

    var startPage =
        authorizationService.GetStartPage(
            user.IsPlatformAdmin,
            roles);

    return Results.Redirect(startPage);
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
    ITenantAuthorizationService authorizationService,
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

    var roles =
        await dbContext.UserAccountRoles
            .AsNoTracking()
            .Where(x =>
                x.UserAccount.UserId == userId &&
                x.UserAccount.AccountId == accountId)
            .Select(x => x.Role)
            .Distinct()
            .ToListAsync(cancellationToken);

    var startPage =
        authorizationService.GetStartPage(
            user.IsPlatformAdmin,
            roles);

    return Results.Redirect(startPage);
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

    var isSupportedCulture =
        SupportedLanguages.All.Any(
            x => string.Equals(
                x.Code,
                culture,
                StringComparison.OrdinalIgnoreCase));

    if (!isSupportedCulture)
    {
        return Results.BadRequest(
            "Unsupported language.");
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

app.MapAgreementEndpoints();
app.Run();


public record LoginRequest(
    string Email,
    string Password,
    bool RememberMe);

