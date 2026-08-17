namespace TenantPlatform.Web.Services.Organizations;

public class OrganizationDeleteCheckResult
{
    public bool CanDelete { get; init; }

    public string? Reason { get; init; }

    public static OrganizationDeleteCheckResult Allowed() =>
        new()
        {
            CanDelete = true
        };

    public static OrganizationDeleteCheckResult NotAllowed(
        string reason) =>
        new()
        {
            CanDelete = false,
            Reason = reason
        };
}

