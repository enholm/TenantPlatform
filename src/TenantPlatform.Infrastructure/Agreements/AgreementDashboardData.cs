using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Agreements;

public sealed class AgreementDashboardData(IDbContextFactory<TenantPlatformDbContext> factory) : IAgreementDashboardData
{
    public async Task<string> GetReminderTimeZoneAsync(Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await AgreementReadQueries.ReminderTimeZoneAsync(db, accountId, ct);
    }

    public async Task<AgreementDashboardList> QueryAsync(AgreementListScope scope, AgreementPriorityQuery query, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var items = await AgreementDashboardReadQueries.Prioritized(AgreementReadQueries.Visible(db, scope), query)
            .Select(x => new AgreementDashboardItem(x.Id, x.Title, x.Type, x.EndDate)).ToListAsync(ct);
        return new(query.Today, items);
    }
}

public static class AgreementDashboardReadQueries
{
    public static IQueryable<Agreement> Prioritized(IQueryable<Agreement> source, AgreementPriorityQuery query)
    {
        var today = query.Today;
        return AgreementListQueries.Attention(source, query.Expired)
            .Union(AgreementListQueries.Attention(source, query.Notice))
            .OrderBy(x => x.EndDate < today ? 0 : 1)
            .ThenByDescending(x => x.EndDate < today ? x.EndDate : null)
            .ThenBy(x => x.EndDate < today ? null : x.NoticeDeadline)
            .ThenBy(x => x.Title).ThenBy(x => x.Id).Take(AgreementPriorityRules.Limit);
    }
}
