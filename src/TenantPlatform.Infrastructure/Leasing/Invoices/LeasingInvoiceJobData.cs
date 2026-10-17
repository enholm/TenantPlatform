using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing.Invoices;
public sealed class LeasingInvoiceJobData(IDbContextFactory<TenantPlatformDbContext> factory) : ILeasingInvoiceJobData
{
    private static IQueryable<LeasingInvoice> Candidates(TenantPlatformDbContext db, DateTimeOffset expired) => db.LeasingInvoices.Where(x => x.Status == LeasingInvoiceStatus.Review &&
        (x.Processing == LeasingInvoiceProcessing.Uploaded || x.Processing == LeasingInvoiceProcessing.Processing && x.ProcessingStartedUtc < expired));
    public async Task<InvoiceWorkTarget?> ReadCandidateAsync(DateTimeOffset expired, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await Candidates(db, expired).AsNoTracking().OrderBy(x => x.UploadedUtc).Select(x => new InvoiceWorkTarget(x.AccountId, x.Id)).FirstOrDefaultAsync(ct); }
    public async Task<bool> ClaimAsync(InvoiceWorkTarget target, Guid token, DateTimeOffset now, DateTimeOffset expired, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Candidates(db, expired).Where(x => x.AccountId == target.AccountId && x.Id == target.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Processing, LeasingInvoiceProcessing.Processing)
            .SetProperty(x => x.ProcessingToken, token).SetProperty(x => x.ProcessingStartedUtc, now).SetProperty(x => x.Revision, Guid.NewGuid()), ct) > 0;
    }
    public async Task<LeasingInvoice?> ReadClaimedAsync(InvoiceWorkTarget target, Guid token, CancellationToken ct)
    { await using var db = await factory.CreateDbContextAsync(ct); return await db.LeasingInvoices.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == target.AccountId && x.Id == target.Id && x.ProcessingToken == token, ct); }
    public async Task<ILeasingInvoiceJobTransaction> BeginPublishAsync(InvoiceWorkTarget target, Guid token, CancellationToken ct)
    {
        var db = await factory.CreateDbContextAsync(ct); IDbContextTransaction? tx = null;
        try
        {
            tx = await db.Database.BeginTransactionAsync(ct); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {target.AccountId} FOR UPDATE", ct);
            var invoice = await db.LeasingInvoices.SingleOrDefaultAsync(x => x.AccountId == target.AccountId && x.Id == target.Id && x.ProcessingToken == token, ct);
            return new Transaction(db, tx, invoice);
        }
        catch { if (tx is not null) await tx.DisposeAsync(); await db.DisposeAsync(); throw; }
    }
    private sealed class Transaction(TenantPlatformDbContext db, IDbContextTransaction tx, LeasingInvoice? invoice) : ILeasingInvoiceJobTransaction
    {
        public LeasingInvoice? Invoice => invoice;
        public async Task CommitAsync(LeasingInvoiceInterpretation? interpretation, LeasingInvoiceHistory history, CancellationToken ct)
        {
            if (invoice is null || history.AccountId != invoice.AccountId || history.InvoiceId != invoice.Id || interpretation is not null && (interpretation.AccountId != invoice.AccountId || interpretation.InvoiceId != invoice.Id)) throw new UnauthorizedAccessException();
            if (interpretation is not null) db.LeasingInvoiceInterpretations.Add(interpretation); db.LeasingInvoiceHistory.Add(history);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync() { try { await tx.DisposeAsync(); } finally { await db.DisposeAsync(); } }
    }
}
