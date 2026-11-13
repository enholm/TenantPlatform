using Microsoft.EntityFrameworkCore;
using TenantPlatform.Web.Services.Dimensions;

namespace TenantPlatform.Web.Services.Agreements;

internal sealed record AgreementReportCatalogSource(
    IReadOnlyList<AgreementOptionDto> Owners,
    DimensionListFilterCatalog Dimensions);

public partial class AgreementService
{
    internal async Task<AgreementReportCatalogSource> LoadReportCatalogSourceAsync(
        Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (userId, admin) = await RequireMemberAsync(db, accountId, ct);
        var visible = AgreementListQuery.Authorized(db, accountId, userId, admin).AsNoTracking();
        return new(
            await AgreementListQuery.LoadOwnersAsync(db, visible, ct),
            await AgreementListQuery.LoadDimensionCatalogAsync(db, accountId, visible, ct));
    }
}
