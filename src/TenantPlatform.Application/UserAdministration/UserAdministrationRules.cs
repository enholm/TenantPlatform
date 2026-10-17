using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Localization;
namespace TenantPlatform.Application.UserAdministration;

internal static class UserAdministrationRules
{
    public static void ValidateUserRequest(
        string firstName,
        string lastName,
        string email,
        string preferredLanguage)
    {
        if (string.IsNullOrWhiteSpace(firstName))
        {
            throw new InvalidOperationException(
                "FirstNameRequired");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            throw new InvalidOperationException(
                "LastNameRequired");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                "EmailRequired");
        }

        if (!SupportedLanguages.All.Any(x =>
            string.Equals(
                x.Code,
                preferredLanguage,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "UnsupportedLanguage");
        }
    }

    public static void ValidateRoleScope(
        AddUserRoleRequest request)
    {
        switch (request.Role)
        {
            case UserRole.LeasingEquipmentManager:
            case UserRole.LeasingLifecycleManager:
            case UserRole.LeasingLifecycleApprover:
            case UserRole.LeasingReportReader:
            case UserRole.LeasingReportExporter:
            case UserRole.LeasingPlanApprover:
            case UserRole.LeasingInvoiceApprover:
            case UserRole.LeasingVarianceApprover:
            case UserRole.LeasingOrderApprover:
            case UserRole.LeasingLimitApprover:
            case UserRole.AccountAdmin:

                if (request.OrganizationId.HasValue ||
                    request.BuildingId.HasValue)
                {
                    throw new InvalidOperationException(
                        "AccountAdminCannotHaveScope");
                }

                break;

            case UserRole.PropertyAdmin:

                if (!request.BuildingId.HasValue ||
                    request.OrganizationId.HasValue)
                {
                    throw new InvalidOperationException(
                        "PropertyAdminRequiresBuilding");
                }

                break;

            case UserRole.TenantAdmin:
            case UserRole.TenantUser:
            case UserRole.ServiceProviderUser:

                if (!request.OrganizationId.HasValue ||
                    request.BuildingId.HasValue)
                {
                    throw new InvalidOperationException(
                        "OrganizationRoleRequiresOrganization");
                }

                break;

            default:

                throw new InvalidOperationException(
                    "UnsupportedUserRole");
        }
    }

}
