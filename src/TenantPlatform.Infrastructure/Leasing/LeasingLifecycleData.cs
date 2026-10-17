using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingLifecycleData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingLifecycleData
{
    public async Task<ILeasingLifecycleWork> OpenAsync(Guid account, Guid target, bool eventTarget, Guid? request, bool write, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            IDbContextTransaction? tx = null;
            if (write)
            {
                tx = await db.Database.BeginTransactionAsync(ct);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
            }
            else db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            var acquisition = eventTarget ? await db.LeasingLifecycleEvents.Where(x => x.AccountId == account && x.Id == target).Select(x => (Guid?)x.AcquisitionId).SingleOrDefaultAsync(ct) : target;
            var a = await db.LeasingAcquisitions.Include(x => x.Items).SingleOrDefaultAsync(x => x.AccountId == account && x.Id == acquisition, ct);
            var f = a?.FrameworkId is Guid frame ? await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == frame, ct) : null;
            var installments = await db.LeasingInstallments.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct);
            var ids = installments.Select(x => x.Id).ToArray();
            var allocations = await db.LeasingPaymentAllocations.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.InstallmentId) && !x.Reversed).ToListAsync(ct);
            var invoiceIds = allocations.Select(x => x.InvoiceId).Distinct().ToArray();
            var state = new LeasingLifecycleState
            {
                Acquisition = a, Framework = f,
                Lifecycle = await db.LeasingLifecycles.SingleOrDefaultAsync(x => x.AccountId == account && x.AcquisitionId == acquisition, ct),
                Equipment = await db.LeasingEquipment.Where(x => x.AccountId == account && x.AcquisitionId == acquisition).OrderBy(x => x.InternalId).ToListAsync(ct),
                Events = await db.LeasingLifecycleEvents.Where(x => x.AccountId == account && x.AcquisitionId == acquisition).OrderByDescending(x => x.RecordedUtc).ThenByDescending(x => x.Id).ToListAsync(ct),
                Dispositions = await db.LeasingDispositions.Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct),
                Documents = await db.LeasingDocuments.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct),
                Changes = await db.LeasingPaymentEvents.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition && x.Action.StartsWith("Lifecycle")).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct),
                Followups = await db.LeasingFollowups.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct),
                Plans = await db.LeasingPaymentPlans.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).Include(x => x.Terms).ToListAsync(ct),
                Installments = installments, Allocations = allocations,
                ApprovedInvoices = await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && invoiceIds.Contains(x.Id) && x.Status == LeasingInvoiceStatus.Approved).ToListAsync(ct),
                Financing = await db.LeasingFinancingRevisions.AsNoTracking().Where(x => x.AccountId == account && x.AcquisitionId == acquisition).ToListAsync(ct),
                PreviousRequest = request.HasValue ? await db.LeasingPaymentEvents.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == request, ct) : null,
                TimeZoneId = await db.AgreementReminderSettings.AsNoTracking().Where(x => x.AccountId == account).Select(x => x.TimeZoneId).SingleOrDefaultAsync(ct) ?? "Europe/Oslo"
            };
            return new Work(db, tx, account, acquisition, state);
        }
        catch { await db.DisposeAsync(); throw; }
    }
    private sealed class Work(TenantPlatformDbContext db, IDbContextTransaction? tx, Guid account, Guid? acquisition, LeasingLifecycleState state) : ILeasingLifecycleWork
    {
        private readonly HashSet<Guid> events = state.Events.Select(x => x.Id).ToHashSet();
        private readonly HashSet<Guid> dispositions = state.Dispositions.Select(x => x.Id).ToHashSet();
        private readonly bool hadLifecycle = state.Lifecycle is not null;
        public LeasingLifecycleState State => state;
        public Task<LeasingRecipientScope> ReadRecipientAsync(Guid user, CancellationToken ct) => LeasingLifecycleRecipientData.ReadAsync(db, account, user, acquisition, null, ct);
        public Task<bool> OrganizationExistsAsync(Guid organization, CancellationToken ct) => db.Organizations.AnyAsync(x => x.AccountId == account && x.Id == organization, ct);
        public async Task<LeasingLifecycleReplacement> ReadReplacementAsync(Guid equipment, CancellationToken ct)
        {
            var unit = await db.LeasingEquipment.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == equipment, ct);
            var parent = unit is null ? null : await db.LeasingAcquisitions.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == unit.AcquisitionId, ct);
            return new(unit, parent, await db.LeasingEquipment.AnyAsync(x => x.AccountId == account && x.ReplacementEquipmentId == equipment, ct), await db.LeasingLifecycleEvents.AsNoTracking().Where(x => x.AccountId == account && x.Kind == LeasingLifecycleKind.Replace && x.Decision == LeasingLifecycleDecision.Approved).Select(x => x.InputJson).ToListAsync(ct));
        }
        public async Task<LeasingLifecyclePartyInvoices> ReadPartyInvoicesAsync(Guid[] financeOrganizations, string currency, CancellationToken ct)
        {
            var rows = await db.LeasingInvoices.AsNoTracking().Where(x => x.AccountId == account && x.Category == LeasingInvoiceCategory.Rental && x.FinanceOrganizationId.HasValue && financeOrganizations.Contains(x.FinanceOrganizationId.Value) && x.Currency == currency && x.Status != LeasingInvoiceStatus.Reversed && x.Status != LeasingInvoiceStatus.Rejected).ToListAsync(ct);
            var ids = rows.Select(x => x.Id).ToArray();
            return new(rows, await db.LeasingPaymentAllocations.AsNoTracking().Where(x => x.AccountId == account && ids.Contains(x.InvoiceId) && !x.Reversed).ToListAsync(ct));
        }
        public async Task CommitAsync(LeasingPaymentEvent history, CancellationToken ct)
        {
            if (tx is null || state.Acquisition is null || history.AccountId != account || history.AcquisitionId != acquisition || history.InvoiceId is not null || state.Events.Any(x => x.AccountId != account || x.AcquisitionId != acquisition) || state.Dispositions.Any(x => x.AccountId != account || x.AcquisitionId != acquisition) || state.Equipment.Any(x => x.AccountId != account || x.AcquisitionId != acquisition)) throw new UnauthorizedAccessException();
            if (state.Lifecycle is { } lifecycle)
            {
                if (lifecycle.AccountId != account || lifecycle.AcquisitionId != acquisition) throw new UnauthorizedAccessException();
                if (!hadLifecycle) db.Entry(lifecycle).State = EntityState.Added;
            }
            foreach (var e in state.Events.Where(x => !events.Contains(x.Id))) db.Entry(e).State = EntityState.Added;
            foreach (var d in state.Dispositions.Where(x => !dispositions.Contains(x.Id))) db.Entry(d).State = EntityState.Added;
            db.LeasingPaymentEvents.Add(history); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); }
    }
}
