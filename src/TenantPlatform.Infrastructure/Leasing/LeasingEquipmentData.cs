using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingEquipmentData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingEquipmentData
{
    public async Task<List<LeasingOption>> ReadBuildingsAsync(Guid account, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Buildings.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.Name).Select(x => new LeasingOption(x.Id, x.Name)).ToListAsync(ct);
    }
    public async Task<EquipmentPage> ReadPageAsync(LeasingUserScope scope, string? search, LeasingEquipmentStatus? status, Guid? acquisition, int page, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var account = scope.AccountId; var user = scope.UserId;
        var visible = db.LeasingAcquisitions.Where(x => x.AccountId == account && (scope.CanReadLifecycle || x.OwnerUserId == user || db.LeasingFrameworks.Any(f => f.AccountId == account && f.Id == x.FrameworkId && f.OwnerUserId == user))).Select(x => x.Id);
        var query = db.LeasingEquipment.AsNoTracking().Where(x => x.AccountId == account && visible.Contains(x.AcquisitionId));
        if (acquisition.HasValue) query = query.Where(x => x.AcquisitionId == acquisition);
        if (status.HasValue) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(search)) { var q = search.ToLower(); query = query.Where(x => x.Description.ToLower().Contains(q) || (x.SerialNumber ?? "").ToLower().Contains(q) || (x.InternalId ?? "").ToLower().Contains(q)); }
        var count = await query.CountAsync(ct); page = Math.Clamp(page, 1, Math.Max(1, (count + 49) / 50));
        var rows = await query.OrderBy(x => x.Description).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50).ToListAsync(ct); var ids = rows.Select(x => x.AcquisitionId).ToArray();
        return new(rows, count, await db.LeasingAcquisitions.Where(x => x.AccountId == account && ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct), await db.Buildings.Where(x => x.AccountId == account).ToDictionaryAsync(x => x.Id, x => x.Name, ct), page);
    }
    public async Task<ILeasingEquipmentWork> BeginAsync(Guid account, Guid acquisition, Guid? request, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            var a = await db.LeasingAcquisitions.Include(x => x.Items).SingleOrDefaultAsync(x => x.AccountId == account && x.Id == acquisition, ct);
            var f = a?.FrameworkId is Guid frame ? await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == frame, ct) : null;
            var lifecycle = await db.LeasingLifecycles.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.AcquisitionId == acquisition, ct);
            var equipment = await db.LeasingEquipment.Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct);
            var dispositions = await db.LeasingDispositions.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct);
            var identities = await db.LeasingEquipment.AsNoTracking().Where(x => x.AccountId == account).Select(x => new LeasingEquipmentIdentity(x.Id, x.ItemId, x.InternalId, x.SerialNumber)).ToListAsync(ct);
            var buildings = await db.Buildings.AsNoTracking().Where(x => x.AccountId == account).Select(x => x.Id).ToListAsync(ct);
            var documents = await db.LeasingDocuments.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).Select(x => x.Id).ToListAsync(ct);
            var previous = request.HasValue ? await db.LeasingPaymentEvents.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == request, ct) : null;
            return new Work(db, tx, account, acquisition, new(a, f, lifecycle, equipment, dispositions, identities, buildings, documents, previous));
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Work : ILeasingEquipmentWork
    {
        private readonly TenantPlatformDbContext db; private readonly IDbContextTransaction tx; private readonly Guid account; private readonly Guid acquisition; private readonly HashSet<Guid> originals;
        public LeasingEquipmentState State { get; }
        public Work(TenantPlatformDbContext db, IDbContextTransaction tx, Guid account, Guid acquisition, LeasingEquipmentState state) { this.db = db; this.tx = tx; this.account = account; this.acquisition = acquisition; State = state; originals = state.Equipment.Select(x => x.Id).ToHashSet(); }
        public Task<LeasingRecipientScope> ReadRecipientAsync(Guid user, CancellationToken ct) => LeasingLifecycleRecipientData.ReadAsync(db, account, user, acquisition, null, ct);
        public async Task CommitAsync(LeasingPaymentEvent history, CancellationToken ct)
        {
            if (history.AccountId != account || history.AcquisitionId != acquisition || State.Equipment.Any(x => x.AccountId != account || x.AcquisitionId != acquisition)) throw new UnauthorizedAccessException();
            foreach (var unit in State.Equipment.Where(x => !originals.Contains(x.Id))) db.Entry(unit).State = EntityState.Added;
            db.LeasingPaymentEvents.Add(history); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
