using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingOrderReadData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingOrderReadData
{
    private static IQueryable<LeasingOrder> Visible(TenantPlatformDbContext db, LeasingUserScope scope) => db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == scope.AccountId && (scope.CanReadOrders || x.OwnerUserId == scope.UserId || db.LeasingFrameworks.Any(f => f.AccountId == scope.AccountId && f.Id == x.FrameworkId && f.OwnerUserId == scope.UserId)));
    public async Task<LeasingOrderTarget?> ReadTargetAsync(Guid account, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var order = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.Id == id).Include(x => x.Lines).SingleOrDefaultAsync(ct);
        if (order is null) return null;
        var frame = await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == order.FrameworkId, ct);
        return frame is null ? null : new(order, frame);
    }
    public async Task<List<LeasingOrder>> QueryAsync(LeasingUserScope scope, Guid? framework, Guid? acquisition, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Visible(db, scope).Include(x => x.Lines).Where(x => !framework.HasValue || x.FrameworkId == framework);
        if (acquisition.HasValue) query = query.Where(x => db.LeasingOrderRealizations.Any(r => r.AccountId == scope.AccountId && r.AcquisitionId == acquisition && r.OrderId == x.Id));
        return await query.OrderByDescending(x => x.OrderDate).ThenBy(x => x.Number).ToListAsync(ct);
    }
    public async Task<OrderDetails> ReadDetailsAsync(Guid account, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var o = await db.LeasingOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        var f = await db.LeasingFrameworks.AsNoTracking().SingleAsync(x => x.AccountId == account && x.Id == o.FrameworkId, ct);
        var links = await db.LeasingOrderRealizations.AsNoTracking().Where(x => x.AccountId == account && x.OrderId == id).ToListAsync(ct);
        var aids = links.Select(x => x.AcquisitionId).ToArray(); var itemIds = links.Select(x => x.ItemId).ToArray();
        var documented = await (from line in db.LeasingInvoiceLines.AsNoTracking() join invoice in db.LeasingInvoices.AsNoTracking() on new { line.AccountId, Id = line.InvoiceId } equals new { invoice.AccountId, invoice.Id } where line.AccountId == account && itemIds.Contains(line.ItemId) && invoice.Status == LeasingInvoiceStatus.Approved select invoice.Kind == LeasingInvoiceKind.CreditNote ? -line.Net : line.Net).SumAsync(ct);
        var used = await db.LeasingAcquisitions.Where(x => x.AccountId == account && x.FrameworkId == f.Id && x.Status == LeasingAcquisitionStatus.Registered).SumAsync(x => f.IncludesVat ? x.GrossTotal - x.ReleasedNetTotal - x.ReleasedVatTotal : x.NetTotal - x.ReleasedNetTotal, ct);
        var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == f.Id && x.Status == LeasingOrderStatus.Approved).Include(x => x.Lines).ToListAsync(ct);
        var capacity = new LeasingCapacity(f.Limit, used, orders.Sum(x => LeasingOrderCalculator.Reserved(x, f.IncludesVat)));
        return new(o, f, capacity, links, await db.LeasingOrderEvents.AsNoTracking().Where(x => x.AccountId == account && x.OrderId == id).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct), await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && aids.Contains(x.AcquisitionId ?? Guid.Empty)).ToListAsync(ct), await db.LeasingDocuments.AsNoTracking().Where(x => x.AccountId == account && x.OrderId == id).ToListAsync(ct), false, false, documented, false);
    }
    public async Task<List<LeasingAcquisition>> ReadCandidatesAsync(LeasingUserScope scope, LeasingOrder order, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var account = scope.AccountId; var user = scope.UserId;
        return await db.LeasingAcquisitions.AsNoTracking().Where(x => x.AccountId == account && (scope.IsAdministrator || x.OwnerUserId == user || db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user)) && x.FrameworkId == order.FrameworkId && x.Currency == order.Currency && x.SupplierOrganizationId == order.SupplierOrganizationId && x.Status == LeasingAcquisitionStatus.Registered && !db.LeasingOrderRealizations.Any(r => r.AccountId == account && r.AcquisitionId == x.Id && r.OrderId != order.Id)).OrderByDescending(x => x.PurchaseDate).ToListAsync(ct);
    }
    public async Task<List<LeasingLimitChange>> ReadLimitChangesAsync(Guid account, Guid framework, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeasingLimitChanges.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == framework).OrderByDescending(x => x.ProposedUtc).ToListAsync(ct);
    }
}
