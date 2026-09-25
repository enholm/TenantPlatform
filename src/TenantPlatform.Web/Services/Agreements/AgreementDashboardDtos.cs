using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Services.AccountSettings;

namespace TenantPlatform.Web.Services.Agreements;

public enum AgreementAttention { Notice, Renewal, Expired }
public enum AgreementInsightDimension { Type, Counterparty, Geography, Organization, Status, Owner }
public sealed record AgreementAttentionDto(DateOnly Today, int Notice, int Renewal, int Expired);
public sealed record AgreementDashboardItem(Guid Id, string Title, AgreementType Type, DateOnly? EndDate);
public sealed record AgreementDashboardList(DateOnly Today, IReadOnlyList<AgreementDashboardItem> Items);
public sealed record AgreementInsightGroup(string Key, string? Name, AgreementType? Type, int Count, bool Direct = false, AgreementStatus? Status = null);
public sealed record AgreementInsightsDto(IReadOnlyList<AgreementInsightGroup> Groups, IReadOnlyList<AccountHierarchyRow> Options)
{
    public int Total => Groups.Sum(x => x.Count);
}

public static class AgreementDashboardQueries
{
    public const int HorizonDays = 90;

    public static IQueryable<Agreement> Attention(IQueryable<Agreement> source, AgreementAttention kind, DateOnly today)
    {
        var until = today.AddDays(HorizonDays);
        var visible = source.Where(x => !x.IsArchived);
        // Upcoming action follows reminder eligibility. Explicitly expired records and active records
        // whose end date has passed remain visible as expired; drafts and terminated records do not.
        return kind switch
        {
            AgreementAttention.Notice => visible.Where(x => x.Status == AgreementStatus.Active &&
                x.NoticeDeadline >= today && x.NoticeDeadline <= until),
            AgreementAttention.Renewal => visible.Where(x => x.Status == AgreementStatus.Active &&
                x.RenewalDate >= today && x.RenewalDate <= until),
            AgreementAttention.Expired => visible.Where(x =>
                (x.Status == AgreementStatus.Active || x.Status == AgreementStatus.Expired) && x.EndDate < today),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    public static IQueryable<Agreement> Prioritized(IQueryable<Agreement> source, DateOnly today) =>
        Attention(source, AgreementAttention.Expired, today)
            .Union(Attention(source, AgreementAttention.Notice, today))
            .OrderBy(x => x.EndDate < today ? 0 : 1)
            .ThenByDescending(x => x.EndDate < today ? x.EndDate : null)
            .ThenBy(x => x.EndDate < today ? null : x.NoticeDeadline)
            .ThenBy(x => x.Title).ThenBy(x => x.Id).Take(5);

    public static IReadOnlyList<AgreementInsightGroup> GroupHierarchy(
        IEnumerable<Guid?> assignments, IReadOnlyList<AccountHierarchyRow> rows, Guid? selected)
    {
        var byId = rows.ToDictionary(x => x.Id);
        if (selected.HasValue && !byId.ContainsKey(selected.Value)) throw new UnauthorizedAccessException();
        var keys = new List<(Guid? Id, bool Direct)>();
        foreach (var id in assignments)
        {
            if (!id.HasValue || !byId.TryGetValue(id.Value, out var row))
            {
                if (!selected.HasValue) keys.Add((null, false));
                continue;
            }
            if (!selected.HasValue) keys.Add((row.Ancestors.FirstOrDefault(row.Id), false));
            else if (row.Id == selected) keys.Add((row.Id, true));
            else
            {
                var path = row.Ancestors.Append(row.Id).ToList();
                var parentIndex = path.IndexOf(selected.Value);
                if (parentIndex >= 0) keys.Add((path[parentIndex + 1], false));
            }
        }
        return keys.GroupBy(x => x).Select(g => new AgreementInsightGroup(
            g.Key.Id?.ToString() ?? "missing", g.Key.Id.HasValue ? byId[g.Key.Id.Value].Path : null,
            null, g.Count(), g.Key.Direct)).OrderByDescending(x => x.Count).ThenBy(x => x.Name).ToList();
    }
}
