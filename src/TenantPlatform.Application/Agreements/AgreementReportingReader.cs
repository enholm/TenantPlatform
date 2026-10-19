using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public enum AgreementArchiveSelection { Include, Exclude, Only }
public enum AgreementReportGrouping { Building, Status }
public sealed record AgreementReportFilter(string? Search = null, AgreementStatus? Status = null, AgreementType? Type = null,
    AgreementArchiveSelection Archive = AgreementArchiveSelection.Include, Guid? BuildingId = null, Guid? OrganizationElementId = null,
    Guid? GeographicAreaId = null, Guid? UnitId = null, bool MissingBuilding = false,
    DateOnly? EndDateFrom = null, DateOnly? EndDateTo = null, int? EndsWithinMonths = null);
public sealed record AgreementReportSelection(AgreementReportFilter Filter, DateOnly Today);
public sealed record AgreementReference(Guid Id, string Name);
public sealed record AgreementReportRow(Guid Id, string Title, AgreementReference? Counterparty, string Type, string Status,
    DateOnly StartDate, DateOnly? EndDate, DateOnly? NoticeDeadline, DateOnly? RenewalDate, bool IsArchived,
    AgreementReference? Building, AgreementReference? Unit, AgreementReference? OrganizationElement, AgreementReference? GeographicArea);
public sealed record AgreementReportPage(int RequestedPage, int Page, int PageSize, int TotalCount, AgreementReportRow[] Items);
public sealed record AgreementReportGroup(string Key, string? Name, int Count);
public sealed record AgreementReportCounts(int TotalCount, AgreementReportGroup[] Groups);
public sealed class AgreementReportLimitException : Exception;

public interface IAgreementReportingData
{
    Task<string> TimeZoneAsync(Guid accountId, CancellationToken ct);
    Task<AgreementReportPage> ListAsync(AgreementListScope scope, AgreementReportSelection selection, int page, CancellationToken ct);
    Task<AgreementReportCounts> CountAsync(AgreementListScope scope, AgreementReportSelection selection, AgreementReportGrouping grouping, int maxGroups, CancellationToken ct);
    Task<AgreementReportRow> DetailsAsync(AgreementReadScope scope, CancellationToken ct);
}

// Immutable input; all business validation and account-date semantics belong to Application.
public sealed class AgreementReportingReader(IAuthorizationService authorization, IAgreementReportingData data, TimeProvider clock)
{
    public const int MaxGroups = 100;
    public async Task<(AgreementReportSelection Selection, AgreementReportPage Data)> ListAsync(Guid accountId, AgreementReportFilter filter, int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000) throw new ArgumentException("Invalid page.");
        var (scope, selection) = await Select(accountId, filter, ct);
        return (selection, await data.ListAsync(scope, selection, page, ct));
    }
    public async Task<(AgreementReportSelection Selection, AgreementReportCounts Data)> CountAsync(Guid accountId, AgreementReportFilter filter, AgreementReportGrouping grouping, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(grouping)) throw new ArgumentException("Invalid grouping.");
        var (scope, selection) = await Select(accountId, filter, ct);
        return (selection, await data.CountAsync(scope, selection, grouping, MaxGroups, ct));
    }
    private async Task<(AgreementListScope, AgreementReportSelection)> Select(Guid account, AgreementReportFilter filter, CancellationToken ct)
    {
        var instant = clock.GetUtcNow();
        AgreementReportFilterRules.Validate(filter);
        var scope = await authorization.GetAgreementListScopeAsync(account, ct);
        var today = AgreementReminderSchedule.Today(instant, await data.TimeZoneAsync(account, ct));
        var normalized = filter with { Search = filter.Search?.Trim(), EndDateFrom = filter.EndsWithinMonths.HasValue ? today : filter.EndDateFrom,
            EndDateTo = filter.EndsWithinMonths is { } months ? today.AddMonths(months) : filter.EndDateTo, EndsWithinMonths = null };
        return (scope, new(normalized, today));
    }
}

public static class AgreementReportFilterRules
{
    public static void Validate(AgreementReportFilter filter)
    {
        if (!Enum.IsDefined(filter.Archive) || filter.Status is { } status && !Enum.IsDefined(status) || filter.Type is { } type && !Enum.IsDefined(type) ||
            filter.Search is { } text && (text.EnumerateRunes().Count() > 200 || text.Any(char.IsControl)) ||
            new[] { filter.BuildingId, filter.OrganizationElementId, filter.GeographicAreaId, filter.UnitId }.Any(x => x == Guid.Empty) ||
            filter.MissingBuilding && filter.BuildingId.HasValue || filter.EndDateFrom > filter.EndDateTo ||
            filter.EndsWithinMonths is < 1 or > 12 || filter.EndsWithinMonths.HasValue && (filter.EndDateFrom.HasValue || filter.EndDateTo.HasValue))
            throw new ArgumentException("Invalid agreement filter.");
    }
}
