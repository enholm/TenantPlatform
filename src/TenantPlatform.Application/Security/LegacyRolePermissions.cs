using TenantPlatform.Core.Identity;

namespace TenantPlatform.Application.Security;

public static class LegacyRolePermissions
{
    public static IReadOnlyList<string> For(UserRole role) => role switch
    {
        UserRole.AccountAdmin =>
        [
            Permissions.Users.Manage,
            Permissions.Agreements.Open, Permissions.Agreements.ReadAll, Permissions.Agreements.Create,
            Permissions.Agreements.Read, Permissions.Agreements.Update, Permissions.Agreements.ManageSharing,
            Permissions.Buildings.Open, Permissions.Buildings.Create, Permissions.Buildings.Read,
            Permissions.Buildings.Update, Permissions.Buildings.Delete, Permissions.Organizations.Open,
            Permissions.Organizations.Create, Permissions.Organizations.Read, Permissions.Organizations.Update,
            Permissions.Organizations.Delete, Permissions.Units.Open, Permissions.Units.Create,
            Permissions.Units.Read, Permissions.Units.Update, Permissions.Units.Delete,
            Permissions.Occupancies.Open, Permissions.Occupancies.Create, Permissions.Occupancies.Update,
            Permissions.Occupancies.Read,
            Permissions.Occupancies.End, Permissions.Occupancies.Delete, Permissions.ServiceDefinitions.Open,
            Permissions.ServiceDefinitions.Create, Permissions.ServiceDefinitions.Read, Permissions.ServiceDefinitions.Update,
            Permissions.ServiceDefinitions.Delete, Permissions.ServiceRequests.Open, Permissions.ServiceRequests.Approve,
            Permissions.ServiceRequests.Complete, Permissions.Leasing.Open, Permissions.Leasing.ApproveOrders,
            Permissions.Leasing.ApproveLimit, Permissions.Leasing.ActivatePlans, Permissions.Leasing.ApproveInvoices,
            Permissions.Leasing.AcceptVariances, Permissions.Leasing.MaintainEquipment, Permissions.Leasing.ManageLifecycle,
            Permissions.Leasing.ApproveLifecycle, Permissions.Leasing.ReadReports, Permissions.Leasing.ExportReports,
            Permissions.MeetingRooms.Open, Permissions.MeetingRooms.Manage, Permissions.Navigation.Home,
            Permissions.Navigation.TenantPortal, Permissions.AccountStructure.Manage,
        ],
        UserRole.PropertyAdmin =>
        [
            Permissions.Buildings.Open, Permissions.Buildings.Read, Permissions.Buildings.Update,
            Permissions.Buildings.Delete, Permissions.Units.Open, Permissions.Units.Create,
            Permissions.Units.Read, Permissions.Units.Update, Permissions.Units.Delete,
            Permissions.Occupancies.Open, Permissions.Occupancies.Create, Permissions.Occupancies.Update,
            Permissions.Occupancies.Read,
            Permissions.Occupancies.End, Permissions.Occupancies.Delete, Permissions.Organizations.Read,
            Permissions.ServiceDefinitions.Read, Permissions.ServiceRequests.Open, Permissions.ServiceRequests.Approve,
            Permissions.ServiceRequests.Complete, Permissions.Navigation.Home, Permissions.Navigation.TenantPortal,
        ],
        UserRole.TenantAdmin =>
        [
            Permissions.Organizations.Read, Permissions.Organizations.Update, Permissions.Buildings.Read,
            Permissions.Units.Read, Permissions.ServiceDefinitions.Read, Permissions.Navigation.TenantPortal,
        ],
        UserRole.TenantUser =>
        [
            Permissions.Organizations.Read, Permissions.Buildings.Read, Permissions.Units.Read,
            Permissions.ServiceDefinitions.Read, Permissions.Navigation.TenantPortal,
        ],
        UserRole.ServiceProviderUser => [Permissions.Navigation.ProviderPortal],
        UserRole.LeasingOrderApprover => [Permissions.Leasing.Open, Permissions.Leasing.ApproveOrders],
        UserRole.LeasingLimitApprover => [Permissions.Leasing.Open, Permissions.Leasing.ApproveLimit],
        UserRole.LeasingPlanApprover => [Permissions.Leasing.Open, Permissions.Leasing.ActivatePlans],
        UserRole.LeasingInvoiceApprover => [Permissions.Leasing.Open, Permissions.Leasing.ApproveInvoices],
        UserRole.LeasingVarianceApprover => [Permissions.Leasing.Open, Permissions.Leasing.AcceptVariances],
        UserRole.LeasingEquipmentManager => [Permissions.Leasing.Open, Permissions.Leasing.MaintainEquipment],
        UserRole.LeasingLifecycleManager => [Permissions.Leasing.Open, Permissions.Leasing.ManageLifecycle],
        UserRole.LeasingLifecycleApprover => [Permissions.Leasing.Open, Permissions.Leasing.ApproveLifecycle],
        UserRole.LeasingReportReader => [Permissions.Leasing.Open, Permissions.Leasing.ReadReports],
        UserRole.LeasingReportExporter => [Permissions.Leasing.Open, Permissions.Leasing.ExportReports],
        _ => []
    };

    public static bool Grants(RoleAssignment assignment, string permission) => For(assignment.Role).Contains(permission);
}
