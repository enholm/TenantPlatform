using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing.Invoices;
public sealed class LeasingPurchaseInvoiceData(IDbContextFactory<TenantPlatformDbContext> factory,IAgreementDocumentStorage storage,ILogger<LeasingPurchaseInvoiceData> logger):ILeasingPurchaseInvoiceData
{
    public async Task<List<LeasingInvoice>> ReadListAsync(LeasingReadScope scope,CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var account=scope.AccountId;var user=scope.UserId;
        var ids=db.LeasingAcquisitions.Where(x=>x.AccountId==account&&(x.OwnerUserId==user||db.LeasingFrameworks.Any(f=>f.AccountId==account&&f.Id==x.FrameworkId&&f.OwnerUserId==user))).Select(x=>x.Id);
        return await db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Equipment&&(scope.Administrator||x.AcquisitionId==null&&x.UploadedByUserId==user||ids.Contains(x.AcquisitionId??Guid.Empty))).OrderByDescending(x=>x.UploadedUtc).Take(500).ToListAsync(ct);
    }
    public async Task<LeasingInvoiceUploadFacts> ReadUploadFactsAsync(Guid account,Guid user,Guid? acquisition,CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);
        var a=acquisition.HasValue?await db.LeasingAcquisitions.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==acquisition,ct):null;
        var f=a?.FrameworkId is Guid frame?await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==frame,ct):null;
        var frames=db.LeasingFrameworks.Where(x=>x.AccountId==account&&x.OwnerUserId==user).Select(x=>x.Id);
        var owns=await frames.AnyAsync(ct)||await db.LeasingAcquisitions.AnyAsync(x=>x.AccountId==account&&(x.OwnerUserId==user||frames.Contains(x.FrameworkId??Guid.Empty)),ct)||await db.LeasingOrders.AnyAsync(x=>x.AccountId==account&&(x.OwnerUserId==user||frames.Contains(x.FrameworkId)),ct);
        return new(a,f,owns);
    }
    public async Task<ILeasingPurchaseInvoiceWork> OpenAsync(Guid account,Guid? invoice,bool write,CancellationToken ct)
    {
        var db=await factory.CreateDbContextAsync(ct);
        try
        {
            IDbContextTransaction? tx=null;
            if(write){tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE",ct);}
            else db.ChangeTracker.QueryTrackingBehavior=QueryTrackingBehavior.NoTracking;
            var selected=invoice.HasValue?await db.LeasingInvoices.Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Equipment&&x.Id==invoice,ct):null;
            return new Work(db,tx,account,selected);
        }
        catch{await db.DisposeAsync();throw;}
    }
    public async Task CleanupUncommittedAsync(Guid account,string storageKey)
    {
        await using var db=await factory.CreateDbContextAsync();
        if(await db.LeasingInvoices.AnyAsync(x=>x.AccountId==account&&x.StorageKey==storageKey))return;
        try{await storage.DiscardUncommittedAsync(storageKey);}catch(Exception ex){logger.LogWarning(ex,"Could not discard uncommitted invoice file");}
    }
    private sealed class Work(TenantPlatformDbContext db,IDbContextTransaction? tx,Guid account,LeasingInvoice? selected):ILeasingPurchaseInvoiceWork
    {
        private readonly Guid? original=selected?.Id;
        private readonly HashSet<Guid> lines=selected?.Lines.Select(x=>x.Id).ToHashSet()??[];
        public LeasingInvoice? Invoice {get;set;}=selected;
        public ILeasingAcquisitionWork AcquisitionWork=>tx is null?throw new InvalidOperationException():LeasingAcquisitionData.Join(db,account);
        public async Task<LeasingInvoicePurchase> ReadPurchaseAsync(Guid acquisition,CancellationToken ct)
        {
            var a=await db.LeasingAcquisitions.Where(x=>x.AccountId==account&&x.Id==acquisition).Include(x=>x.Items).ThenInclude(x=>x.DimensionSelections).Include(x=>x.Items).ThenInclude(x=>x.AllocationRows).Include(x=>x.Items).ThenInclude(x=>x.AllocationDimensions).AsSplitQuery().SingleOrDefaultAsync(ct);
            var f=a?.FrameworkId is Guid frame?await ReadFrameworkAsync(frame,ct):null;
            var docs=await db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Equipment&&x.AcquisitionId==acquisition).ToListAsync(ct);var ids=docs.Select(x=>x.Id).ToArray();
            return new(a,f,await db.LeasingOrderRealizations.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==acquisition,ct),docs,await db.LeasingInvoiceLines.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.InvoiceId)).ToListAsync(ct));
        }
        public Task<LeasingFramework?> ReadFrameworkAsync(Guid framework,CancellationToken ct)=>db.LeasingFrameworks.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==framework,ct);
        public async Task<LeasingInvoiceOrder> ReadOrderAsync(Guid order,CancellationToken ct)
        {
            var o=await db.LeasingOrders.Include(x=>x.Lines).SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==order,ct);return new(o,o is null?null:await ReadFrameworkAsync(o.FrameworkId,ct));
        }
        public Task<Organization?> ReadOrganizationAsync(Guid organization,CancellationToken ct)=>db.Organizations.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==organization,ct);
        public Task<List<LeasingInvoice>> ReadApprovedAsync(CancellationToken ct)=>db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Equipment&&x.Status==LeasingInvoiceStatus.Approved&&x.Id!=Invoice!.Id).ToListAsync(ct);
        public Task<List<LeasingInvoiceInterpretation>> ReadInterpretationsAsync(CancellationToken ct)=>db.LeasingInvoiceInterpretations.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==Invoice!.Id).OrderByDescending(x=>x.CreatedUtc).ToListAsync(ct);
        public Task<List<LeasingInvoiceHistory>> ReadHistoryAsync(CancellationToken ct)=>db.LeasingInvoiceHistory.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==Invoice!.Id).OrderByDescending(x=>x.RecordedUtc).ToListAsync(ct);
        public Task<List<LeasingOption>> ReadAcquisitionOptionsAsync(LeasingReadScope scope,CancellationToken ct)=>db.LeasingAcquisitions.AsNoTracking().Where(x=>x.AccountId==account&&x.Status!=LeasingAcquisitionStatus.Cancelled&&(scope.Administrator||x.OwnerUserId==scope.UserId||db.LeasingFrameworks.Any(f=>f.AccountId==account&&f.Id==x.FrameworkId&&f.OwnerUserId==scope.UserId))).OrderBy(x=>x.Name).Select(x=>new LeasingOption(x.Id,x.Name)).ToListAsync(ct);
        public async Task<LeasingCapacity> ReadCapacityAsync(Guid framework,CancellationToken ct)
        {
            var f=await ReadFrameworkAsync(framework,ct)??throw new UnauthorizedAccessException();
            var acquisitions=await db.LeasingAcquisitions.Where(x=>x.AccountId==account&&x.FrameworkId==framework&&x.Status==LeasingAcquisitionStatus.Registered).ToListAsync(ct);
            var orders=await db.LeasingOrders.AsNoTracking().Where(x=>x.AccountId==account&&x.FrameworkId==framework&&x.Status==LeasingOrderStatus.Approved).Include(x=>x.Lines).ToListAsync(ct);
            return new(f.Limit,LeasingCalculator.Used(acquisitions,f.IncludesVat),orders.Sum(x=>LeasingOrderCalculator.Reserved(x,f.IncludesVat)));
        }
        public Task<bool> HasDependenciesAsync(Guid invoice,Guid[] lines,Guid[] items,CancellationToken ct)=>db.LeasingInvoiceLines.AnyAsync(x=>x.AccountId==account&&x.InvoiceId!=invoice&&(lines.Contains(x.CreditedLineId??Guid.Empty)||items.Contains(x.ItemId))&&db.LeasingInvoices.Any(d=>d.AccountId==account&&d.Category==LeasingInvoiceCategory.Equipment&&d.Id==x.InvoiceId&&d.Status==LeasingInvoiceStatus.Approved),ct);
        public async Task SaveInvoiceAsync(LeasingInvoiceHistory history,CancellationToken ct)
        {
            var invoice=Invoice??throw new InvalidOperationException();
            if(tx is null||invoice.AccountId!=account||invoice.Category!=LeasingInvoiceCategory.Equipment||original.HasValue&&original!=invoice.Id||history.AccountId!=account||history.InvoiceId!=invoice.Id||invoice.Lines.Any(x=>x.AccountId!=account||x.InvoiceId!=invoice.Id))throw new UnauthorizedAccessException();
            if(original is null&&db.Entry(invoice).State==EntityState.Detached)db.LeasingInvoices.Add(invoice);
            foreach(var line in invoice.Lines.Where(x=>!lines.Contains(x.Id)))db.Entry(line).State=EntityState.Added;
            db.LeasingInvoiceHistory.Add(history);await db.SaveChangesAsync(ct);
        }
        public async Task SaveFinancialHistoryAsync(LeasingHistory history,CancellationToken ct)
        {
            if(tx is null||history.AccountId!=account||history.AcquisitionId!=Invoice?.AcquisitionId)throw new UnauthorizedAccessException();db.LeasingHistory.Add(history);await db.SaveChangesAsync(ct);
        }
        public Task CommitAsync(CancellationToken ct)=>tx?.CommitAsync(ct)??throw new InvalidOperationException();
        public async ValueTask DisposeAsync(){if(tx is not null)await tx.DisposeAsync();await db.DisposeAsync();}
    }
}
