using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Leasing;

public sealed record PaymentPermissions(bool CanRegister, bool CanActivate, bool CanApproveInvoices, bool CanAcceptVariance);
public sealed record PaymentTermView(LeasingPlanTerm Term, LeasingInstallment Installment, decimal InvoicedNet, decimal InvoicedVat, LeasingBillingStatus Status, List<LeasingPaymentAllocation> Allocations);
public sealed record PaymentDetails(LeasingAcquisition Acquisition, List<LeasingFinancingRevision> Financing, List<LeasingPaymentPlan> Plans,
    List<PaymentTermView> Terms, List<LeasingInvoice> Invoices, List<LeasingDocument> Documents, List<LeasingPaymentEvent> History, PaymentPermissions Permissions);
public sealed record PaymentPlanRow(Guid AcquisitionId, string Acquisition, string FinanceCompany, string Currency, Guid? PlanId, int? Version, LeasingPlanStatus? Status, DateOnly? From, DateOnly? To, DateOnly? NextDue, bool NeedsReview);

public sealed partial class LeasingService
{
    private async Task<bool> PaymentReader(bool admin, CancellationToken ct) => admin || await ReportReader(ct) || await authorization.CanActivateLeasingPlansAsync(ct) || await authorization.CanApproveLeasingInvoicesAsync(ct) || await authorization.CanAcceptLeasingVariancesAsync(ct);
    private void PaymentEvent(TenantPlatformDbContext db, Guid account, Guid? acquisition, Guid? invoice, Guid user, string action, string reason, string before, string after, Guid request) =>
        db.LeasingPaymentEvents.Add(new() { Id=request,AccountId=account,AcquisitionId=acquisition,InvoiceId=invoice,ActorUserId=user,Action=action,Reason=reason,BeforeJson=before,AfterJson=after,RecordedUtc=clock.GetUtcNow() });
    private static async Task<bool> PaymentRetry(TenantPlatformDbContext db, Guid account, Guid request, Guid user, string action, Guid? acquisition, Guid? invoice, CancellationToken ct)
    {
        if(request==Guid.Empty)throw new LeasingValidationException("PaymentInvalid");
        var e=await db.LeasingPaymentEvents.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==request,ct);
        if(e==null)return false;
        if(e.ActorUserId!=user||e.Action!=action||e.AcquisitionId!=acquisition||e.InvoiceId!=invoice)throw new LeasingValidationException("LeasingConcurrency");
        return true;
    }
    private async Task EnsureInitialFinancing(TenantPlatformDbContext db, LeasingAcquisition a, Guid user, CancellationToken ct)
    {
        if(!await db.LeasingFinancingRevisions.AnyAsync(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id,ct))
            db.LeasingFinancingRevisions.Add(new(){Id=Guid.NewGuid(),AccountId=a.AccountId,AcquisitionId=a.Id,SnapshotJson=Snapshot(LeasingFinancingSnapshot.From(a)),ActorUserId=user,RecordedUtc=clock.GetUtcNow(),Reason="PaymentInitialUnknownDate"});
    }
    public static LeasingFinancingRevision? FinancingAt(IEnumerable<LeasingFinancingRevision> revisions, DateOnly date) => revisions.Where(x=>x.EffectiveFrom.HasValue&&x.EffectiveFrom<=date).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault();
    public async Task AddFinancingRevisionAsync(Guid account, Guid acquisition, Guid acquisitionRevision, DateOnly effectiveFrom, LeasingFinancingSnapshot input, string reason, Guid request, CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var a=await Acquisitions(db,account,user,admin).SingleOrDefaultAsync(x=>x.Id==acquisition,ct)??throw new UnauthorizedAccessException();
        if(await PaymentRetry(db,account,request,user,"FinancingRevised",acquisition,null,ct))return;
        CheckRevision(a.Revision,acquisitionRevision,true);Reason(reason,true);
        if(a.Status==LeasingAcquisitionStatus.Cancelled||effectiveFrom==default||input.Currency!=a.Currency||input.Terms.Months!=a.Terms.Months)throw new LeasingValidationException("PaymentInvalid");
        await References(db,account,input.FinanceOrganizationId,a.OwnerUserId,ct);
        ValidateCommon(a.Name,a.Reference,input.Currency,null,input.Terms);
        var t=input.Terms;
        if(input.FinancedAmount<0||input.FinancedAmount>1000000000000m||!Precision(input.FinancedAmount,2)||
            new[]{t.AdvanceRent,t.ResidualValue,t.EstablishmentFee,t.OtherFees}.Any(x=>x.HasValue&&(x<0||x>1000000000000m||!Precision(x.Value,2)))||
            new[]{t.ObservedReferenceRate,t.RateFloor,t.RateCap}.Any(x=>x.HasValue&&(x is < -100 or >100||!Precision(x.Value,4)))||
            t.PaymentTiming.HasValue&&!Enum.IsDefined(t.PaymentTiming.Value)||t.RateResetFrequency.HasValue&&!Enum.IsDefined(t.RateResetFrequency.Value)||
            t.RateLimitBasis.HasValue&&!Enum.IsDefined(t.RateLimitBasis.Value)||t.RateFloor.HasValue&&t.RateCap.HasValue&&t.RateFloor>t.RateCap||
            (t.RateFloor.HasValue||t.RateCap.HasValue)&&!t.RateLimitBasis.HasValue||t.ObservedReferenceRate.HasValue&&!t.ObservationDate.HasValue||
            t.FirstDueDate==DateOnly.MinValue||t.ObservationDate==DateOnly.MinValue||t.FinanceReference?.Length>200||t.DocumentReference?.Length>500||t.FinancingNotes?.Length>10000||
            t.ResidualDocumentReference?.Length>500||t.ResidualIsObligation&&(t.ResidualValue==null||string.IsNullOrWhiteSpace(t.ResidualDocumentReference)))throw new LeasingValidationException("PaymentInvalidTerms");
        if(await db.LeasingFinancingRevisions.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.EffectiveFrom==effectiveFrom,ct))throw new LeasingValidationException("PaymentEffectiveDuplicate");
        await EnsureInitialFinancing(db,a,user,ct);
        var revision=new LeasingFinancingRevision{Id=request,AccountId=account,AcquisitionId=a.Id,EffectiveFrom=effectiveFrom,SnapshotJson=Snapshot(input),ActorUserId=user,RecordedUtc=clock.GetUtcNow(),Reason=reason};
        db.LeasingFinancingRevisions.Add(revision);
        var affected=await db.LeasingPlanTerms.Where(x=>x.AccountId==account&&x.PeriodTo>=effectiveFrom&&db.LeasingPaymentPlans.Any(p=>p.AccountId==account&&p.Id==x.PlanId&&p.AcquisitionId==a.Id&&p.Status==LeasingPlanStatus.Active)).ToListAsync(ct);
        foreach(var term in affected){term.NeedsReview=true;term.ReviewReason=null;}
        a.Revision=Guid.NewGuid();
        PaymentEvent(db,account,a.Id,null,user,"FinancingRevised",reason,"{}",Snapshot(revision),request);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<PaymentDetails> GetPaymentsAsync(Guid account, Guid acquisition, CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var reader=await PaymentReader(admin,ct);
        var a=await Acquisitions(db,account,user,reader).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==acquisition,ct)??throw new UnauthorizedAccessException();
        var financing=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).OrderBy(x=>x.EffectiveFrom).ToListAsync(ct);
        var plans=await db.LeasingPaymentPlans.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).Include(x=>x.Terms).OrderByDescending(x=>x.Version).ToListAsync(ct);
        var obligations=await db.LeasingInstallments.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).ToDictionaryAsync(x=>x.Id,ct);
        var ids=obligations.Keys.ToArray();var allocations=await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.InstallmentId)).ToListAsync(ct);
        var invoiceIds=allocations.Select(x=>x.InvoiceId).Distinct().ToArray();var invoices=await db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&invoiceIds.Contains(x.Id)).ToListAsync(ct);
        var active=plans.SingleOrDefault(x=>x.Status==LeasingPlanStatus.Active);
        var views=new List<PaymentTermView>();
        foreach(var term in active?.Terms.OrderBy(x=>x.DueDate).ThenBy(x=>x.Reference)??Enumerable.Empty<LeasingPlanTerm>())
        {
            var links=allocations.Where(x=>x.InstallmentId==term.InstallmentId&&!x.Reversed&&invoices.Any(i=>i.Id==x.InvoiceId&&i.Status==LeasingInvoiceStatus.Approved)).ToList();
            decimal net=0,vat=0;foreach(var link in links){var sign=invoices.Single(x=>x.Id==link.InvoiceId).Kind==LeasingInvoiceKind.Invoice?1:-1;net+=sign*link.Net;vat+=sign*link.Vat;}
            var obligation=obligations[term.InstallmentId];views.Add(new(term,obligation,net,vat,LeasingPaymentCalculator.Status(term,obligation,net,vat,links.Count>0),links));
        }
        return new(a,financing,plans,views,invoices,await db.LeasingDocuments.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).ToListAsync(ct),
            await db.LeasingPaymentEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==acquisition).OrderByDescending(x=>x.RecordedUtc).ToListAsync(ct),
            new(await Acquisitions(db,account,user,admin).AnyAsync(x=>x.Id==acquisition,ct)&&a.Status!=LeasingAcquisitionStatus.Cancelled,await authorization.CanActivateLeasingPlansAsync(ct),await authorization.CanApproveLeasingInvoicesAsync(ct),await authorization.CanAcceptLeasingVariancesAsync(ct)));
    }
    public async Task<List<PaymentPlanRow>> ListPaymentPlansAsync(Guid account, string? search=null, string? currency=null, Guid? finance=null, LeasingPlanStatus? status=null, CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var reader=await PaymentReader(admin,ct);
        var acquisitions=Acquisitions(db,account,user,reader).AsNoTracking();if(!string.IsNullOrWhiteSpace(search))acquisitions=acquisitions.Where(x=>x.Name.ToLower().Contains(search.ToLower())||x.Reference.ToLower().Contains(search.ToLower()));
        if(!string.IsNullOrEmpty(currency))acquisitions=acquisitions.Where(x=>x.Currency==currency);
        var rows=new List<PaymentPlanRow>();var today=await OrderToday(db,account,ct);
        var names=await db.Organizations.Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        foreach(var a in await acquisitions.OrderBy(x=>x.Name).ToListAsync(ct))
        {
            var revisions=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct);
            var current=FinancingAt(revisions,today);var party=current==null?a.FinanceOrganizationId:JsonSerializer.Deserialize<LeasingFinancingSnapshot>(current.SnapshotJson)!.FinanceOrganizationId;
            if(finance.HasValue&&party!=finance.Value)continue;
            var plans=await db.LeasingPaymentPlans.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).Include(x=>x.Terms).ToListAsync(ct);
            var shown=status.HasValue?plans.Where(x=>x.Status==status).ToList():plans.Where(x=>x.Status!=LeasingPlanStatus.Replaced).ToList();
            if(shown.Count==0&&!status.HasValue)rows.Add(new(a.Id,a.Name,names.GetValueOrDefault(party,""),a.Currency,null,null,null,null,null,null,true));
            foreach(var p in shown)rows.Add(new(a.Id,a.Name,names.GetValueOrDefault(party,""),p.Currency,p.Id,p.Version,p.Status,p.Terms.Select(x=>(DateOnly?)x.PeriodFrom).Min(),p.Terms.Select(x=>(DateOnly?)x.PeriodTo).Max(),p.Terms.Where(x=>x.DueDate>=today).Select(x=>(DateOnly?)x.DueDate).Min(),p.Terms.Any(x=>x.NeedsReview)||current==null));
        }
        return rows;
    }
    private static bool PaymentMoney(decimal? value)=>!value.HasValue||value>=0&&value<=1000000000000m&&Precision(value.Value,2);
    private static void ValidatePlanTerm(LeasingPlanTerm term, LeasingAcquisition a)
    {
        if(string.IsNullOrWhiteSpace(term.Reference)||term.Reference.Length>100||term.PeriodFrom==default||term.PeriodTo<term.PeriodFrom||term.DueDate==default||!Enum.IsDefined(term.Type)||
            new[]{term.Net,term.Vat,term.Gross,term.CapitalComponent,term.InterestComponent,term.FeeComponent}.Any(x=>!PaymentMoney(x))||
            term.Net.HasValue&&term.Vat.HasValue&&term.Gross.HasValue&&term.Net+term.Vat!=term.Gross||term.ReviewReason?.Length>2000||term.ObligationDocumentReference?.Length>500)throw new LeasingValidationException("PaymentInvalidTerm");
        // Components are a net breakdown, never extra amounts added to the term.
        if(term.ComponentsComplete&&(!term.Net.HasValue||!term.CapitalComponent.HasValue||!term.InterestComponent.HasValue||!term.FeeComponent.HasValue||term.CapitalComponent+term.InterestComponent+term.FeeComponent!=term.Net))throw new LeasingValidationException("PaymentComponentsMismatch");
        if(term.Type==LeasingPaymentType.ResidualObligation&&string.IsNullOrWhiteSpace(term.ObligationDocumentReference))throw new LeasingValidationException("PaymentResidualDocumentation");
        term.NeedsReview=term.PeriodFrom<a.PurchaseDate||term.PeriodTo>a.EndDate||term.Net==null||term.Vat==null||term.Gross==null;
    }
    public async Task<Guid> SavePaymentPlanAsync(Guid account, Guid acquisition, Guid? basedOn, List<LeasingPlanTerm> terms, string? notes, LeasingPlanSource source, Guid? document, Guid request, CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var a=await Acquisitions(db,account,user,admin).SingleOrDefaultAsync(x=>x.Id==acquisition,ct)??throw new UnauthorizedAccessException();
        if(await PaymentRetry(db,account,request,user,"PlanDraft",acquisition,null,ct))
        {
            var saved=await db.LeasingPaymentEvents.AsNoTracking().SingleAsync(x=>x.AccountId==account&&x.Id==request,ct);
            return JsonSerializer.Deserialize<LeasingPaymentPlan>(saved.AfterJson)!.Id;
        }
        if(a.Status==LeasingAcquisitionStatus.Cancelled||terms.Count is <1 or >600||notes?.Length>10000||!Enum.IsDefined(source))throw new LeasingValidationException("PaymentInvalid");
        if(source==LeasingPlanSource.Imported&&!document.HasValue)throw new LeasingValidationException("PaymentSourceRequired");
        if(document.HasValue&&!await db.LeasingDocuments.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Id==document,ct))throw new UnauthorizedAccessException();
        var active=await db.LeasingPaymentPlans.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active,ct);
        if(active?.Id!=basedOn)throw new LeasingValidationException("LeasingConcurrency");
        var refs=new HashSet<string>();
        var financing=await db.LeasingFinancingRevisions.Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct);
        foreach(var term in terms)
        {
            term.Reference=term.Reference.Trim().ToUpperInvariant();ValidatePlanTerm(term,a);if(!refs.Add(term.Reference))throw new LeasingValidationException("PaymentDuplicateTerm");
            if(term.FinancingRevisionId.HasValue&&!financing.Any(x=>x.Id==term.FinancingRevisionId))throw new UnauthorizedAccessException();
            var applicable=FinancingAt(financing,term.PeriodFrom);if(applicable==null||applicable.Id!=term.FinancingRevisionId)term.NeedsReview=true;
        }
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(new{a.Currency,basedOn,notes,terms=terms.Select(x=>new{x.Reference,x.PeriodFrom,x.PeriodTo,x.DueDate,x.Type,x.Net,x.Vat,x.Gross,x.CapitalComponent,x.InterestComponent,x.FeeComponent,x.ComponentsComplete,x.FinancingRevisionId,x.ReviewReason,x.ObligationDocumentReference})}))));
        var duplicate=await db.LeasingPaymentPlans.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.ContentHash==fingerprint,ct);
        if(duplicate!=null)
        {
            PaymentEvent(db,account,a.Id,null,user,"PlanDraft","Existing identical content","{}",Snapshot(duplicate),request);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return duplicate.Id;
        }
        var p=new LeasingPaymentPlan{Id=request,AccountId=account,AcquisitionId=a.Id,Version=1+(await db.LeasingPaymentPlans.Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).MaxAsync(x=>(int?)x.Version,ct)??0),Status=LeasingPlanStatus.Draft,Source=source,SourceDocumentId=document,Currency=a.Currency,BasedOnPlanId=basedOn,ContentHash=fingerprint,Revision=Guid.NewGuid(),CreatedByUserId=user,CreatedUtc=clock.GetUtcNow(),Notes=notes};
        foreach(var input in terms)
        {
            var obligation=await db.LeasingInstallments.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Reference==input.Reference,ct);
            if(input.InstallmentId!=Guid.Empty&&obligation?.Id!=input.InstallmentId)throw new LeasingValidationException("PaymentStableReference");
            if(obligation==null){obligation=new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=a.Id,Reference=input.Reference,Revision=Guid.NewGuid()};db.LeasingInstallments.Add(obligation);}
            var term=JsonSerializer.Deserialize<LeasingPlanTerm>(Snapshot(input))!;term.Id=Guid.NewGuid();term.AccountId=account;term.PlanId=p.Id;term.InstallmentId=obligation.Id;p.Terms.Add(term);
        }
        db.LeasingPaymentPlans.Add(p);PaymentEvent(db,account,a.Id,null,user,"PlanDraft","","{}",Snapshot(p),request);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return p.Id;
    }
    public async Task ActivatePaymentPlanAsync(Guid account, Guid plan, Guid revision, string reason, bool invoicedChangesConfirmed, Guid request, CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        if(!await authorization.CanActivateLeasingPlansAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var p=await db.LeasingPaymentPlans.Include(x=>x.Terms).SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==plan,ct)??throw new UnauthorizedAccessException();
        if(await PaymentRetry(db,account,request,user,"PlanActivated",p.AcquisitionId,null,ct))return;
        if(p.Status==LeasingPlanStatus.Active)return;CheckRevision(p.Revision,revision,true);Reason(reason,true);
        if(p.Status!=LeasingPlanStatus.Draft)throw new LeasingValidationException("LeasingInvalidTransition");
        var a=await db.LeasingAcquisitions.SingleAsync(x=>x.AccountId==account&&x.Id==p.AcquisitionId,ct);if(a.Status!=LeasingAcquisitionStatus.Registered)throw new LeasingValidationException("PaymentRegisteredRequired");
        var old=await db.LeasingPaymentPlans.Include(x=>x.Terms).SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active,ct);
        if(old?.Id!=p.BasedOnPlanId)throw new LeasingValidationException("LeasingConcurrency");
        var financing=await db.LeasingFinancingRevisions.Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct);
        foreach(var term in p.Terms)
        {
            ValidatePlanTerm(term,a);var applicable=FinancingAt(financing,term.PeriodFrom);if(applicable==null||applicable.Id!=term.FinancingRevisionId)term.NeedsReview=true;
            if(term.Net==null||term.Vat==null||term.Gross==null)throw new LeasingValidationException("PaymentUnknownAmounts");
            if(term.NeedsReview&&string.IsNullOrWhiteSpace(term.ReviewReason))throw new LeasingValidationException("PaymentReviewRequired");
            term.NeedsReview=false;
        }
        var allocatedIds=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account&&db.LeasingInstallments.Any(i=>i.AccountId==account&&i.Id==x.InstallmentId&&i.AcquisitionId==a.Id)).Select(x=>x.InstallmentId).Distinct().ToListAsync(ct);
        if(allocatedIds.Any(id=>p.Terms.All(t=>t.InstallmentId!=id)))throw new LeasingValidationException("PaymentAllocatedRemoval");
        var changed=old?.Terms.Where(t=>allocatedIds.Contains(t.InstallmentId)&&p.Terms.Any(n=>n.InstallmentId==t.InstallmentId&&TermFinancialSnapshot(n)!=TermFinancialSnapshot(t))).ToList()??[];
        if(changed.Count>0&&!invoicedChangesConfirmed)throw new LeasingValidationException("PaymentConfirmInvoicedChange");
        foreach(var id in changed.Select(x=>x.InstallmentId)){var obligation=await db.LeasingInstallments.SingleAsync(x=>x.AccountId==account&&x.Id==id,ct);obligation.VarianceAccepted=false;obligation.BillingComplete=false;obligation.Revision=Guid.NewGuid();}
        var before=old==null?"{}":Snapshot(old);
        if(old!=null){old.Status=LeasingPlanStatus.Replaced;old.Revision=Guid.NewGuid();await db.SaveChangesAsync(ct);}
        p.Status=LeasingPlanStatus.Active;p.CheckedByUserId=user;p.CheckedUtc=clock.GetUtcNow();p.Revision=Guid.NewGuid();
        PaymentEvent(db,account,a.Id,null,user,"PlanActivated",reason,before,Snapshot(p),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static string TermFinancialSnapshot(LeasingPlanTerm t)=>Snapshot(new{t.PeriodFrom,t.PeriodTo,t.DueDate,t.Type,t.Net,t.Vat,t.Gross,t.CapitalComponent,t.InterestComponent,t.FeeComponent,t.ComponentsComplete,t.FinancingRevisionId});
    public async Task ReviewPaymentTermAsync(Guid account, Guid installment, Guid revision, bool billingComplete, bool acceptVariance, bool reviewPlan, string reason, Guid request, CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        if(reviewPlan?!await authorization.CanActivateLeasingPlansAsync(ct):!await authorization.CanAcceptLeasingVariancesAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var i=await db.LeasingInstallments.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==installment,ct)??throw new UnauthorizedAccessException();
        if(await PaymentRetry(db,account,request,user,"TermReviewed",i.AcquisitionId,null,ct))return;
        CheckRevision(i.Revision,revision,true);Reason(reason,true);
        var t=await db.LeasingPlanTerms.SingleOrDefaultAsync(x=>x.AccountId==account&&x.InstallmentId==i.Id&&db.LeasingPaymentPlans.Any(p=>p.AccountId==account&&p.Id==x.PlanId&&p.Status==LeasingPlanStatus.Active),ct)??throw new LeasingValidationException("PaymentActiveRequired");
        var before=Snapshot(new{i,t});
        if(reviewPlan){t.NeedsReview=false;t.ReviewReason=reason;}
        else {if(acceptVariance&&!billingComplete)throw new LeasingValidationException("PaymentCompletionRequired");i.BillingComplete=billingComplete;i.VarianceAccepted=acceptVariance;i.ControlReason=reason;i.CheckedByUserId=user;i.CheckedUtc=clock.GetUtcNow();}
        i.Revision=Guid.NewGuid();PaymentEvent(db,account,i.AcquisitionId,null,user,"TermReviewed",reason,before,Snapshot(new{i,t}),request);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
