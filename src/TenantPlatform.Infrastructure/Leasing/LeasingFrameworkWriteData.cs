using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingFrameworkWriteData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingFrameworkWriteData
{
    public async Task<ILeasingFrameworkWriteTransaction> BeginAsync(Guid account, Guid framework, Guid finance, Guid owner, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var entity = await db.LeasingFrameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == framework, ct);
            var registered = await db.LeasingAcquisitions.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == framework && x.Status == LeasingAcquisitionStatus.Registered).ToListAsync(ct);
            var ids = registered.Select(x => x.Id).ToArray();
            var orders = await db.LeasingOrders.AsNoTracking().Where(x => x.AccountId == account && x.FrameworkId == framework).Include(x => x.Lines).ToListAsync(ct);
            var closed = await db.LeasingLifecycles.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.AcquisitionId) && x.Status == LeasingLifecycleStatus.Closed && x.DocumentReviewComplete && !x.PaymentPlanReviewRequired).Select(x => x.AcquisitionId).ToListAsync(ct);
            var pending = await db.LeasingLifecycleEvents.AnyAsync(x => x.AccountId == account && ids.Contains(x.AcquisitionId) && x.Decision == LeasingLifecycleDecision.Pending, ct);
            var open = await db.LeasingFollowups.AnyAsync(x => x.AccountId == account && (x.FrameworkId == framework || x.AcquisitionId.HasValue && ids.Contains(x.AcquisitionId.Value)) && (x.Status == LeasingFollowupStatus.Open || x.Status == LeasingFollowupStatus.InProgress), ct);
            var credit = await db.LeasingInvoices.AnyAsync(x => x.AccountId == account && x.Kind == LeasingInvoiceKind.CreditNote && x.Status == LeasingInvoiceStatus.Approved && db.LeasingAcquisitions.Any(a => a.AccountId == account && a.Id == x.AcquisitionId && a.FrameworkId == framework), ct);
            var financeExists = await db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == finance, ct);
            var ownerActive = await db.UserAccounts.AnyAsync(x => x.AccountId == account && x.UserId == owner && x.User.IsActive, ct);
            return new Transaction(db, tx, account, framework, new(entity, registered, orders, closed, pending, open, credit, financeExists, ownerActive));
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, Guid key, LeasingFrameworkWriteState state) : ILeasingFrameworkWriteTransaction
    {
        public LeasingFrameworkWriteState State => state;
        public async Task CommitAsync(LeasingFramework framework, LeasingHistory history, CancellationToken ct)
        {
            if (framework.AccountId != account || framework.Id != key || history.AccountId != account || history.FrameworkId != key || history.AcquisitionId is not null) throw new UnauthorizedAccessException();
            if (state.Framework is null) db.LeasingFrameworks.Add(framework);
            db.LeasingHistory.Add(history); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
