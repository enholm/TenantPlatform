using Microsoft.EntityFrameworkCore;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Leasing;

internal sealed record LeasingReportCatalogSource(
    IReadOnlyList<LeasingOption> Owners,
    DimensionListFilterCatalog Dimensions);

public sealed partial class LeasingService
{
    internal async Task<LeasingReportCatalogSource> LoadReportCatalogSourceAsync(
        Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await Member(db, accountId, ct);
        var frameworkReader = admin || await authorization.CanChangeLeasingLimitAsync(ct);
        var scopes = new LeasingListScopes(
            LeasingListQuery.AuthorizedFrameworks(db, accountId, userId, frameworkReader).AsNoTracking(),
            LeasingListQuery.AuthorizedAcquisitions(db, accountId, userId, admin).AsNoTracking());
        return new(
            await LeasingListQuery.LoadOwnersAsync(db, scopes, ct),
            await LeasingListQuery.LoadDimensionCatalogAsync(db, accountId, scopes, ct));
    }
}
