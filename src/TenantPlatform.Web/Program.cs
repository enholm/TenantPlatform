using TenantPlatform.Application.Agreements;
using ServiceRequestService = TenantPlatform.Web.Services.ServiceRequests.ServiceRequestService;
using TenantPlatform.Application.Authentication;
using ServiceDefinitionFieldService = TenantPlatform.Web.Services.ServiceDefinitionFields.ServiceDefinitionFieldService;
using ServiceDefinitionService = TenantPlatform.Web.Services.ServiceDefinitions.ServiceDefinitionService;
using OccupancyService = TenantPlatform.Web.Services.Occupancies.OccupancyService;
using UnitService = TenantPlatform.Web.Services.Units.UnitService;
using TenantPlatform.Application.Units;
using OrganizationService = TenantPlatform.Web.Services.Organizations.OrganizationService;
using TenantPlatform.Application.Organizations;
using AccountService = TenantPlatform.Web.Services.Accounts.AccountService;
using TenantPlatform.Application.Accounts;
using BuildingService = TenantPlatform.Web.Services.Buildings.BuildingService;
using TenantPlatform.Application.Buildings;
using TenantPlatform.Web.Services.Leasing;
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
using TenantPlatform.Application.Occupancies;
using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Web.Security.Auditing;
using TenantPlatform.Application.ServiceDefinitions;
using TenantPlatform.Core.Localization;
using TenantPlatform.Application.ServiceDefinitionFields;
using TenantPlatform.Application.ServiceCatalog;
using ServiceCatalogService = TenantPlatform.Web.Services.ServiceCatalog.ServiceCatalogService;
using TenantPlatform.Application.ServiceRequests;
using TenantPlatform.Core.Identity;
using TenantPlatform.Web.Email;
using TenantPlatform.Web.Services.Accounts;
using TenantPlatform.Application.UserAdministration;
using UserAdministrationService = TenantPlatform.Web.Services.UserAdministration.UserAdministrationService;
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
builder.Services.AddScoped<AuthenticationSessionService>();
builder.Services.AddScoped<IAuthenticationSessionData, AuthenticationSessionData>();
builder.Services.AddScoped<ILocalAuthenticationService, LocalAuthenticationService>();
builder.Services.AddScoped<AuthenticationCookieService>();
builder.Services.AddScoped<ITenantAuthorizationService, TenantAuthorizationService>();
builder.Services.AddScoped<TenantPlatform.Application.Buildings.BuildingService>();
builder.Services.AddScoped<TenantPlatform.Application.Buildings.IBuildingData, TenantPlatform.Infrastructure.Buildings.BuildingData>();
builder.Services.AddScoped<IBuildingService, BuildingService>();
builder.Services.AddScoped<TenantPlatform.Application.Organizations.OrganizationService>();
builder.Services.AddScoped<TenantPlatform.Application.Organizations.IOrganizationData, TenantPlatform.Infrastructure.Organizations.OrganizationData>();
builder.Services.AddScoped<IOrganizationService, OrganizationService>();
builder.Services.AddScoped<TenantPlatform.Application.Units.UnitService>();
builder.Services.AddScoped<TenantPlatform.Application.Units.IUnitData, TenantPlatform.Infrastructure.Units.UnitData>();
builder.Services.AddScoped<IUnitService, UnitService>();
builder.Services.AddScoped<TenantPlatform.Application.Occupancies.OccupancyService>();
builder.Services.AddScoped<TenantPlatform.Application.Occupancies.IOccupancyData, TenantPlatform.Infrastructure.Occupancies.OccupancyData>();
builder.Services.AddScoped<IOccupancyService, OccupancyService>();
builder.Services.AddScoped<IAuditUserContext, AuditUserContext>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceDefinitions.IServiceDefinitionData, TenantPlatform.Infrastructure.ServiceDefinitions.ServiceDefinitionData>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceDefinitions.ServiceDefinitionService>();
builder.Services.AddScoped<IServiceDefinitionService, ServiceDefinitionService>();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceDefinitionFields.ServiceDefinitionFieldService>();
builder.Services.AddScoped<IServiceDefinitionFieldService, ServiceDefinitionFieldService>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceCatalog.ServiceCatalogService>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceCatalog.IServiceCatalogLocationData, TenantPlatform.Infrastructure.ServiceDefinitions.ServiceCatalogLocationData>();
builder.Services.AddScoped<IServiceCatalogService, ServiceCatalogService>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceRequests.ServiceRequestService>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceRequests.IServiceRequestData, TenantPlatform.Infrastructure.ServiceRequests.ServiceRequestData>();
builder.Services.AddScoped<IServiceRequestService, ServiceRequestService>();
builder.Services.AddScoped<IServiceRequestEmailAddressService, ServiceRequestEmailAddressService>();
builder.Services.AddScoped<IServiceRequestEmailComposer, ServiceRequestEmailComposer>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<IInboundServiceRequestEmailService, InboundServiceRequestEmailService>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceRequests.IInboundServiceRequestData, TenantPlatform.Infrastructure.ServiceRequests.InboundServiceRequestData>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceRequests.IServiceRequestOutboxData, TenantPlatform.Infrastructure.ServiceRequests.ServiceRequestOutboxData>();
builder.Services.AddScoped<TenantPlatform.Application.ServiceRequests.InboundServiceRequestEmailJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.ServiceRequests.IInboundServiceRequestData>(), services.GetRequiredService<IServiceRequestReplyAddressParser>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.ServiceRequestInbound)));
builder.Services.AddScoped<TenantPlatform.Application.ServiceRequests.EmailOutboxJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.ServiceRequests.IServiceRequestOutboxData>(), services.GetRequiredService<IEmailSender>(),
    services.GetRequiredService<IServiceRequestEmailAddressService>(), services.GetRequiredService<TimeProvider>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.EmailOutbox)));
