using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Security.Auditing;

public class AuditUserContext : IAuditUserContext
{
    private readonly ICurrentUserContextService _currentUserContextService;

    public AuditUserContext(
        ICurrentUserContextService currentUserContextService)
    {
        _currentUserContextService = currentUserContextService;
    }

    public Guid? UserId =>
        _currentUserContextService.Current.IsAuthenticated
            ? _currentUserContextService.Current.UserId
            : null;

    public Guid? AccountId =>
        _currentUserContextService.Current.CurrentAccountId;

    public string? Email =>
        _currentUserContextService.Current.IsAuthenticated
            ? _currentUserContextService.Current.Email
            : null;
}

