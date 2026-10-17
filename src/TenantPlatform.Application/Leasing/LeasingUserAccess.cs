using TenantPlatform.Application.Security;
namespace TenantPlatform.Application.Leasing;

public sealed record LeasingUserScope(Guid AccountId, Guid UserId, bool IsAdministrator, bool CanReadLifecycle, bool CanReadOrders, bool CanManageLifecycle);
public static class LeasingUserAccess
{
    // Existing action checks require account-wide assignments. These flags never replace
    // concrete ownership checks; global platform administration confers no tenant access.
    public static bool Has(AccountActorScope actor, string permission) => actor.Assignments.Any(x => x.AccountId == actor.AccountId && x.BuildingId is null && x.OrganizationId is null && LegacyRolePermissions.Grants(x, permission));
    public static LeasingUserScope Create(AccountActorScope actor)
    {
        bool Has(string permission) => LeasingUserAccess.Has(actor, permission);
        var admin = Has(Permissions.Agreements.Create);
        return new(actor.AccountId, actor.UserId, admin,
            admin || Has(Permissions.Leasing.MaintainEquipment) || Has(Permissions.Leasing.ManageLifecycle) || Has(Permissions.Leasing.ApproveLifecycle) || Has(Permissions.Leasing.ReadReports) || Has(Permissions.Leasing.ExportReports),
            admin || Has(Permissions.Leasing.ApproveOrders), admin || Has(Permissions.Leasing.ManageLifecycle));
    }
}
