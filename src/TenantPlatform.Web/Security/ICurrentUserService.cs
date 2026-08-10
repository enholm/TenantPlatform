namespace TenantPlatform.Web.Security;

public interface ICurrentUserService
{
    CurrentUser CurrentUser { get; }
}
