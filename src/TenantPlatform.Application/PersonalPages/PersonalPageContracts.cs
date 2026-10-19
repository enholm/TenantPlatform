using TenantPlatform.Application.Agreements;
namespace TenantPlatform.Application.PersonalPages;

public enum PagePeriodKind { Fixed, NextMonths }
public enum PageComponent { Metric, BuildingChart, AgreementTable }
public sealed record PagePeriod(PagePeriodKind Kind, int? Months = 3, DateOnly? From = null, DateOnly? To = null);
public sealed record PageDefinition(int Version, string Title, Guid ConnectionId, string QueryVersion, string AggregateVersion,
    PagePeriod Period, AgreementReportFilter Filter, PageComponent[] Components, string Layout = "Stacked", string ProfileVersion = "");
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
