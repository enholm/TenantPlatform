using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Leasing;

internal sealed record LeasingUsedCapacity(Guid FrameworkId, decimal Net, decimal Vat)
{
    internal decimal Gross => Net + Vat;
}

internal static class LeasingCapacityQuery
{
    internal static async Task<IReadOnlyDictionary<Guid, LeasingUsedCapacity>> UsedByFrameworkAsync(
        TenantPlatformDbContext db, Guid accountId, IReadOnlyCollection<Guid> frameworkIds,
        CancellationToken ct)
    {
        if (frameworkIds.Count == 0) return new Dictionary<Guid, LeasingUsedCapacity>();
        return await db.LeasingAcquisitions.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.FrameworkId.HasValue &&
                frameworkIds.Contains(x.FrameworkId.Value) &&
                x.Status == LeasingAcquisitionStatus.Registered)
            .GroupBy(x => x.FrameworkId!.Value)
            .Select(x => new LeasingUsedCapacity(x.Key,
                x.Sum(y => y.NetTotal - y.ReleasedNetTotal),
                x.Sum(y => y.VatTotal - y.ReleasedVatTotal)))
            .ToDictionaryAsync(x => x.FrameworkId, ct);
    }
}
