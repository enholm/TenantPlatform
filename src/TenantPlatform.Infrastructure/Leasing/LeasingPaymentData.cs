using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
public sealed class LeasingPaymentData(IDbContextFactory<TenantPlatformDbContext> factory):ILeasingPaymentData
{
    public async Task<LeasingPaymentList> ReadListAsync(LeasingReadScope scope,string? search,string? currency,CancellationToken ct)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var account=scope.AccountId;var user=scope.UserId;
        var query=db.LeasingAcquisitions.AsNoTracking().Where(x=>x.AccountId==account&&(scope.PaymentReader||x.OwnerUserId==user||db.LeasingFrameworks.Any(f=>f.AccountId==account&&f.Id==x.FrameworkId&&f.OwnerUserId==user)));
        if(!string.IsNullOrWhiteSpace(search))query=query.Where(x=>x.Name.ToLower().Contains(search.ToLower())||x.Reference.ToLower().Contains(search.ToLower()));
        if(!string.IsNullOrEmpty(currency))query=query.Where(x=>x.Currency==currency);
        var acquisitions=await query.OrderBy(x=>x.Name).ToListAsync(ct);var ids=acquisitions.Select(x=>x.Id).ToArray();
        return new(acquisitions,await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).ToListAsync(ct),await db.LeasingPaymentPlans.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.AcquisitionId)).Include(x=>x.Terms).ToListAsync(ct),await db.Organizations.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct),await db.AgreementReminderSettings.AsNoTracking().Where(x=>x.AccountId==account).Select(x=>x.TimeZoneId).SingleOrDefaultAsync(ct)??"Europe/Oslo");
    }
    public async Task<ILeasingPaymentWork> OpenAsync(Guid account,Guid target,LeasingPaymentTarget kind,Guid? request,bool write,CancellationToken ct)
    {
        var db=await factory.CreateDbContextAsync(ct);
        try
        {
            IDbContextTransaction? tx=null;
            if(write){tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE",ct);}
            else db.ChangeTracker.QueryTrackingBehavior=QueryTrackingBehavior.NoTracking;
            Guid? acquisition=kind switch
            {
                LeasingPaymentTarget.Plan=>await db.LeasingPaymentPlans.Where(x=>x.AccountId==account&&x.Id==target).Select(x=>(Guid?)x.AcquisitionId).SingleOrDefaultAsync(ct),
                LeasingPaymentTarget.Installment=>await db.LeasingInstallments.Where(x=>x.AccountId==account&&x.Id==target).Select(x=>(Guid?)x.AcquisitionId).SingleOrDefaultAsync(ct),
                _=>target
            };
            var a=await db.LeasingAcquisitions.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==acquisition,ct);
            var f=a?.FrameworkId is Guid frame?await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==frame,ct):null;
            var installments=await db.LeasingInstallments.Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).ToListAsync(ct);var ids=installments.Select(x=>x.Id).ToArray();
            var allocations=await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.InstallmentId)).ToListAsync(ct);var invoiceIds=allocations.Select(x=>x.InvoiceId).Distinct().ToArray();
            return new Work(db,tx,account,acquisition,new(){Acquisition=a,Framework=f,Installments=installments,Allocations=allocations,
                Financing=await db.LeasingFinancingRevisions.Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).OrderBy(x=>x.EffectiveFrom).ToListAsync(ct),
                Plans=await db.LeasingPaymentPlans.Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).Include(x=>x.Terms).OrderByDescending(x=>x.Version).ToListAsync(ct),
                Invoices=await db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&invoiceIds.Contains(x.Id)).ToListAsync(ct),
                Documents=await db.LeasingDocuments.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).ToListAsync(ct),
                History=await db.LeasingPaymentEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).OrderByDescending(x=>x.RecordedUtc).ToListAsync(ct),
                PreviousRequest=request.HasValue?await db.LeasingPaymentEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==request,ct):null});
        }
        catch{await db.DisposeAsync();throw;}
    }
    private sealed class Work(TenantPlatformDbContext db,IDbContextTransaction? tx,Guid account,Guid? acquisition,LeasingPaymentState state):ILeasingPaymentWork
    {
        private readonly HashSet<Guid> plans=state.Plans.Select(x=>x.Id).ToHashSet();
        private readonly HashSet<Guid> installments=state.Installments.Select(x=>x.Id).ToHashSet();
        private readonly HashSet<Guid> financing=state.Financing.Select(x=>x.Id).ToHashSet();
        public LeasingPaymentState State=>state;
        public Task<bool> OrganizationExistsAsync(Guid organization,CancellationToken ct)=>db.Organizations.AnyAsync(x=>x.AccountId==account&&x.Id==organization,ct);
        public Task<bool> MemberExistsAsync(Guid user,CancellationToken ct)=>db.UserAccounts.AnyAsync(x=>x.AccountId==account&&x.UserId==user&&x.User.IsActive,ct);
        public async Task FlushReplacedPlanAsync(CancellationToken ct){if(tx is null)throw new InvalidOperationException();await db.SaveChangesAsync(ct);}
        public async Task CommitAsync(LeasingPaymentEvent history,CancellationToken ct)
        {
            if(tx is null||state.Acquisition is null||history.AccountId!=account||history.AcquisitionId!=acquisition||history.InvoiceId is not null||state.Plans.Any(x=>x.AccountId!=account||x.AcquisitionId!=acquisition||x.Terms.Any(t=>t.AccountId!=account||t.PlanId!=x.Id))||state.Installments.Any(x=>x.AccountId!=account||x.AcquisitionId!=acquisition)||state.Financing.Any(x=>x.AccountId!=account||x.AcquisitionId!=acquisition))throw new UnauthorizedAccessException();
            foreach(var i in state.Installments.Where(x=>!installments.Contains(x.Id)))db.Entry(i).State=EntityState.Added;
            foreach(var p in state.Plans.Where(x=>!plans.Contains(x.Id))){db.Entry(p).State=EntityState.Added;foreach(var t in p.Terms)db.Entry(t).State=EntityState.Added;}
            foreach(var f in state.Financing.Where(x=>!financing.Contains(x.Id)))db.Entry(f).State=EntityState.Added;
            db.LeasingPaymentEvents.Add(history);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
        }
        public async ValueTask DisposeAsync(){if(tx is not null)await tx.DisposeAsync();await db.DisposeAsync();}
    }
}
