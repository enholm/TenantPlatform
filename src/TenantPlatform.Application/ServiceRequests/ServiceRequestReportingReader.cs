using TenantPlatform.Application.Security;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Application.ServiceRequests;

public static class ServiceRequestReportScopes { public const string Read = "tenantplatform.service_requests.read"; }
public enum ServiceRequestReportGrouping { Building, Status }
public sealed record ServiceRequestReportFilter(string? Search = null, ServiceRequestStatus? Status = null,
    Guid? BuildingId = null, bool MissingBuilding = false);
public sealed record ServiceRequestReportReference(Guid Id, string Name);
public sealed record ServiceRequestReportRow(Guid Id, string? Title, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt, DateTimeOffset? CompletedAt, ServiceRequestReportReference? Building, ServiceRequestReportReference? Unit);
public sealed record ServiceRequestReportGroup(string Key, string? Name, int Count);
public sealed record ServiceRequestReportPage(int RequestedPage, int Page, int TotalCount, ServiceRequestReportRow[] Items);
public sealed record ServiceRequestReportCounts(int TotalCount, ServiceRequestReportGroup[] Groups);
public sealed class ServiceRequestReportLimitException : Exception;
public sealed record ServiceRequestQueryResult(string SchemaVersion, Guid AccountId, ServiceRequestReportFilter Filter,
    int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount, bool HasMore, int? NextPage,
    bool Complete, bool SnapshotConsistent, bool LimitReached, ServiceRequestReportRow[] Items);
public sealed record ServiceRequestAggregateResult(string SchemaVersion, Guid AccountId, ServiceRequestReportFilter Filter,
    string GroupBy, int TotalCount, ServiceRequestReportGroup[] Groups, bool Complete, bool SnapshotConsistent,
    bool CountsUniqueRequests, bool GroupsOverlap, string MissingKey);

public interface IServiceRequestReportingData
{
    Task<ServiceRequestReportPage> ListAsync(ServiceRequestReadScope scope, ServiceRequestReportFilter filter, int page, CancellationToken ct);
    Task<ServiceRequestReportCounts> CountAsync(ServiceRequestReadScope scope, ServiceRequestReportFilter filter, ServiceRequestReportGrouping grouping, int maxGroups, CancellationToken ct);
}

// Uses exactly the existing request-detail visibility: requester OR administration OR tenant OR assigned provider.
// No client identity/scope, initialization, mail, workflow transition or implicit status calculation.
public sealed class ServiceRequestReportingReader(IAuthorizationService authorization, IServiceRequestReportingData data)
{
    public static ServiceRequestReportFilter Normalize(ServiceRequestReportFilter filter)
    {
        if (filter.Status is { } status && !Enum.IsDefined(status) || filter.BuildingId == Guid.Empty ||
            filter.MissingBuilding && filter.BuildingId.HasValue || filter.Search is { } search &&
            (search.EnumerateRunes().Count() > 200 || search.Any(char.IsControl))) throw new ArgumentException("Invalid request filter.");
        return filter with { Search = filter.Search?.Trim() };
    }
    private async Task<ServiceRequestReadScope> Scope(Guid account, CancellationToken ct) =>
        ServiceRequestAccess.ReadScope(await authorization.GetAccountActorScopeAsync(account, ct));
    public async Task<ServiceRequestQueryResult> ListAsync(Guid account, ServiceRequestReportFilter filter, int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000) throw new ArgumentException("Invalid page.");
        filter = Normalize(filter);
        var rows = await data.ListAsync(await Scope(account, ct), filter, page, ct);
        var more = (long)rows.Page * 25 < rows.TotalCount;
        return new("1", account, filter, page, rows.Page, 25, rows.TotalCount, rows.Items.Length, more,
            more && rows.Page < 10000 ? rows.Page + 1 : null, rows.Items.Length == rows.TotalCount, false, more && rows.Page == 10000, rows.Items);
    }
    public async Task<ServiceRequestAggregateResult> CountAsync(Guid account, ServiceRequestReportFilter filter,
        ServiceRequestReportGrouping grouping, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(grouping)) throw new ArgumentException("Invalid grouping.");
        filter = Normalize(filter);
        var counts = await data.CountAsync(await Scope(account, ct), filter, grouping, 100, ct);
        return new("1", account, filter, grouping.ToString(), counts.TotalCount, counts.Groups, true, false, true, false, "missing");
    }
}