builder.Services.AddScoped<IServiceRequestReplyAddressParser, ServiceRequestReplyAddressParser>();
builder.Services.AddScoped<TenantPlatform.Application.Accounts.AccountService>();
builder.Services.AddScoped<TenantPlatform.Application.Accounts.IAccountData, TenantPlatform.Infrastructure.Accounts.AccountData>();
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<TenantPlatform.Application.UserAdministration.UserAdministrationService>();
builder.Services.AddScoped<TenantPlatform.Application.UserAdministration.IUserAdministrationData, TenantPlatform.Infrastructure.UserAdministration.UserAdministrationData>();
builder.Services.AddScoped<TenantPlatform.Application.UserAdministration.IUserPasswordHasher, PasswordService>();
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
builder.Services.AddOptions<TenantPlatform.Infrastructure.Agreements.AgreementAnalysisOptions>().Bind(builder.Configuration.GetSection("AgreementAnalysis"));
builder.Services.AddHttpClient<TenantPlatform.Application.Agreements.IContractAnalysisClient, TenantPlatform.Infrastructure.Agreements.OpenAiContractAnalysisClient>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementAnalysisService>(sp => sp.GetRequiredService<AgreementService>());
builder.Services.AddScoped<AgreementAnalysisCleanup>();
builder.Services.AddHostedService<AgreementAnalysisCleanupWorker>();
builder.Services.AddScoped<TenantPlatform.Application.AccountSettings.AccountRegisterService>();
builder.Services.AddScoped<TenantPlatform.Application.AccountSettings.IAccountRegisterData, TenantPlatform.Infrastructure.AccountSettings.AccountRegisterData>();
builder.Services.AddScoped<TenantPlatform.Web.Services.AccountSettings.OrganizationElementService>();
builder.Services.AddScoped<TenantPlatform.Web.Services.AccountSettings.GeographicAreaService>();
builder.Services.AddScoped<LeasingService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingReportService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingReportData, TenantPlatform.Infrastructure.Leasing.LeasingReportData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingRentalInvoiceService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingRentalInvoiceData, TenantPlatform.Infrastructure.Leasing.LeasingRentalInvoiceData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.Invoices.LeasingPurchaseInvoiceService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.Invoices.ILeasingPurchaseInvoiceData, TenantPlatform.Infrastructure.Leasing.Invoices.LeasingPurchaseInvoiceData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingPaymentService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingPaymentData, TenantPlatform.Infrastructure.Leasing.LeasingPaymentData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingLifecycleService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingLifecycleData, TenantPlatform.Infrastructure.Leasing.LeasingLifecycleData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingEquipmentService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingEquipmentData, TenantPlatform.Infrastructure.Leasing.LeasingEquipmentData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingOrderWriter>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingOrderWriteData, TenantPlatform.Infrastructure.Leasing.LeasingOrderWriteData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingOrderReader>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingOrderReadData, TenantPlatform.Infrastructure.Leasing.LeasingOrderReadData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingAcquisitionWriter>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingAcquisitionData, TenantPlatform.Infrastructure.Leasing.LeasingAcquisitionData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingDocumentService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingDocumentData, TenantPlatform.Infrastructure.Leasing.LeasingDocumentData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.IPaymentPlanSourceReader, TenantPlatform.Infrastructure.Leasing.PaymentPlanSourceReader>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingReader>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingReadData, TenantPlatform.Infrastructure.Leasing.LeasingReadData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingFrameworkWriter>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingFrameworkWriteData, TenantPlatform.Infrastructure.Leasing.LeasingFrameworkWriteData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingClassificationService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingClassificationData, TenantPlatform.Infrastructure.Leasing.LeasingClassificationData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingFollowupService>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingFollowupData, TenantPlatform.Infrastructure.Leasing.LeasingFollowupData>();
builder.Services.AddScoped<LeasingLifecycleProcessor>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.ILeasingLifecycleJobData, TenantPlatform.Infrastructure.Leasing.LeasingLifecycleJobData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.LeasingLifecycleJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.Leasing.ILeasingLifecycleJobData>(), services.GetRequiredService<TimeProvider>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.LeasingLifecycle)));
builder.Services.AddHostedService<LeasingLifecycleWorker>();
builder.Services.AddOptions<TenantPlatform.Infrastructure.Leasing.Invoices.LeasingInvoiceInterpretationOptions>().Bind(builder.Configuration.GetSection("LeasingInvoiceInterpretation"));
builder.Services.AddHttpClient<TenantPlatform.Application.Leasing.Invoices.IInvoiceDocumentInterpreter, TenantPlatform.Infrastructure.Leasing.Invoices.InvoiceDocumentInterpreter>(client => client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<TenantPlatform.Web.Services.Leasing.Invoices.LeasingInvoiceProcessor>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.Invoices.ILeasingInvoiceJobData, TenantPlatform.Infrastructure.Leasing.Invoices.LeasingInvoiceJobData>();
builder.Services.AddScoped<TenantPlatform.Application.Leasing.Invoices.LeasingInvoiceJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.Leasing.Invoices.ILeasingInvoiceJobData>(), services.GetRequiredService<TenantPlatform.Application.Leasing.Invoices.IInvoiceDocumentInterpreter>(),
    services.GetRequiredService<TimeProvider>(), new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.LeasingInvoices)));
