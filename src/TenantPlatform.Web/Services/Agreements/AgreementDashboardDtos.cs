using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Application.AccountSettings;

namespace TenantPlatform.Web.Services.Agreements;

public static class AgreementDashboardQueries
{
    public const int HorizonDays = AgreementAttentionRules.HorizonDays;

    public static IQueryable<Agreement> Attention(IQueryable<Agreement> source, AgreementAttention kind, DateOnly today) =>
        TenantPlatform.Infrastructure.Agreements.AgreementListQueries.Attention(source, AgreementAttentionRules.Create(kind, today));

    public static IQueryable<Agreement> Prioritized(IQueryable<Agreement> source, DateOnly today) =>
        TenantPlatform.Infrastructure.Agreements.AgreementDashboardReadQueries.Prioritized(source, AgreementPriorityRules.Create(today));

    public static IReadOnlyList<AgreementInsightGroup> GroupHierarchy(
        IEnumerable<Guid?> assignments, IReadOnlyList<AccountHierarchyRow> rows, Guid? selected)
    {
        return AgreementInsightRules.GroupHierarchy(assignments.GroupBy(x => x)
            .Select(g => new AgreementHierarchyCount(g.Key, g.Count())), rows, selected);
    }
}
