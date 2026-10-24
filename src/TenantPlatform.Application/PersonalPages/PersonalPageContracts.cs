using TenantPlatform.Application.Agreements;
namespace TenantPlatform.Application.PersonalPages;

public enum PagePeriodKind { Fixed, NextMonths, All }
public enum PageComponent { Metric, BuildingChart, AgreementTable }
public enum PageColumn { Title, Type, Status, Building, StartDate, EndDate, NoticeDeadline, RenewalDate }
public sealed record PagePeriod(PagePeriodKind Kind, int? Months = 3, DateOnly? From = null, DateOnly? To = null);
public sealed record PageDefinition(int Version, string Title, Guid ConnectionId, string QueryVersion, string AggregateVersion,
    PagePeriod Period, AgreementReportFilter Filter, PageComponent[] Components, string Layout = "Stacked", string ProfileVersion = "",
    AgreementReportGrouping GroupBy = AgreementReportGrouping.Building, PageColumn[]? Columns = null);
public sealed record PersonalPage(Guid Id, Guid AccountId, Guid OwnerUserId, PageDefinition Definition);
public sealed record PersonalPageCollection(Guid Revision, PersonalPage[] Pages);
public sealed record PageSource(Guid ConnectionId, string Name, string QueryVersion, string AggregateVersion, string ProfileVersion);
public interface IPersonalPageData
{
    Task<PersonalPageCollection> ReadAsync(Guid account, Guid user, CancellationToken ct);
    Task SaveAsync(Guid account, Guid user, Guid expectedRevision, PersonalPageCollection value, CancellationToken ct);
}
public sealed class PersonalPageException(string code) : Exception(code) { public string Code { get; } = code; }

public static class PersonalPageScopes { public const string Manage = "tenantplatform.pages.manage"; }

// v1 always meant all three components, Building grouping and Title/Building/EndDate.
// Conversion is in memory only. Stored originals remain available for revision/conflict checks.
public static class PageDefinitions
{
    public static PageColumn[] LegacyColumns => [PageColumn.Title, PageColumn.Building, PageColumn.EndDate];
    public static PageDefinition Current(PageDefinition definition)
    {
        PersonalPageService.Validate(definition);
        return definition.Version == 1 ? definition with { Version = 2, GroupBy = AgreementReportGrouping.Building, Columns = LegacyColumns } : definition;
    }
    public static bool NeedsQuery(PageDefinition definition) => definition.Components.Contains(PageComponent.AgreementTable);
    public static bool NeedsAggregate(PageDefinition definition) => definition.Components.Any(c => c is PageComponent.Metric or PageComponent.BuildingChart);
}
