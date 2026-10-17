using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingDocumentData(IDbContextFactory<TenantPlatformDbContext> factory, IAgreementDocumentStorage storage, ILogger<LeasingDocumentData> logger) : ILeasingDocumentData
{
    public async Task<LeasingDocumentTarget> ReadTargetAsync(Guid account, Guid parent, LeasingDocumentParent kind, Guid actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Target(db, account, parent, kind, false, ct);
    }
    private static async Task<LeasingDocumentTarget> Target(TenantPlatformDbContext db, Guid account, Guid parent, LeasingDocumentParent kind, bool tracked, CancellationToken ct)
    {
        var frameworks = tracked ? db.LeasingFrameworks : db.LeasingFrameworks.AsNoTracking();
        var acquisitions = tracked ? db.LeasingAcquisitions : db.LeasingAcquisitions.AsNoTracking();
        var orders = tracked ? db.LeasingOrders : db.LeasingOrders.AsNoTracking();
        var f = kind == LeasingDocumentParent.Framework ? await frameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == parent, ct) : null;
        var a = kind == LeasingDocumentParent.Acquisition ? await acquisitions.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == parent, ct) : null;
        var o = kind == LeasingDocumentParent.Order ? await orders.Include(x => x.Lines).SingleOrDefaultAsync(x => x.AccountId == account && x.Id == parent, ct) : null;
        var frameId = a?.FrameworkId ?? o?.FrameworkId;
        var related = frameId.HasValue ? await frameworks.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == frameId, ct) : null;
        return new(new(f ?? (o is not null ? related : null), a, a is not null ? related : null, false, false), o);
    }
    public async Task<LeasingDocument?> ReadDocumentAsync(Guid account, Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LeasingDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct);
    }
    public async Task<ILeasingDocumentTransaction> BeginAsync(Guid account, Guid parent, LeasingDocumentParent kind, Guid actor, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            return new Transaction(db, tx, account, parent, kind, await Target(db, account, parent, kind, true, ct));
        }
        catch { await db.DisposeAsync(); throw; }
    }
    public async Task CleanupUncommittedAsync(Guid account, string key)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(CancellationToken.None);
            if (!await db.LeasingDocuments.AnyAsync(x => x.StorageKey == key)) await storage.DiscardUncommittedAsync(key);
        }
        catch (Exception ex) { logger.LogWarning("Could not clean up uncommitted leasing file: {ErrorType}", ex.GetType().Name); }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, Guid parent, LeasingDocumentParent kind, LeasingDocumentTarget target) : ILeasingDocumentTransaction
    {
        public LeasingDocumentTarget Target => target;
        public async Task CommitAsync(LeasingDocument document, LeasingHistory? history, LeasingOrderEvent? orderHistory, CancellationToken ct)
        {
            if (document.AccountId != account || document.FrameworkId != (kind == LeasingDocumentParent.Framework ? parent : null) || document.AcquisitionId != (kind == LeasingDocumentParent.Acquisition ? parent : null) || document.OrderId != (kind == LeasingDocumentParent.Order ? parent : null)) throw new UnauthorizedAccessException();
            if (history is not null) { if (history.AccountId != account || history.FrameworkId != document.FrameworkId || history.AcquisitionId != document.AcquisitionId) throw new UnauthorizedAccessException(); db.LeasingHistory.Add(history); }
            if (orderHistory is not null) { if (orderHistory.AccountId != account || orderHistory.OrderId != document.OrderId) throw new UnauthorizedAccessException(); db.LeasingOrderEvents.Add(orderHistory); }
            db.LeasingDocuments.Add(document); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
