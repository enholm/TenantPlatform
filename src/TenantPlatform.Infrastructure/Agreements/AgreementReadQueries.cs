using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

internal static class AgreementReadQueries
{
    public static IQueryable<Agreement> Visible(TenantPlatformDbContext db, AgreementListScope scope) =>
        db.Agreements.AsNoTracking().Where(x => x.AccountId == scope.AccountId &&
            (scope.CanReadAll || x.OwnerUserId == scope.UserId || db.AgreementAccess.Any(g =>
                g.AccountId == scope.AccountId && g.AgreementId == x.Id && g.UserId == scope.UserId)));

    public static async Task<string> ReminderTimeZoneAsync(TenantPlatformDbContext db, Guid accountId, CancellationToken ct) =>
        await db.AgreementReminderSettings.AsNoTracking().Where(x => x.AccountId == accountId)
            .Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? new AgreementReminderSettings().TimeZoneId;
}
