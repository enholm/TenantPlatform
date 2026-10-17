using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingAcquisitionData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingAcquisitionData
{
    public async Task<ILeasingAcquisitionWork> BeginAsync(Guid account, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            return new Work(db, tx, account, true);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    // Only composition: the caller owns the EF transaction; LoadAsync ensures the account lock.
    // Application still establishes fresh actor/resource authorization for every operation.
    public static ILeasingAcquisitionWork Join(TenantPlatformDbContext db, Guid account)
    {
        if (db.Database.CurrentTransaction is not { } tx) throw new InvalidOperationException("An account-locked transaction is required.");
        return new Work(db, tx, account, false);
    }
    private sealed class Work(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, bool owns) : ILeasingAcquisitionWork
    {
        private LeasingAcquisition? loaded;
        private List<LeasingItem> originalItems = [];
        private List<LeasingDimensionSelection> originalSelections = [];
        private List<LeasingAllocationRow> originalRows = [];
        private List<LeasingAllocationDimension> originalDimensions = [];
        public Guid AccountId => account;
        public async Task<AcquisitionFacts> LoadAsync(AcquisitionLoad r, CancellationToken ct)
        {
            if (!owns) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            loaded = r.Id.HasValue ? await db.LeasingAcquisitions.Where(x => x.AccountId == account && x.Id == r.Id).Include(x => x.Items).ThenInclude(x => x.DimensionSelections).Include(x => x.Items).ThenInclude(x => x.AllocationRows).Include(x => x.Items).ThenInclude(x => x.AllocationDimensions).AsSplitQuery().SingleOrDefaultAsync(ct) : null;
            originalItems = loaded?.Items.ToList() ?? []; originalSelections = originalItems.SelectMany(x => x.DimensionSelections).ToList(); originalRows = originalItems.SelectMany(x => x.AllocationRows).ToList(); originalDimensions = originalItems.SelectMany(x => x.AllocationDimensions).ToList();
            var old = loaded?.FrameworkId is Guid oldId ? await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == oldId, ct) : null;
            var target = r.Framework is Guid next ? await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == next, ct) : null;
            var finance = await db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == r.Finance, ct);
            var supplier = await db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == r.Supplier, ct);
            var owner = await db.UserAccounts.AnyAsync(x => x.AccountId == account && x.UserId == r.Owner && x.User.IsActive, ct);
            var revisions = r.Id.HasValue && await db.LeasingFinancingRevisions.AnyAsync(x => x.AccountId == account && x.AcquisitionId == r.Id, ct);
            var invoices = r.Id.HasValue ? await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == r.Id).ToListAsync(ct) : [];
            var invoiceIds = invoices.Select(x => x.Id).ToArray(); var itemIds = originalItems.Select(x => x.Id).ToArray();
            var lines = await db.LeasingInvoiceLines.AsNoTracking().Where(x => x.AccountId == account && (invoiceIds.Contains(x.InvoiceId) || itemIds.Contains(x.ItemId))).ToListAsync(ct);
            var links = await db.LeasingOrderRealizations.Where(x => x.AccountId == account && (r.Id.HasValue && x.AcquisitionId == r.Id || r.Request.HasValue && x.RequestId == r.Request)).ToListAsync(ct);
            var orderIds = links.Select(x => x.OrderId).Append(r.Order ?? Guid.Empty).ToArray();
            var orders = await db.LeasingOrders.Where(x => x.AccountId == account && orderIds.Contains(x.Id)).Include(x => x.Lines).ToListAsync(ct);
            var previous = r.Request.HasValue ? await db.LeasingOrderEvents.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == r.Request, ct) : null;
            var approval = r.Invoice.HasValue ? await db.LeasingInvoices.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == r.Invoice, ct) : null;
            var units = await db.LeasingEquipment.AsNoTracking().Where(x => x.AccountId == account && itemIds.Contains(x.ItemId)).GroupBy(x => x.ItemId).Select(x => new { Id = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count, ct);
            var quantities = await db.LeasingDispositions.AsNoTracking().Where(x => x.AccountId == account && itemIds.Contains(x.ItemId) && x.EquipmentId == null && !x.Reversed).GroupBy(x => x.ItemId).Select(x => new { Id = x.Key, Quantity = x.Sum(y => y.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Quantity, ct);
            return new(loaded, old, target, finance, supplier, owner, revisions, invoices, lines, links, orders, previous, approval, units, quantities, await LeasingClassificationData.CatalogAsync(db, account, false, ct));
        }
        public async Task SaveAcquisitionAsync(LeasingAcquisition a, LeasingHistory history, CancellationToken ct)
        {
            if (a.AccountId != account || loaded is not null && a.Id != loaded.Id || history.AccountId != account || history.AcquisitionId != a.Id || history.FrameworkId is not null) throw new UnauthorizedAccessException();
            if (a.Items.Any(x => x.AccountId != account || x.AcquisitionId != a.Id)) throw new UnauthorizedAccessException();
            var items = a.Items; var selections = items.SelectMany(x => x.DimensionSelections).ToList(); var rows = items.SelectMany(x => x.AllocationRows).ToList(); var dimensions = items.SelectMany(x => x.AllocationDimensions).ToList();
            db.LeasingDimensionSelections.RemoveRange(originalSelections.Where(x => selections.All(y => y.Id != x.Id)));
            db.LeasingAllocationRows.RemoveRange(originalRows.Where(x => rows.All(y => y.Id != x.Id)));
            db.LeasingAllocationDimensions.RemoveRange(originalDimensions.Where(x => dimensions.All(y => y.ItemId != x.ItemId || y.DimensionId != x.DimensionId)));
            db.LeasingItems.RemoveRange(originalItems.Where(x => items.All(y => y.Id != x.Id)));
            if (loaded is null) db.LeasingAcquisitions.Add(a);
            foreach (var item in items) if (originalItems.All(x => x.Id != item.Id)) db.Entry(item).State = EntityState.Added;
            foreach (var value in selections) if (originalSelections.All(x => x.Id != value.Id)) db.Entry(value).State = EntityState.Added;
            foreach (var value in rows) if (originalRows.All(x => x.Id != value.Id)) db.Entry(value).State = EntityState.Added;
            foreach (var value in dimensions) if (originalDimensions.All(x => x.ItemId != value.ItemId || x.DimensionId != value.DimensionId)) db.Entry(value).State = EntityState.Added;
            db.LeasingHistory.Add(history); await db.SaveChangesAsync(ct);
        }
        public async Task SaveFinancingAsync(LeasingFinancingRevision? revision, CancellationToken ct)
        {
            if (revision is not null) { if (revision.AccountId != account) throw new UnauthorizedAccessException(); db.LeasingFinancingRevisions.Add(revision); }
            await db.SaveChangesAsync(ct);
        }
        public async Task SaveOrdersAsync(IReadOnlyList<LeasingOrderEvent> events, IReadOnlyList<LeasingOrderRealization> added, CancellationToken ct)
        {
            if (events.Any(x => x.AccountId != account) || added.Any(x => x.AccountId != account)) throw new UnauthorizedAccessException();
            db.LeasingOrderEvents.AddRange(events); db.LeasingOrderRealizations.AddRange(added); await db.SaveChangesAsync(ct);
        }
        public async Task<LeasingCapacity> ReadCapacityAsync(Guid framework, CancellationToken ct)
        {
            var f = await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == framework, ct) ?? throw new UnauthorizedAccessException();
            var used = await db.LeasingAcquisitions.Where(x => x.AccountId == account && x.FrameworkId == framework && x.Status == LeasingAcquisitionStatus.Registered).SumAsync(x => f.IncludesVat ? x.GrossTotal - x.ReleasedNetTotal - x.ReleasedVatTotal : x.NetTotal - x.ReleasedNetTotal, ct);
            var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == framework && x.Status == LeasingOrderStatus.Approved).Include(x => x.Lines).ToListAsync(ct);
            return new(f.Limit, used, orders.Sum(x => LeasingOrderCalculator.Reserved(x, f.IncludesVat)));
        }
        public Task CommitAsync(CancellationToken ct) => owns ? tx.CommitAsync(ct) : throw new InvalidOperationException("The enclosing use case owns commit.");
        public async ValueTask DisposeAsync() { if (owns) { await tx.DisposeAsync(); await db.DisposeAsync(); } }
    }
}
