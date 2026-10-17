using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Infrastructure.Agreements;

// Only the persistence adapter sees queryables. Criteria carry the Application rules.
public static class AgreementListQueries
{
    public static IQueryable<Agreement> Attention(IQueryable<Agreement> source, AgreementAttentionCriteria criteria)
    {
        var statuses = criteria.Statuses.ToArray();
        var visible = source.Where(x => (!criteria.ExcludeArchived || !x.IsArchived) && statuses.Contains(x.Status));
        return criteria.Date switch
        {
            AgreementAttentionDate.Notice => visible.Where(x => x.NoticeDeadline >= criteria.FromInclusive && x.NoticeDeadline <= criteria.ThroughInclusive),
            AgreementAttentionDate.Renewal => visible.Where(x => x.RenewalDate >= criteria.FromInclusive && x.RenewalDate <= criteria.ThroughInclusive),
            AgreementAttentionDate.End => visible.Where(x => x.EndDate < criteria.Before),
            _ => throw new ArgumentOutOfRangeException(nameof(criteria))
        };
    }
}