builder.Services.AddHostedService<TenantPlatform.Web.Services.Leasing.Invoices.LeasingInvoiceWorker>();
builder.Services.AddScoped<TenantPlatform.Application.Dimensions.DimensionService>();
builder.Services.AddScoped<TenantPlatform.Application.Dimensions.IDimensionData, TenantPlatform.Infrastructure.AccountSettings.DimensionData>();
builder.Services.AddScoped<TenantPlatform.Web.Services.Dimensions.DimensionService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementInitializationService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementFollowupWriter>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementIndexService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementDocumentService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementManagementService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementAnalysisService>();
builder.Services.AddScoped<TenantPlatform.Application.Initialization.IPlatformSeedData, PlatformSeedData>();
builder.Services.AddScoped<TenantPlatform.Application.Initialization.IDemoSeedData, DemoSeedData>();
builder.Services.AddSingleton<TenantPlatform.Application.Initialization.IBootstrapEnvironment>(new HostBootstrapEnvironment(builder.Environment.IsDevelopment()));
builder.Services.AddScoped<TenantPlatform.Application.Initialization.PlatformInitializationJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.Initialization.IPlatformSeedData>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.PlatformSeed)));
builder.Services.AddScoped<TenantPlatform.Application.Initialization.DemoInitializationJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.Initialization.IDemoSeedData>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.DemoSeed),
    services.GetRequiredService<TenantPlatform.Application.Initialization.IBootstrapEnvironment>()));
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementAnalysisData, TenantPlatform.Infrastructure.Agreements.AgreementAnalysisData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementAnalysisCleanupService>(services => new(
    new TenantPlatform.Infrastructure.Agreements.AgreementAnalysisCleanupData(services.GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>()),
    services.GetRequiredService<TenantPlatform.Application.Agreements.IAgreementDocumentStorage>(), services.GetRequiredService<TimeProvider>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.AgreementAnalysisCleanup)));
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementFinancialService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementFinancialData, TenantPlatform.Infrastructure.Agreements.AgreementFinancialData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementManagementData, TenantPlatform.Infrastructure.Agreements.AgreementManagementData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDocumentData, TenantPlatform.Infrastructure.Agreements.AgreementDocumentData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementIndexData, TenantPlatform.Infrastructure.Agreements.AgreementIndexData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementFollowupWriteData, TenantPlatform.Infrastructure.Agreements.AgreementFollowupWriteData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementInitializationData, TenantPlatform.Infrastructure.Agreements.AgreementDeadlineInitializer>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDeadlineReader, TenantPlatform.Application.Agreements.AgreementDeadlineReader>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDeadlineData, TenantPlatform.Infrastructure.Agreements.AgreementDeadlineData>();
builder.Services.AddScoped<TenantPlatform.Application.Dashboard.HomeReader>();
builder.Services.AddScoped<TenantPlatform.Application.Dashboard.IHomeData, TenantPlatform.Infrastructure.Authorization.HomeData>();
builder.Services.AddScoped<TenantPlatform.Application.Security.NavigationReader>();
builder.Services.AddScoped<TenantPlatform.Application.Security.AccountContextReader>();
builder.Services.AddScoped<TenantPlatform.Application.Security.IAccountContextData, TenantPlatform.Infrastructure.Authorization.AccountContextData>();
builder.Services.AddScoped<TenantPlatform.Application.Security.IAuthorizationData, TenantPlatform.Infrastructure.Authorization.AuthorizationData>();
builder.Services.AddScoped<TenantPlatform.Application.Security.IAuthorizationService, TenantPlatform.Application.Security.AuthorizationService>();
builder.Services.AddScoped<TenantPlatform.Application.Security.ICurrentActor, TenantPlatform.Web.Security.Application.CurrentActorAdapter>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementListData, AgreementListData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementFollowupData, TenantPlatform.Infrastructure.Agreements.AgreementFollowupData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementFollowupReader, TenantPlatform.Application.Agreements.AgreementFollowupReader>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDetailsData, TenantPlatform.Infrastructure.Agreements.AgreementDetailsData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDetailsReader, TenantPlatform.Application.Agreements.AgreementDetailsReader>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementInsightsData, TenantPlatform.Infrastructure.Agreements.AgreementInsightsData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementInsightsService, TenantPlatform.Application.Agreements.AgreementInsightsService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDashboardData, TenantPlatform.Infrastructure.Agreements.AgreementDashboardData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementDashboardService, TenantPlatform.Application.Agreements.AgreementDashboardService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementAttentionData, TenantPlatform.Infrastructure.Agreements.AgreementAttentionData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementAttentionService, TenantPlatform.Application.Agreements.AgreementAttentionService>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementListService, TenantPlatform.Application.Agreements.AgreementListService>();
builder.Services.AddScoped<AgreementService>();
builder.Services.AddScoped<IAgreementService>(sp => sp.GetRequiredService<AgreementService>());
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementFollowupService>(sp => sp.GetRequiredService<AgreementService>());
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
builder.Services.AddScoped<TenantPlatform.Application.Agreements.IAgreementReminderData, TenantPlatform.Infrastructure.Agreements.AgreementReminderData>();
builder.Services.AddScoped<TenantPlatform.Application.Agreements.AgreementReminderJob>(services => new(
    services.GetRequiredService<TenantPlatform.Application.Agreements.IAgreementReminderData>(), services.GetRequiredService<TimeProvider>(),
    services.GetRequiredService<TenantPlatform.Application.Agreements.IAgreementReminderTransport>(),
    new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.AgreementReminders)));
