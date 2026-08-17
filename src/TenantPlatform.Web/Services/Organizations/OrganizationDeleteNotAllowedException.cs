namespace TenantPlatform.Web.Services.Organizations;

public class OrganizationDeleteNotAllowedException : Exception
{
    public OrganizationDeleteNotAllowedException(
        string message)
        : base(message)
    {
    }
}

