using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Leasing.Invoices;
using TenantPlatform.Web.Services.Agreements;
namespace TenantPlatform.Web.Services.Leasing;

public sealed class RentalInvoiceReview
{
    public InvoiceData Data {get;set;}=new();
    public Guid FinanceOrganizationId {get;set;}
    public Guid? OriginalInvoiceId {get;set;}
    public string AgreementReference {get;set;}="";
    public string PeriodReference {get;set;}="";
    public bool ContentConfirmed {get;set;}
    public string Reason {get;set;}="";
    public List<RentalAllocationInput> Allocations {get;set;}=[];
}
public sealed class RentalAllocationInput
{
    public Guid InstallmentId {get;set;}
    public decimal Net {get;set;}
    public decimal Vat {get;set;}
    public Guid? CreditedAllocationId {get;set;}
}
public sealed record RentalMatchOption(Guid InstallmentId,Guid AcquisitionId,string Acquisition,string Reference,DateOnly From,DateOnly To,DateOnly Due,decimal? Net,decimal? Vat,string Currency,string AgreementReference,bool Suggested);
public sealed record RentalInvoiceDetails(LeasingInvoice Invoice,RentalInvoiceReview Review,List<LeasingInvoiceInterpretation> Interpretations,List<LeasingPaymentAllocation> Allocations,List<LeasingPaymentEvent> History,List<RentalMatchOption> Options,List<LeasingInvoice> CreditSources,List<LeasingPaymentAllocation> CreditAllocations,bool CanEdit,bool CanApprove);
public sealed record RentalInvoiceRow(LeasingInvoice Invoice,string FinanceName,decimal AllocatedNet,decimal AllocatedVat,List<string> Acquisitions);

