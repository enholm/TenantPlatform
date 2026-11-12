using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public enum AgreementAttention { Notice, Renewal, Expired }
public enum AgreementInsightDimension { Type, Counterparty, Status, Owner }
// Exactly one discriminator must be set; dimension identity is always a GUID, never a code/name.
public sealed record AgreementInsightGrouping(AgreementInsightDimension? Fixed = null, Guid? DimensionId = null)
{
    public void Validate()
    {
        if (Fixed.HasValue == DimensionId.HasValue || Fixed.HasValue && !Enum.IsDefined(Fixed.Value) || DimensionId == Guid.Empty)
            throw new ArgumentException("Invalid agreement insight grouping.");
    }
}
public sealed record AgreementInsightDimensionOption(Guid Id, string Name);
public enum AgreementInsightGroupKind { Fixed, Node, Direct, Unspecified }
public sealed record AgreementInsightNode(Guid Id, Guid? ParentId, string Path, int Depth, bool IsActive);
public sealed record AgreementAttentionDto(DateOnly Today, int Notice, int Renewal, int Expired);
public sealed record AgreementDashboardItem(Guid Id, string Title, AgreementType Type, DateOnly? EndDate);
public sealed record AgreementDashboardList(DateOnly Today, IReadOnlyList<AgreementDashboardItem> Items);
public sealed record AgreementInsightGroup(string Key, string? Name, AgreementType? Type, int Count,
    AgreementStatus? Status = null, AgreementInsightGroupKind Kind = AgreementInsightGroupKind.Fixed, Guid? NodeId = null);
// TotalUniqueAgreements counts the authorized unarchived dataset; ContextUniqueAgreements counts a selected subtree (or the dataset at top level).
public sealed record AgreementInsightsDto(IReadOnlyList<AgreementInsightGroup> Groups, IReadOnlyList<AgreementInsightNode> Options,
    int TotalUniqueAgreements, int ContextUniqueAgreements, Guid? SelectedNodeId = null, bool AllowMultiple = false)
{
    // Occurrences across displayed groups, not a unique agreement total. This is the percentage denominator.
    public int GroupSum => Groups.Sum(x => x.Count);
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

}
