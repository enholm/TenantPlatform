using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingRentalInvoiceData(IDbContextFactory<TenantPlatformDbContext> factory,IAgreementDocumentStorage storage,ILogger<LeasingRentalInvoiceData> logger):ILeasingRentalInvoiceData
{
    public async Task<ILeasingRentalInvoiceWork> OpenAsync(Guid account,Guid? invoice,Guid? request,bool write,CancellationToken ct)
    {
        var db=await factory.CreateDbContextAsync(ct);
        try
        {
            IDbContextTransaction? tx=null;
            if(write){tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE",ct);}
            else db.ChangeTracker.QueryTrackingBehavior=QueryTrackingBehavior.NoTracking;
            // Matching, duplicate/credit validation and closure share this account snapshot
            // inside the selected invoice transaction. No role decision is made here.
            var invoices=await db.LeasingInvoices.Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental).OrderByDescending(x=>x.UploadedUtc).ToListAsync(ct);
            var selected=invoices.SingleOrDefault(x=>x.Id==invoice);
            var installments=await db.LeasingInstallments.Where(x=>x.AccountId==account).ToListAsync(ct);
            var state=new LeasingRentalInvoiceState
            {
                Invoice=selected,AccountInvoices=invoices,Installments=installments,
                Acquisitions=await db.LeasingAcquisitions.AsNoTracking().Where(x=>x.AccountId==account).ToListAsync(ct),
                Frameworks=await db.LeasingFrameworks.AsNoTracking().Where(x=>x.AccountId==account).ToListAsync(ct),
                Orders=await db.LeasingOrders.AsNoTracking().Where(x=>x.AccountId==account).ToListAsync(ct),
                Plans=await db.LeasingPaymentPlans.AsNoTracking().Where(x=>x.AccountId==account).Include(x=>x.Terms).ToListAsync(ct),
                Financing=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==account).ToListAsync(ct),
                Allocations=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account).ToListAsync(ct),
                Lifecycles=await db.LeasingLifecycles.Where(x=>x.AccountId==account&&x.DocumentReviewComplete).ToListAsync(ct),
                Organizations=await db.Organizations.AsNoTracking().Where(x=>x.AccountId==account).ToListAsync(ct),
                Interpretations=invoice.HasValue?await db.LeasingInvoiceInterpretations.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==invoice).OrderByDescending(x=>x.CreatedUtc).ToListAsync(ct):[],
                History=invoice.HasValue?await db.LeasingPaymentEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==invoice).OrderByDescending(x=>x.RecordedUtc).ToListAsync(ct):[],
                PreviousRequest=request.HasValue?await db.LeasingPaymentEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==request,ct):null
            };
            return new Work(db,tx,account,state);
        }
        catch{await db.DisposeAsync();throw;}
    }
    public async Task CleanupUncommittedAsync(Guid account,string storageKey)
    {
        await using var db=await factory.CreateDbContextAsync();if(await db.LeasingInvoices.AnyAsync(x=>x.AccountId==account&&x.StorageKey==storageKey))return;
        try{await storage.DiscardUncommittedAsync(storageKey);}catch(Exception ex){logger.LogWarning(ex,"Could not discard uncommitted rental invoice file");}
    }
    public async Task DeleteFileAsync(Guid invoice,string storageKey)
    {
        try{await storage.DeleteAsync(storageKey);}catch(Exception ex){logger.LogWarning(ex,"Could not remove the file for deleted rental invoice {InvoiceId}",invoice);}
    }
    private sealed class Work(TenantPlatformDbContext db,IDbContextTransaction? tx,Guid account,LeasingRentalInvoiceState state):ILeasingRentalInvoiceWork
    {
        private readonly Guid? original=state.Invoice?.Id;
        private readonly HashSet<Guid> allocations=state.Allocations.Select(x=>x.Id).ToHashSet();
        public LeasingRentalInvoiceState State=>state;
        public async Task FlushReversalAsync(CancellationToken ct){if(tx is null)throw new InvalidOperationException();await db.SaveChangesAsync(ct);}
        public async Task CommitAsync(IReadOnlyList<LeasingPaymentEvent> history,LeasingInvoiceHistory? uploadHistory,CancellationToken ct)
        {
            var invoice=state.Invoice??throw new InvalidOperationException();
            if(tx is null||invoice.AccountId!=account||invoice.Category!=LeasingInvoiceCategory.Rental||original.HasValue&&original!=invoice.Id||state.Allocations.Any(x=>x.AccountId!=account)||history.Any(x=>x.AccountId!=account)||uploadHistory is not null&&(uploadHistory.AccountId!=account||uploadHistory.InvoiceId!=invoice.Id))throw new UnauthorizedAccessException();
            if(original is null)db.LeasingInvoices.Add(invoice);
            foreach(var part in state.Allocations.Where(x=>!allocations.Contains(x.Id)))db.Entry(part).State=EntityState.Added;
            db.LeasingPaymentEvents.AddRange(history);if(uploadHistory is not null)db.LeasingInvoiceHistory.Add(uploadHistory);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        public async Task DeleteDraftAsync(LeasingPaymentEvent history,CancellationToken ct)
        {
            var invoice=state.Invoice??throw new InvalidOperationException();if(tx is null||invoice.AccountId!=account||history.AccountId!=account||history.InvoiceId is not null)throw new UnauthorizedAccessException();
            db.LeasingInvoiceInterpretations.RemoveRange(await db.LeasingInvoiceInterpretations.Where(x=>x.AccountId==account&&x.InvoiceId==invoice.Id).ToListAsync(ct));
            db.LeasingInvoiceHistory.RemoveRange(await db.LeasingInvoiceHistory.Where(x=>x.AccountId==account&&x.InvoiceId==invoice.Id).ToListAsync(ct));
            foreach(var entry in await db.LeasingPaymentEvents.Where(x=>x.AccountId==account&&x.InvoiceId==invoice.Id).ToListAsync(ct))entry.InvoiceId=null;
            db.LeasingPaymentEvents.Add(history);db.LeasingInvoices.Remove(invoice);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync(){if(tx is not null)await tx.DisposeAsync();await db.DisposeAsync();}
    }
}
