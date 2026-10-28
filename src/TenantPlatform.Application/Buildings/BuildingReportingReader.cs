using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Buildings;

public static class StructureReadScopes { public const string Read = "tenantplatform.structure.read"; }
public sealed record BuildingReportFilter(string? Search = null);
public sealed record BuildingReportRow(Guid Id, string Name, string? AddressLine1, string? PostalCode, string? City);
public sealed record BuildingReportGroup(string Key, string? Name, int Count);
public sealed record BuildingReportPage(int Page, int TotalCount, BuildingReportRow[] Items);
public sealed record BuildingReportCounts(int TotalCount, BuildingReportGroup[] Groups);
public sealed class BuildingReportLimitException : Exception;
// Created by Application only after Buildings.Open; the port performs no role decisions.
public sealed record BuildingReportScope(Guid AccountId, Guid UserId);
public sealed record BuildingQueryResult(string SchemaVersion, Guid AccountId, BuildingReportFilter Filter,
    int RequestedPage, int Page, int PageSize, int TotalCount, int ReturnedCount, bool HasMore, int? NextPage,
    bool Complete, bool SnapshotConsistent, bool LimitReached, BuildingReportRow[] Items);
public sealed record BuildingAggregateResult(string SchemaVersion, Guid AccountId, BuildingReportFilter Filter,
    string GroupBy, int TotalCount, BuildingReportGroup[] Groups, bool Complete, bool SnapshotConsistent,
    bool CountsUniqueBuildings, bool GroupsOverlap, string MissingKey);
public interface IBuildingReportingData
{
    Task<BuildingReportPage> ListAsync(BuildingReportScope scope, BuildingReportFilter filter, int page, CancellationToken ct);
    Task<BuildingReportCounts> CountAsync(BuildingReportScope scope, BuildingReportFilter filter, bool city, int maxGroups, CancellationToken ct);
}

public sealed class BuildingReportingReader(IAuthorizationService authorization, IBuildingReportingData data)
{
    public static BuildingReportFilter Normalize(BuildingReportFilter filter)
    {
        if (filter.Search is { } s && (s.Any(char.IsControl) || s.Trim().EnumerateRunes().Count() > 200)) throw new ArgumentException();
        return filter with { Search = filter.Search?.Trim() };
    }
    private async Task<BuildingReportScope> Scope(Guid account, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        // Exactly BuildingService.GetBuildingsAsync semantics, including the legacy list exception.
        if (!await authorization.CanAsync(Permissions.Buildings.Open, new(ResourceKind.Account, AccountId: account), ct))
            throw new UnauthorizedAccessException();
        return new(account, actor.UserId);
    }
    private async Task Revalidate(BuildingReportScope before, CancellationToken ct)
    {
        if (await Scope(before.AccountId, ct) != before) throw new UnauthorizedAccessException();
    }
    public async Task<BuildingQueryResult> ListAsync(Guid account, BuildingReportFilter filter, int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 10000) throw new ArgumentException();
        filter = Normalize(filter);
        var scope = await Scope(account, ct);
        var rows = await data.ListAsync(scope, filter, page, ct);
        await Revalidate(scope, ct);
        var more = (long)rows.Page * 25 < rows.TotalCount;
        return new("1", account, filter, page, rows.Page, 25, rows.TotalCount, rows.Items.Length, more,
            more && rows.Page < 10000 ? rows.Page + 1 : null, rows.Items.Length == rows.TotalCount, false, more && rows.Page == 10000, rows.Items);
    }
    public async Task<BuildingAggregateResult> CountAsync(Guid account, BuildingReportFilter filter, bool city = false, CancellationToken ct = default)
    {
        filter = Normalize(filter);
        var scope = await Scope(account, ct);
        var counts = await data.CountAsync(scope, filter, city, 100, ct);
        await Revalidate(scope, ct);
        return new("1", account, filter, city ? "City" : "None", counts.TotalCount, counts.Groups, true, false, true, false, "missing");
    }
}
