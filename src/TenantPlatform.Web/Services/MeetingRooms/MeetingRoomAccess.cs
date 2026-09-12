using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.MeetingRooms;

internal static class MeetingRoomAccess
{
    public static async Task RequireAsync(
        Guid accountId, ICurrentUserContextService userContext,
        ITenantAuthorizationService authorization, bool administration,
        CancellationToken cancellationToken)
    {
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId ||
            !(administration
                ? await authorization.CanManageMeetingRoomsAsync(cancellationToken)
                : await authorization.CanUseMeetingRoomsAsync(cancellationToken)))
            throw new UnauthorizedAccessException();
    }
}
