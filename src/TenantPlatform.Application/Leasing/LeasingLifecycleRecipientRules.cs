using TenantPlatform.Application.Security;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record LeasingRecipientFacts(Guid UserId, bool IsActive, bool IsMember, IReadOnlyList<UserRole> Roles);
public sealed record LeasingRecipientScope(LeasingRecipientFacts? Recipient, LeasingAcquisition? Acquisition, LeasingFramework? Framework);
public static class LeasingLifecycleRecipientRules
{
    // Explicit legacy lifecycle recipient rule. It deliberately treats these existing action roles
    // as account-area readers, without granting any write/approval or global PlatformAdmin bypass.
    public static bool CanRead(LeasingRecipientScope scope)
    {
        var recipient = scope.Recipient;
        if (recipient is not { IsActive: true, IsMember: true } || scope.Acquisition is null && scope.Framework is null) return false;
        var reader = recipient.Roles.Any(role => LegacyRolePermissions.For(role).Any(permission => permission is
            Permissions.Leasing.MaintainEquipment or Permissions.Leasing.ManageLifecycle or Permissions.Leasing.ApproveLifecycle or Permissions.Leasing.ReadReports or Permissions.Leasing.ExportReports));
        return reader || scope.Acquisition?.OwnerUserId == recipient.UserId || scope.Framework?.OwnerUserId == recipient.UserId;
    }
}