builder.Services.AddHostedService<AgreementReminderWorker>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.MeetingRoomAccess>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.IMeetingRoomData, TenantPlatform.Infrastructure.MeetingRooms.MeetingRoomData>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.MeetingRoomService>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.RoomBookingService>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.CalendarIntegrationService>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.IMeetingRoomService,
    TenantPlatform.Web.Services.MeetingRooms.MeetingRoomService>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.IRoomBookingService,
    TenantPlatform.Web.Services.MeetingRooms.RoomBookingService>();
builder.Services.AddScoped<TenantPlatform.Application.MeetingRooms.ICalendarIntegrationService,
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


// Schema is technical bootstrap. Fixed-data creation uses separate bounded system use cases.
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await using (var db = await factory.CreateDbContextAsync())
        await DatabaseInitializer.MigrateSchemaAsync(db, startupLogger);
    await scope.ServiceProvider.GetRequiredService<TenantPlatform.Application.Initialization.PlatformInitializationJob>().InitializeAsync();
    await scope.ServiceProvider.GetRequiredService<TenantPlatform.Application.Initialization.DemoInitializationJob>().InitializeAsync();
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
app.MapPost("/auth/login", async (HttpContext httpContext, AuthenticationSessionService sessions,
    AuthenticationCookieService cookies, ITenantAuthorizationService navigation, IFormCollection form, CancellationToken ct) =>
{
    AuthenticationSession session;
    try { session = await sessions.LoginAsync(form["email"].ToString(), form["password"].ToString(), ct); }
    catch (UnauthorizedAccessException) { return Results.Redirect("/login?error=invalid-login"); }
    if (session.User is null) return Results.Redirect(session.Error == "no-account-access" ? "/login?error=no-account-access" : "/login?error=invalid-login");
    var remember = string.Equals(form["rememberMe"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
    await cookies.SignInAsync(httpContext, session.User, session.AccountId, remember);
    cookies.SetCulture(httpContext, session.User.PreferredLanguage);
    if (!session.AccountId.HasValue) return Results.Redirect(session.SelectAccountRequired ? "/select-account" : "/accounts");
    return Results.Redirect(navigation.GetStartPage(session.User.IsPlatformAdmin, session.Roles));
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
app.MapPost("/auth/select-account", async (HttpContext httpContext, AuthenticationSessionService sessions,
    AuthenticationCookieService cookies, ITenantAuthorizationService navigation, IFormCollection form, CancellationToken ct) =>
{
    if (httpContext.User.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    if (!Guid.TryParse(form["accountId"], out var account)) return Results.BadRequest("Invalid account ID.");
    AuthenticationSession session;
    try { session = await sessions.SelectAccountAsync(account, ct); }
    catch (UnauthorizedAccessException) { return Results.Forbid(); }
    var authentication = await httpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await cookies.SignInAsync(httpContext, session.User!, session.AccountId, authentication.Properties?.IsPersistent ?? false);
    return Results.Redirect(navigation.GetStartPage(session.User!.IsPlatformAdmin, session.Roles));
});

//------------------------------
// Endpoint preferences/language
// -----------------------------
app.MapPost("/preferences/language", async (HttpContext httpContext, AuthenticationSessionService sessions,
    AuthenticationCookieService cookies, IFormCollection form, CancellationToken ct) =>
{
    string language;
    try { language = await sessions.ChangeLanguageAsync(form["culture"].ToString(), ct); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (ArgumentException) { return Results.BadRequest("Unsupported language."); }
    cookies.SetCulture(httpContext, language);
    return Results.Redirect("/");
});
// ----------------------------------------------------------------------

app.MapAgreementEndpoints();
app.MapLeasingEndpoints();
app.Run();


public record LoginRequest(
    string Email,
    string Password,
    bool RememberMe);

