namespace TenantPlatform.Core.Identity;

public enum UserRole
{
    AccountAdmin = 2,
    LeasingOrderApprover = 7,
    LeasingLimitApprover = 8,
    LeasingPlanApprover = 9,
    LeasingInvoiceApprover = 10,
    LeasingVarianceApprover = 11,
    LeasingEquipmentManager = 12,
    LeasingLifecycleManager = 13,
    LeasingLifecycleApprover = 14,
    LeasingReportReader = 15,
    LeasingReportExporter = 16,
    PropertyAdmin = 3,
    TenantAdmin = 4,
    TenantUser = 5,
    ServiceProviderUser = 6
}
