using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingOrderWriteData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingOrderWriteData
{
    public async Task<ILeasingOrderWork> BeginAsync(Guid account, Guid? order, Guid? framework, Guid? change, Guid? request, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var o = order.HasValue ? await db.LeasingOrders.Where(x => x.AccountId == account && x.Id == order).Include(x => x.Lines).SingleOrDefaultAsync(ct) : null;
            var c = change.HasValue ? await db.LeasingLimitChanges.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == change, ct) : null;
            var frameId = framework ?? o?.FrameworkId ?? c?.FrameworkId;
            var f = frameId.HasValue ? await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == frameId, ct) : null;
            var originalFrame = o is not null ? await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == o.FrameworkId, ct) : null;
            var e = request.HasValue ? await db.LeasingOrderEvents.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == request, ct) : null;
            var count = await db.LeasingOrders.CountAsync(x => x.AccountId == account, ct);
            var zone = await db.AgreementReminderSettings.Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Oslo";
            return new Work(db, tx, account, new(o, f, c, e, count, zone, await LeasingClassificationData.CatalogAsync(db, account, false, ct), originalFrame));
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Work : ILeasingOrderWork
    {
        private readonly TenantPlatformDbContext db; private readonly IDbContextTransaction tx; private readonly Guid account; private readonly List<LeasingOrderLine> original;
        public LeasingOrderWriteState State { get; }
        public Work(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, LeasingOrderWriteState state) { this.db = db; this.tx = tx; this.account = account; State = state; original = state.Order?.Lines.ToList() ?? []; }
        public async Task<(bool Supplier, bool Owner)> ReferencesAsync(Guid supplier, Guid owner, CancellationToken ct) => (await db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == supplier, ct), await db.UserAccounts.AnyAsync(x => x.AccountId == account && x.UserId == owner && x.User.IsActive, ct));
        public async Task SaveOrderAsync(LeasingOrder order, LeasingOrderEvent history, CancellationToken ct)
        {
            if (order.AccountId != account || order.FrameworkId != State.Framework?.Id || State.Order is not null && order.Id != State.Order.Id || history.AccountId != account || history.OrderId != order.Id || order.Lines.Any(x => x.AccountId != account || x.OrderId != order.Id)) throw new UnauthorizedAccessException();
            db.LeasingOrderLines.RemoveRange(original.Where(x => order.Lines.All(y => y.Id != x.Id)));
            if (State.Order is null) db.LeasingOrders.Add(order);
            foreach (var line in order.Lines.Where(x => original.All(y => y.Id != x.Id))) db.Entry(line).State = EntityState.Added;
            db.LeasingOrderEvents.Add(history); await db.SaveChangesAsync(ct);
        }
        public async Task SaveLimitAsync(LeasingLimitChange change, LeasingHistory? history, CancellationToken ct)
        {
            if (change.AccountId != account || change.FrameworkId != State.Framework?.Id || State.Change is not null && change.Id != State.Change.Id) throw new UnauthorizedAccessException();
            if (State.Change is null) db.LeasingLimitChanges.Add(change);
            if (history is not null) { if (history.AccountId != account || history.FrameworkId != change.FrameworkId || history.AcquisitionId is not null) throw new UnauthorizedAccessException(); db.LeasingHistory.Add(history); }
            await db.SaveChangesAsync(ct);
        }
        public async Task<LeasingCapacity> ReadCapacityAsync(CancellationToken ct)
        {
            var f = State.Framework ?? throw new UnauthorizedAccessException();
            var used = await db.LeasingAcquisitions.Where(x => x.AccountId == account && x.FrameworkId == f.Id && x.Status == LeasingAcquisitionStatus.Registered).SumAsync(x => f.IncludesVat ? x.GrossTotal - x.ReleasedNetTotal - x.ReleasedVatTotal : x.NetTotal - x.ReleasedNetTotal, ct);
            var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == f.Id && x.Status == LeasingOrderStatus.Approved).Include(x => x.Lines).ToListAsync(ct);
            return new(f.Limit, used, orders.Sum(x => LeasingOrderCalculator.Reserved(x, f.IncludesVat)));
        }
        public Task CommitAsync(CancellationToken ct) => tx.CommitAsync(ct);
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