public sealed partial class LeasingService
{
    private static IQueryable<LeasingInvoice> RentalInvoices(TenantPlatformDbContext db,Guid account,Guid user,bool reader)=>db.LeasingInvoices.Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&(reader||x.UploadedByUserId==user));
    private static RentalInvoiceReview RentalReview(LeasingInvoice invoice)=>JsonSerializer.Deserialize<RentalInvoiceReview>(invoice.ReviewJson)??new();
    public async Task<bool> CanRegisterRentalInvoiceAsync(Guid account,CancellationToken ct=default) { await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);return admin||await Acquisitions(db,account,user,false).AnyAsync(ct); }
    public async Task<Guid> NewRentalInvoiceAsync(Guid account,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        if(!admin&&!await Acquisitions(db,account,user,false).AnyAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        if(await RentalInvoices(db,account,user,admin).AnyAsync(x=>x.Id==request,ct))return request;
        if(request==Guid.Empty)throw new LeasingValidationException("PaymentInvalid");
        var invoice=new LeasingInvoice{Id=request,AccountId=account,Category=LeasingInvoiceCategory.Rental,Status=LeasingInvoiceStatus.Review,Processing=LeasingInvoiceProcessing.Ready,Revision=Guid.NewGuid(),UploadedByUserId=user,UploadedUtc=clock.GetUtcNow(),ReviewJson=Snapshot(new RentalInvoiceReview())};db.LeasingInvoices.Add(invoice);
        PaymentEvent(db,account,null,invoice.Id,user,"RentalCreated","","{}",invoice.ReviewJson,request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return invoice.Id;
    }
    public async Task DeleteRentalInvoiceAsync(Guid account, Guid id, Guid revision, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await Member(db, account, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(db, account, ct);
        var invoice = await RentalInvoices(db, account, user, admin).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
        if (invoice.Status != LeasingInvoiceStatus.Review) throw new LeasingValidationException("RentalDeleteOnlyDraft");
        CheckRevision(invoice.Revision, revision, true);
        db.LeasingInvoiceInterpretations.RemoveRange(await db.LeasingInvoiceInterpretations.Where(x => x.AccountId == account && x.InvoiceId == id).ToListAsync(ct));
        db.LeasingInvoiceHistory.RemoveRange(await db.LeasingInvoiceHistory.Where(x => x.AccountId == account && x.InvoiceId == id).ToListAsync(ct));
        // Keep the payment audit trail after removing the draft and its foreign-key reference.
        foreach (var entry in await db.LeasingPaymentEvents.Where(x => x.AccountId == account && x.InvoiceId == id).ToListAsync(ct)) entry.InvoiceId = null;
        PaymentEvent(db, account, null, null, user, "RentalDeleted", id.ToString(), invoice.ReviewJson, "{}", Guid.NewGuid());
        db.LeasingInvoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (!string.IsNullOrEmpty(invoice.StorageKey))
        {
            try { await storage.DeleteAsync(invoice.StorageKey); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not remove the file for deleted rental invoice {InvoiceId}", id); }
        }
    }
    public Task<Guid> UploadRentalInvoiceAsync(Guid account,string filename,Stream stream,CancellationToken ct=default)=>UploadInvoiceCore(account,null,filename,stream,LeasingInvoiceCategory.Rental,ct);
    public async Task<List<RentalInvoiceRow>> ListRentalInvoicesAsync(Guid account,string? search=null,string? currency=null,Guid? finance=null,LeasingInvoiceStatus? status=null,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var reader=await PaymentReader(admin,ct);
        var query=RentalInvoices(db,account,user,reader).AsNoTracking();if(!string.IsNullOrWhiteSpace(search))query=query.Where(x=>x.Number.ToLower().Contains(search.ToLower())||x.FileName.ToLower().Contains(search.ToLower()));
        if(!string.IsNullOrEmpty(currency))query=query.Where(x=>x.Currency==currency);if(finance.HasValue)query=query.Where(x=>x.FinanceOrganizationId==finance);if(status.HasValue)query=query.Where(x=>x.Status==status);
        var invoices=await query.OrderByDescending(x=>x.UploadedUtc).ToListAsync(ct);var ids=invoices.Select(x=>x.Id).ToArray();
        var allocations=await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==account&&ids.Contains(x.InvoiceId)&&!x.Reversed).ToListAsync(ct);
        var obligations=await db.LeasingInstallments.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.AcquisitionId,ct);
        var names=await db.LeasingAcquisitions.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        var parties=await db.Organizations.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        return invoices.Select(i=>new RentalInvoiceRow(i,parties.GetValueOrDefault(i.FinanceOrganizationId??Guid.Empty,""),allocations.Where(x=>x.InvoiceId==i.Id).Sum(x=>x.Net),allocations.Where(x=>x.InvoiceId==i.Id).Sum(x=>x.Vat),allocations.Where(x=>x.InvoiceId==i.Id).Select(x=>names[obligations[x.InstallmentId]]).Distinct().ToList())).ToList();
    }
    public async Task<RentalInvoiceDetails> GetRentalInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var reader=await PaymentReader(admin,ct);
        var invoice=await RentalInvoices(db,account,user,reader).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();var review=RentalReview(invoice);
        var candidates=await Acquisitions(db,account,user,reader).AsNoTracking().Where(x=>x.Status==LeasingAcquisitionStatus.Registered&&x.Currency==review.Data.Currency).ToListAsync(ct);
        var options=new List<RentalMatchOption>();
        foreach(var a in candidates)
        {
            var terms=await db.LeasingPlanTerms.AsNoTracking().Where(x=>x.AccountId==account&&db.LeasingPaymentPlans.Any(p=>p.AccountId==account&&p.Id==x.PlanId&&p.AcquisitionId==a.Id&&p.Status==LeasingPlanStatus.Active)).ToListAsync(ct);
            var revisions=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id).ToListAsync(ct);
            foreach(var t in terms)
            {
                var financing=TermFinancing(a,t,revisions);if(financing.FinanceOrganizationId!=review.FinanceOrganizationId)continue;
                var reference=financing.Terms.FinanceReference??"";var suggested=reference!=""&&(reference==review.AgreementReference||reference==review.Data.OrderReference);
                options.Add(new(t.InstallmentId,a.Id,a.Name,t.Reference,t.PeriodFrom,t.PeriodTo,t.DueDate,t.Net,t.Vat,a.Currency,reference,suggested));
            }
        }
        var sources=await RentalInvoices(db,account,user,reader).AsNoTracking().Where(x=>x.Status==LeasingInvoiceStatus.Approved&&x.Kind==LeasingInvoiceKind.Invoice&&x.Currency==review.Data.Currency&&x.FinanceOrganizationId==review.FinanceOrganizationId).ToListAsync(ct);
        var sourceIds=sources.Select(x=>x.Id).ToArray();
        return new(invoice,review,await db.LeasingInvoiceInterpretations.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==id).OrderByDescending(x=>x.CreatedUtc).ToListAsync(ct),
            await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==id).ToListAsync(ct),
            await db.LeasingPaymentEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.InvoiceId==id).OrderByDescending(x=>x.RecordedUtc).ToListAsync(ct),options.OrderByDescending(x=>x.Suggested).ThenBy(x=>x.Due).ToList(),sources,
            await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==account&&sourceIds.Contains(x.InvoiceId)&&!x.Reversed).ToListAsync(ct),
            invoice.Status==LeasingInvoiceStatus.Review&&(admin||invoice.UploadedByUserId==user),await authorization.CanApproveLeasingInvoicesAsync(ct));
    }
    private static LeasingFinancingSnapshot TermFinancing(LeasingAcquisition a,LeasingPlanTerm term,List<LeasingFinancingRevision> revisions)
    {
        var revision=term.FinancingRevisionId.HasValue?revisions.SingleOrDefault(x=>x.Id==term.FinancingRevisionId):FinancingAt(revisions,term.PeriodFrom);
        return revision==null?LeasingFinancingSnapshot.From(a):JsonSerializer.Deserialize<LeasingFinancingSnapshot>(revision.SnapshotJson)!;
    }
    public async Task SaveRentalReviewAsync(Guid account,Guid id,Guid revision,RentalInvoiceReview review,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var i=await RentalInvoices(db,account,user,admin).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();
        CheckRevision(i.Revision,revision,true);if(i.Status!=LeasingInvoiceStatus.Review)throw new LeasingValidationException("InvoiceImmutable");
        if(review.FinanceOrganizationId!=Guid.Empty&&!await db.Organizations.AnyAsync(x=>x.AccountId==account&&x.Id==review.FinanceOrganizationId,ct))throw new UnauthorizedAccessException();
        if(review.Reason.Length>2000||review.AgreementReference.Length>200||review.PeriodReference.Length>200||review.Data.Lines.Count>500||review.Allocations.Count>1200||Snapshot(review).Length>2_000_000)throw new LeasingValidationException("PaymentInvalid");
        if(review.OriginalInvoiceId.HasValue&&!await RentalInvoices(db,account,user,await PaymentReader(admin,ct)).AnyAsync(x=>x.Id==review.OriginalInvoiceId,ct))throw new UnauthorizedAccessException();
        foreach(var part in review.Allocations.Where(x=>x.CreditedAllocationId.HasValue))
            if(!await db.LeasingPaymentAllocations.AnyAsync(x=>x.AccountId==account&&x.Id==part.CreditedAllocationId,ct))throw new UnauthorizedAccessException();
        var visible=Acquisitions(db,account,user,await PaymentReader(admin,ct)).Select(x=>x.Id);
        foreach(var part in review.Allocations)
            if(!await db.LeasingInstallments.AnyAsync(x=>x.AccountId==account&&x.Id==part.InstallmentId&&visible.Contains(x.AcquisitionId),ct))throw new UnauthorizedAccessException();
        var before=i.ReviewJson;i.ReviewJson=Snapshot(review);i.FinanceOrganizationId=review.FinanceOrganizationId==Guid.Empty?null:review.FinanceOrganizationId;i.Number=review.Data.Number;i.Currency=review.Data.Currency;i.ReviewedByUserId=user;i.ReviewedUtc=clock.GetUtcNow();i.Revision=Guid.NewGuid();
        await InvalidateLifecycleDocumentReview(db,i,ct);
        PaymentEvent(db,account,null,id,user,"RentalReviewed",review.Reason,before,i.ReviewJson,Guid.NewGuid());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task UseRentalInterpretationAsync(Guid account,Guid id,Guid revision,Guid interpretation,CancellationToken ct=default)
    {
        var d=await GetRentalInvoiceAsync(account,id,ct);var source=d.Interpretations.SingleOrDefault(x=>x.Id==interpretation)??throw new UnauthorizedAccessException();
        d.Review.Data=JsonSerializer.Deserialize<InvoiceData>(source.ResultJson)!;d.Review.ContentConfirmed=false;await SaveRentalReviewAsync(account,id,revision,d.Review,ct);
    }
    public async Task RetryRentalInterpretationAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var i=await RentalInvoices(db,account,user,admin).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();CheckRevision(i.Revision,revision,true);
        if(i.Status!=LeasingInvoiceStatus.Review||string.IsNullOrEmpty(i.StorageKey)||i.Processing is LeasingInvoiceProcessing.Uploaded or LeasingInvoiceProcessing.Processing)throw new LeasingValidationException("InvoiceProcessingBusy");
        i.Processing=LeasingInvoiceProcessing.Uploaded;i.Revision=Guid.NewGuid();PaymentEvent(db,account,null,id,user,"RentalInterpretationRequested","","{}","{}",Guid.NewGuid());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ApproveRentalInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        if(!await authorization.CanApproveLeasingInvoicesAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var i=await RentalInvoices(db,account,user,true).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();if(i.Status==LeasingInvoiceStatus.Approved)return;
        CheckRevision(i.Revision,revision,true);var r=RentalReview(i);var d=r.Data;
        if(i.Status!=LeasingInvoiceStatus.Review||i.Processing is LeasingInvoiceProcessing.Processing or LeasingInvoiceProcessing.Uploaded||!r.ContentConfirmed||!Enum.IsDefined(d.Kind)||string.IsNullOrWhiteSpace(d.Number)||d.Number.Length>100||d.Date==null||d.Date==DateOnly.MinValue||d.DueDate==DateOnly.MinValue||!AgreementPeriodCalculator.Currencies.Contains(d.Currency))throw new LeasingValidationException("InvoiceReviewRequired");
        var amounts=InvoiceCalculator.Calculate(d);if(amounts.Errors.Count>0)throw new LeasingValidationException(amounts.Errors[0]);
        if(!await db.Organizations.AnyAsync(x=>x.AccountId==account&&x.Id==r.FinanceOrganizationId,ct))throw new LeasingValidationException("LeasingInvalidParty");
        var party=await db.Organizations.AsNoTracking().SingleAsync(x=>x.AccountId==account&&x.Id==r.FinanceOrganizationId,ct);
        if(!string.IsNullOrWhiteSpace(party.OrganizationNumber)&&Identity(d)!=new string(party.OrganizationNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant())throw new LeasingValidationException("RentalSupplierMismatch");
        var number=InvoiceNumber(d);
        if(await db.LeasingInvoices.AnyAsync(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&x.Status==LeasingInvoiceStatus.Approved&&x.Id!=id&&(i.FileHash!=""&&x.FileHash==i.FileHash||x.FinanceOrganizationId==r.FinanceOrganizationId&&x.Kind==d.Kind&&x.Number==number),ct))throw new LeasingValidationException("InvoiceCertainDuplicate");
        if(d.Kind==LeasingInvoiceKind.CreditNote)
        {
            var original=await db.LeasingInvoices.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==r.OriginalInvoiceId&&x.Category==LeasingInvoiceCategory.Rental&&x.Status==LeasingInvoiceStatus.Approved&&x.Kind==LeasingInvoiceKind.Invoice&&x.FinanceOrganizationId==r.FinanceOrganizationId&&x.Currency==d.Currency,ct)??throw new LeasingValidationException("PaymentCreditSourceRequired");
            var credits=await db.LeasingInvoices.Where(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&x.OriginalInvoiceId==original.Id&&x.Status==LeasingInvoiceStatus.Approved).ToListAsync(ct);
            if(credits.Sum(x=>x.Net)+amounts.Net>original.Net||credits.Sum(x=>x.Vat)+amounts.Vat>original.Vat)throw new LeasingValidationException("InvoiceOverCredit");
        }
        else if(r.OriginalInvoiceId.HasValue)throw new LeasingValidationException("PaymentInvalid");
        i.Kind=d.Kind;i.FinanceOrganizationId=r.FinanceOrganizationId;i.OriginalInvoiceId=r.OriginalInvoiceId;i.Number=number;i.InvoiceDate=d.Date;i.Currency=d.Currency;i.Net=amounts.Net;i.Vat=amounts.Vat;i.SupplierIdentity=Identity(d);
        i.ApprovedByUserId=user;i.ApprovedUtc=clock.GetUtcNow();i.Status=LeasingInvoiceStatus.Approved;i.ApprovedJson=i.ReviewJson;i.Revision=Guid.NewGuid();
        await ApplyRentalAllocations(db,i,r.Allocations,user,ct);
        PaymentEvent(db,account,null,id,user,"RentalApproved",r.Reason,"{}",i.ApprovedJson,Guid.NewGuid());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task ApplyRentalAllocations(TenantPlatformDbContext db,LeasingInvoice invoice,List<RentalAllocationInput> inputs,Guid user,CancellationToken ct)
    {
        if(inputs.Count>1200||inputs.Any(x=>!PaymentMoney(x.Net)||!PaymentMoney(x.Vat)||x.Net+x.Vat<=0)||inputs.Sum(x=>x.Net)>invoice.Net||inputs.Sum(x=>x.Vat)>invoice.Vat)throw new LeasingValidationException("PaymentOverAllocation");
        var pendingCredits=new Dictionary<Guid,(decimal Net,decimal Vat)>();
        foreach(var input in inputs)
        {
            var obligation=await db.LeasingInstallments.SingleOrDefaultAsync(x=>x.AccountId==invoice.AccountId&&x.Id==input.InstallmentId,ct)??throw new UnauthorizedAccessException();
            var a=await db.LeasingAcquisitions.SingleAsync(x=>x.AccountId==invoice.AccountId&&x.Id==obligation.AcquisitionId,ct);
            var term=await db.LeasingPlanTerms.SingleOrDefaultAsync(x=>x.AccountId==invoice.AccountId&&x.InstallmentId==obligation.Id&&db.LeasingPaymentPlans.Any(p=>p.AccountId==invoice.AccountId&&p.Id==x.PlanId&&p.Status==LeasingPlanStatus.Active),ct)??throw new LeasingValidationException("PaymentActiveRequired");
            var revisions=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==invoice.AccountId&&x.AcquisitionId==a.Id).ToListAsync(ct);var financing=TermFinancing(a,term,revisions);
            if(a.Currency!=invoice.Currency||financing.Currency!=invoice.Currency||financing.FinanceOrganizationId!=invoice.FinanceOrganizationId)throw new LeasingValidationException("PaymentMatchPartyCurrency");
            if(invoice.Kind==LeasingInvoiceKind.CreditNote)
            {
                var original=await db.LeasingPaymentAllocations.SingleOrDefaultAsync(x=>x.AccountId==invoice.AccountId&&x.Id==input.CreditedAllocationId&&!x.Reversed&&x.InvoiceId==invoice.OriginalInvoiceId&&x.InstallmentId==obligation.Id,ct)??throw new LeasingValidationException("PaymentCreditSourceRequired");
                var credits=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==invoice.AccountId&&x.CreditedAllocationId==original.Id&&!x.Reversed).ToListAsync(ct);var pending=pendingCredits.GetValueOrDefault(original.Id);
                if(credits.Sum(x=>x.Net)+pending.Net+input.Net>original.Net||credits.Sum(x=>x.Vat)+pending.Vat+input.Vat>original.Vat)throw new LeasingValidationException("InvoiceOverCredit");pendingCredits[original.Id]=(pending.Net+input.Net,pending.Vat+input.Vat);
            }
            else if(input.CreditedAllocationId.HasValue)throw new LeasingValidationException("PaymentInvalid");
            db.LeasingPaymentAllocations.Add(new(){Id=Guid.NewGuid(),AccountId=invoice.AccountId,InvoiceId=invoice.Id,InstallmentId=obligation.Id,CreditedAllocationId=input.CreditedAllocationId,Net=input.Net,Vat=input.Vat,ActorUserId=user,RecordedUtc=clock.GetUtcNow()});
            obligation.BillingComplete=false;obligation.VarianceAccepted=false;obligation.ControlReason=null;obligation.Revision=Guid.NewGuid();
            PaymentEvent(db,invoice.AccountId,a.Id,invoice.Id,user,"RentalMatched","","{}",Snapshot(input),Guid.NewGuid());
        }
        await InvalidateLifecycleDocumentReview(db,invoice,ct);
    }
    public async Task CorrectRentalAllocationsAsync(Guid account,Guid id,Guid revision,List<RentalAllocationInput> inputs,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanApproveLeasingInvoicesAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var i=await RentalInvoices(db,account,user,true).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();if(await PaymentRetry(db,account,request,user,"RentalRematched",null,id,ct))return;
        CheckRevision(i.Revision,revision,true);Reason(reason,true);if(i.Status!=LeasingInvoiceStatus.Approved)throw new LeasingValidationException("InvoiceImmutable");
        var old=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account&&x.InvoiceId==id&&!x.Reversed).ToListAsync(ct);var before=Snapshot(old);await ReverseRentalAllocations(db,i,old,user,ct);await db.SaveChangesAsync(ct);
        await ApplyRentalAllocations(db,i,inputs,user,ct);i.Revision=Guid.NewGuid();
        PaymentEvent(db,account,null,id,user,"RentalRematched",reason,before,Snapshot(inputs),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private async Task ReverseRentalAllocations(TenantPlatformDbContext db,LeasingInvoice i,List<LeasingPaymentAllocation> allocations,Guid user,CancellationToken ct)
    {
        await InvalidateLifecycleDocumentReview(db,i,ct);
        var ids=allocations.Select(x=>x.Id).ToArray();if(await db.LeasingPaymentAllocations.AnyAsync(x=>x.AccountId==i.AccountId&&x.CreditedAllocationId.HasValue&&ids.Contains(x.CreditedAllocationId.Value)&&!x.Reversed,ct))throw new LeasingValidationException("InvoiceReverseDependencies");
        foreach(var link in allocations)
        {
            link.Reversed=true;var obligation=await db.LeasingInstallments.SingleAsync(x=>x.AccountId==i.AccountId&&x.Id==link.InstallmentId,ct);obligation.BillingComplete=false;obligation.VarianceAccepted=false;obligation.ControlReason=null;obligation.Revision=Guid.NewGuid();
            PaymentEvent(db,i.AccountId,obligation.AcquisitionId,i.Id,user,"RentalAllocationReversed","",Snapshot(link),"{}",Guid.NewGuid());
        }
    }
    public async Task ReverseRentalInvoiceAsync(Guid account,Guid id,Guid revision,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanApproveLeasingInvoicesAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var i=await RentalInvoices(db,account,user,true).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();if(await PaymentRetry(db,account,request,user,"RentalReversed",null,id,ct))return;if(i.Status==LeasingInvoiceStatus.Reversed)return;
        CheckRevision(i.Revision,revision,true);Reason(reason,true);if(i.Status!=LeasingInvoiceStatus.Approved)throw new LeasingValidationException("InvoiceImmutable");
        if(await db.LeasingInvoices.AnyAsync(x=>x.AccountId==account&&x.Category==LeasingInvoiceCategory.Rental&&x.OriginalInvoiceId==id&&x.Status==LeasingInvoiceStatus.Approved,ct))throw new LeasingValidationException("InvoiceReverseDependencies");
        var old=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==account&&x.InvoiceId==id&&!x.Reversed).ToListAsync(ct);await ReverseRentalAllocations(db,i,old,user,ct);
        i.Status=LeasingInvoiceStatus.Reversed;i.Revision=Guid.NewGuid();PaymentEvent(db,account,null,id,user,"RentalReversed",reason,i.ApprovedJson??"{}",Snapshot(old),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<AgreementDownload> DownloadRentalInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var i=await RentalInvoices(db,account,user,await PaymentReader(admin,ct)).AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();
        if(string.IsNullOrWhiteSpace(i.StorageKey))throw new UnauthorizedAccessException();return new(await storage.OpenReadAsync(i.StorageKey,ct),i.FileName,i.MediaType);
    }
}
