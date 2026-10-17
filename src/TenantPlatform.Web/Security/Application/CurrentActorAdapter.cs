using TenantPlatform.Application.Security;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Security.Application;

public sealed class CurrentActorAdapter(ICurrentUserContextService context) : ICurrentActor
{
    public ActorSnapshot GetSnapshot()
    {
        var current = context.Current;
        return new(current.IsAuthenticated, current.UserId, current.CurrentAccountId);
    }
}
