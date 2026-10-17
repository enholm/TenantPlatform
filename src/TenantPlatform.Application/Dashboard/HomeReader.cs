using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Dashboard;

public sealed record RecentServiceRequest(DateTimeOffset CreatedAt, string Status, string RequesterName);
public sealed record HomeOverview(string? AccountName, int BuildingCount, int OrganizationCount, int UnitCount,
    int OpenRequestCount, IReadOnlyList<RecentServiceRequest> RecentRequests);
public interface IHomeData
{
    Task<HomeOverview> ReadAsync(HomeReadScope scope, CancellationToken ct);
}
public sealed class HomeReader(IAuthorizationService authorization, IHomeData data)
{
    public async Task<HomeOverview> ReadAsync(Guid accountId, CancellationToken ct = default) =>
        await data.ReadAsync(await authorization.GetHomeReadScopeAsync(accountId, ct), ct);
}
