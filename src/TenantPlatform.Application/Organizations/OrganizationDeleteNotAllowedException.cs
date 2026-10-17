namespace TenantPlatform.Application.Organizations;

public class OrganizationDeleteNotAllowedException : Exception
{
    public OrganizationDeleteNotAllowedException(
        string message)
        : base(message)
    {
    }
}

