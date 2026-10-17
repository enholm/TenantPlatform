using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Infrastructure.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class LeasingUserComposition
{
    public static LeasingService Create(IDbContextFactory<TenantPlatformDbContext> factory, ICurrentUserContextService context, IAgreementDocumentStorage storage, TimeProvider clock)
    {
        var authorization = new AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new AuthorizationData(factory), clock);
        var classification = new LeasingClassificationService(authorization, new LeasingClassificationData(factory), clock);
        var followups = new LeasingFollowupService(authorization, new LeasingFollowupData(factory), new LeasingLifecycleJob(new LeasingLifecycleJobData(factory), clock, new HostSystemWorkContext(SystemWorkKind.LeasingLifecycle)), clock);
        return new(storage, classification, followups, new LeasingFrameworkWriter(authorization, new LeasingFrameworkWriteData(factory), clock), new LeasingReader(authorization, new LeasingReadData(factory), clock), new LeasingDocumentService(authorization, new LeasingDocumentData(factory, storage, NullLogger<LeasingDocumentData>.Instance), storage, new PaymentPlanSourceReader(), clock), new LeasingAcquisitionWriter(authorization, new LeasingAcquisitionData(factory), clock), new LeasingOrderReader(authorization, new LeasingOrderReadData(factory), new LeasingReadData(factory), clock), new LeasingOrderWriter(authorization, new LeasingOrderWriteData(factory), clock), new LeasingEquipmentService(authorization, new LeasingEquipmentData(factory), clock), new LeasingLifecycleService(authorization, new LeasingLifecycleData(factory), clock), new LeasingPaymentService(authorization, new LeasingPaymentData(factory), clock), new TenantPlatform.Application.Leasing.Invoices.LeasingPurchaseInvoiceService(authorization, new TenantPlatform.Infrastructure.Leasing.Invoices.LeasingPurchaseInvoiceData(factory, storage, NullLogger<TenantPlatform.Infrastructure.Leasing.Invoices.LeasingPurchaseInvoiceData>.Instance), storage, new LeasingAcquisitionWriter(authorization, new LeasingAcquisitionData(factory), clock), clock), new LeasingRentalInvoiceService(authorization, new LeasingRentalInvoiceData(factory, storage, NullLogger<LeasingRentalInvoiceData>.Instance), storage, clock), new LeasingReportService(authorization, new LeasingReportData(factory), followups, clock));
    }
}
