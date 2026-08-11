namespace TenantPlatform.Web.Security.CurrentUserContext;

public interface ICurrentUserContextService
{
    CurrentUserContext Current { get; }
}
