using System.Text.Json;
using System.Security.Cryptography;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Leasing.Invoices;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingRentalInvoiceService(IAuthorizationService authorization,ILeasingRentalInvoiceData store,IAgreementDocumentStorage storage,TimeProvider clock)
{
    private Task<AccountActorScope> Actor(Guid account,CancellationToken ct)=>authorization.GetAccountActorScopeAsync(account,ct);
    private async Task<AccountActorScope> Required(Guid account,CancellationToken ct){var actor=await Actor(account,ct);if(!LeasingUserAccess.Has(actor,Permissions.Leasing.ApproveInvoices))throw new UnauthorizedAccessException();return actor;}
    private async Task<AccountActorScope> Revalidate(AccountActorScope actor,string? permission,CancellationToken ct)
    {
        var now=await Actor(actor.AccountId,ct);if(now.UserId!=actor.UserId||permission is not null&&!LeasingUserAccess.Has(now,permission))throw new UnauthorizedAccessException();return now;
    }
    private static bool Owns(AccountActorScope actor,LeasingRentalInvoiceState graph,LeasingAcquisition a)=>LeasingReader.Scope(actor).Administrator||a.OwnerUserId==actor.UserId||graph.Frameworks.Any(f=>f.Id==a.FrameworkId&&f.OwnerUserId==actor.UserId);
    private static IEnumerable<LeasingAcquisition> VisibleAcquisitions(LeasingRentalInvoiceState graph,AccountActorScope actor,bool reader)=>graph.Acquisitions.Where(x=>reader||Owns(actor,graph,x));
    private static IEnumerable<LeasingInvoice> VisibleInvoices(LeasingRentalInvoiceState graph,AccountActorScope actor,bool reader)=>graph.AccountInvoices.Where(x=>reader||x.UploadedByUserId==actor.UserId);
    private static LeasingInvoice Authorized(LeasingRentalInvoiceState graph,AccountActorScope actor,bool reader)
    {
        var i=graph.Invoice??throw new UnauthorizedAccessException();if(!reader&&!LeasingReader.Scope(actor).Administrator&&i.UploadedByUserId!=actor.UserId)throw new UnauthorizedAccessException();return i;
    }
    private static T Copy<T>(T input)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(input))!;
    private static string Snapshot<T>(T value)=>JsonSerializer.Serialize(value);
    private static RentalInvoiceReview RentalReview(LeasingInvoice invoice)=>JsonSerializer.Deserialize<RentalInvoiceReview>(invoice.ReviewJson)??new();
    private static void CheckRevision(Guid actual,Guid supplied,bool existing){if(existing&&actual!=supplied)throw new LeasingValidationException("LeasingConcurrency");}
    private static void Reason(string reason,bool required){if(reason.Length>2000||required&&string.IsNullOrWhiteSpace(reason))throw new LeasingValidationException("LeasingReasonRequired");}
    private static bool PaymentMoney(decimal? value)=>!value.HasValue||value>=0&&value<=1000000000000m&&decimal.Round(value.Value,2)==value.Value;
    private void PaymentEvent(List<LeasingPaymentEvent> history,Guid account,Guid? acquisition,Guid? invoice,Guid user,string action,string reason,string before,string after,Guid request)=>history.Add(new(){Id=request,AccountId=account,AcquisitionId=acquisition,InvoiceId=invoice,ActorUserId=user,Action=action,Reason=reason,BeforeJson=before,AfterJson=after,RecordedUtc=clock.GetUtcNow()});
    private static void Invalidate(LeasingRentalInvoiceState graph,LeasingInvoice invoice)=>LeasingLifecycleRules.InvalidateDocumentReview(invoice,graph.Financing,graph.Acquisitions,graph.Lifecycles);
    private static bool Retry(LeasingRentalInvoiceState graph,Guid request,Guid user,string action,Guid? acquisition,Guid? invoice)
    {
        if(request==Guid.Empty)throw new LeasingValidationException("PaymentInvalid");if(graph.PreviousRequest is not {} e)return false;
        if(e.ActorUserId!=user||e.Action!=action||e.AcquisitionId!=acquisition||e.InvoiceId!=invoice)throw new LeasingValidationException("LeasingConcurrency");return true;
    }
    private static LeasingInvoice Public(LeasingInvoice invoice){invoice=Copy(invoice);invoice.StorageKey="";invoice.ProcessingToken=null;invoice.ProcessingError=null;return invoice;}
    private static bool Register(AccountActorScope actor,LeasingRentalInvoiceState graph)=>LeasingReader.Scope(actor).Administrator||VisibleAcquisitions(graph,actor,false).Any();
    private static bool Upload(AccountActorScope actor,LeasingRentalInvoiceState graph)=>Register(actor,graph)||graph.Frameworks.Any(f=>f.OwnerUserId==actor.UserId)||graph.Orders.Any(o=>o.OwnerUserId==actor.UserId||graph.Frameworks.Any(f=>f.Id==o.FrameworkId&&f.OwnerUserId==actor.UserId));
    public async Task<bool> CanRegisterRentalInvoiceAsync(Guid account,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,null,null,false,ct);actor=await Revalidate(actor,null,ct);return Register(actor,work.State);
    }
    public async Task<Guid> NewRentalInvoiceAsync(Guid account,Guid request,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,request,null,true,ct);actor=await Revalidate(actor,null,ct);var graph=work.State;
        if(!Register(actor,graph))throw new UnauthorizedAccessException();if(graph.Invoice is not null){Authorized(graph,actor,false);return request;}
        if(request==Guid.Empty)throw new LeasingValidationException("PaymentInvalid");
        var invoice=new LeasingInvoice{Id=request,AccountId=account,Category=LeasingInvoiceCategory.Rental,Status=LeasingInvoiceStatus.Review,Processing=LeasingInvoiceProcessing.Ready,Revision=Guid.NewGuid(),UploadedByUserId=actor.UserId,UploadedUtc=clock.GetUtcNow(),ReviewJson=Snapshot(new RentalInvoiceReview())};graph.Invoice=invoice;
        var history=new List<LeasingPaymentEvent>();PaymentEvent(history,account,null,request,actor.UserId,"RentalCreated","","{}",invoice.ReviewJson,request);await work.CommitAsync(history,null,ct);return request;
    }
    public async Task DeleteRentalInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,null,true,ct);actor=await Revalidate(actor,null,ct);var invoice=Authorized(work.State,actor,false);
        if(invoice.Status!=LeasingInvoiceStatus.Review)throw new LeasingValidationException("RentalDeleteOnlyDraft");CheckRevision(invoice.Revision,revision,true);
        var history=new List<LeasingPaymentEvent>();PaymentEvent(history,account,null,null,actor.UserId,"RentalDeleted",id.ToString(),invoice.ReviewJson,"{}",Guid.NewGuid());await work.DeleteDraftAsync(history.Single(),ct);
        if(!string.IsNullOrEmpty(invoice.StorageKey))await store.DeleteFileAsync(id,invoice.StorageKey);
    }
    public async Task<Guid> UploadRentalInvoiceAsync(Guid account,string filename,Stream stream,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);
        await using(var before=await store.OpenAsync(account,null,null,false,ct)){actor=await Revalidate(actor,null,ct);if(!Upload(actor,before.State))throw new UnauthorizedAccessException();}
        if(Path.GetExtension(filename).ToLowerInvariant() is not (".xml" or ".pdf" or ".png" or ".jpg" or ".jpeg"))throw new AgreementFileException("AgreementInvalidFileType");
        var stored=await storage.StoreAsync(stream,filename,ct);
        try
        {
            await using var original=await storage.OpenReadAsync(stored.StorageKey,ct);var hash=Convert.ToHexString(await SHA256.HashDataAsync(original,ct));
            await using var work=await store.OpenAsync(account,null,null,true,ct);actor=await Revalidate(actor,null,ct);var graph=work.State;if(!Upload(actor,graph))throw new UnauthorizedAccessException();
            var duplicate=VisibleInvoices(graph,actor,LeasingReader.Scope(actor).PaymentReader).FirstOrDefault(x=>x.FileHash==hash&&x.Status is not(LeasingInvoiceStatus.Reversed or LeasingInvoiceStatus.Rejected));
            if(duplicate is not null){await storage.DiscardUncommittedAsync(stored.StorageKey);return duplicate.Id;}
            var invoice=new LeasingInvoice{Id=Guid.NewGuid(),AccountId=account,Category=LeasingInvoiceCategory.Rental,UploadedByUserId=actor.UserId,UploadedUtc=clock.GetUtcNow(),FileName=stored.FileName,StorageKey=stored.StorageKey,MediaType=stored.MediaType,Size=stored.Size,FileHash=hash,Revision=Guid.NewGuid()};graph.Invoice=invoice;
            var history=new LeasingInvoiceHistory{Id=Guid.NewGuid(),AccountId=account,InvoiceId=invoice.Id,ActorUserId=actor.UserId,Action="Uploaded",RecordedUtc=clock.GetUtcNow()};await work.CommitAsync([],history,ct);return invoice.Id;
        }
        catch{await store.CleanupUncommittedAsync(account,stored.StorageKey);throw;}
    }
    public async Task<List<RentalInvoiceRow>> ListRentalInvoicesAsync(Guid account,string? search=null,string? currency=null,Guid? finance=null,LeasingInvoiceStatus? status=null,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,null,null,false,ct);actor=await Revalidate(actor,null,ct);var graph=work.State;
        var rows=VisibleInvoices(graph,actor,LeasingReader.Scope(actor).PaymentReader);
        if(!string.IsNullOrWhiteSpace(search))rows=rows.Where(x=>x.Number.Contains(search,StringComparison.OrdinalIgnoreCase)||x.FileName.Contains(search,StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrEmpty(currency))rows=rows.Where(x=>x.Currency==currency);if(finance.HasValue)rows=rows.Where(x=>x.FinanceOrganizationId==finance);if(status.HasValue)rows=rows.Where(x=>x.Status==status);
        return rows.Select(i=>{var links=graph.Allocations.Where(x=>x.InvoiceId==i.Id&&!x.Reversed).ToList();return new RentalInvoiceRow(Public(i),graph.Organizations.SingleOrDefault(x=>x.Id==i.FinanceOrganizationId)?.Name??"",links.Sum(x=>x.Net),links.Sum(x=>x.Vat),links.Select(x=>graph.Acquisitions.Single(a=>a.Id==graph.Installments.Single(t=>t.Id==x.InstallmentId).AcquisitionId).Name).Distinct().ToList());}).ToList();
    }
    public async Task<RentalInvoiceDetails> GetRentalInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,null,false,ct);actor=await Revalidate(actor,null,ct);var graph=work.State;var scope=LeasingReader.Scope(actor);var invoice=Authorized(graph,actor,scope.PaymentReader);var review=RentalReview(invoice);var options=new List<RentalMatchOption>();
        foreach(var a in VisibleAcquisitions(graph,actor,scope.PaymentReader).Where(x=>x.Status==LeasingAcquisitionStatus.Registered&&x.Currency==review.Data.Currency))
        {
            foreach(var t in graph.Plans.Where(x=>x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active).SelectMany(x=>x.Terms))
            {
                var financing=TermFinancing(a,t,graph.Financing.Where(x=>x.AcquisitionId==a.Id).ToList());if(financing.FinanceOrganizationId!=review.FinanceOrganizationId)continue;
                var reference=financing.Terms.FinanceReference??"";var suggested=reference!=""&&(reference==review.AgreementReference||reference==review.Data.OrderReference);options.Add(new(t.InstallmentId,a.Id,a.Name,t.Reference,t.PeriodFrom,t.PeriodTo,t.DueDate,t.Net,t.Vat,a.Currency,reference,suggested));
            }
        }
        var sources=VisibleInvoices(graph,actor,scope.PaymentReader).Where(x=>x.Status==LeasingInvoiceStatus.Approved&&x.Kind==LeasingInvoiceKind.Invoice&&x.Currency==review.Data.Currency&&x.FinanceOrganizationId==review.FinanceOrganizationId).ToList();var ids=sources.Select(x=>x.Id).ToHashSet();
        return new(Public(invoice),review,graph.Interpretations,graph.Allocations.Where(x=>x.InvoiceId==id).ToList(),graph.History,options.OrderByDescending(x=>x.Suggested).ThenBy(x=>x.Due).ToList(),sources.Select(Public).ToList(),graph.Allocations.Where(x=>ids.Contains(x.InvoiceId)&&!x.Reversed).ToList(),invoice.Status==LeasingInvoiceStatus.Review&&(scope.Administrator||invoice.UploadedByUserId==actor.UserId),LeasingUserAccess.Has(actor,Permissions.Leasing.ApproveInvoices));
    }
    public async Task<AgreementDownload> DownloadRentalInvoiceAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,null,false,ct);actor=await Revalidate(actor,null,ct);var i=Authorized(work.State,actor,LeasingReader.Scope(actor).PaymentReader);
        if(string.IsNullOrWhiteSpace(i.StorageKey))throw new UnauthorizedAccessException();return new(await storage.OpenReadAsync(i.StorageKey,ct),i.FileName,i.MediaType);
    }
    public static LeasingFinancingSnapshot TermFinancing(LeasingAcquisition a,LeasingPlanTerm term,List<LeasingFinancingRevision> revisions)
    {
        var revision=term.FinancingRevisionId.HasValue?revisions.SingleOrDefault(x=>x.Id==term.FinancingRevisionId):LeasingPaymentService.FinancingAt(revisions,term.PeriodFrom);
        return revision==null?LeasingFinancingSnapshot.From(a):JsonSerializer.Deserialize<LeasingFinancingSnapshot>(revision.SnapshotJson)!;
    }
    public async Task SaveRentalReviewAsync(Guid account,Guid id,Guid revision,RentalInvoiceReview review,CancellationToken ct=default)
    {
        review=Copy(review);var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,null,true,ct);actor=await Revalidate(actor,null,ct);var user=actor.UserId;var admin=LeasingReader.Scope(actor).Administrator;var graph=work.State;var history=new List<LeasingPaymentEvent>();
        var i=Authorized(graph,actor,false);
        CheckRevision(i.Revision,revision,true);if(i.Status!=LeasingInvoiceStatus.Review)throw new LeasingValidationException("InvoiceImmutable");
        if(review.FinanceOrganizationId!=Guid.Empty&&!graph.Organizations.Any(x=>x.Id==review.FinanceOrganizationId))throw new UnauthorizedAccessException();
        if(review.Reason.Length>2000||review.AgreementReference.Length>200||review.PeriodReference.Length>200||review.Data.Lines.Count>500||review.Allocations.Count>1200||Snapshot(review).Length>2_000_000)throw new LeasingValidationException("PaymentInvalid");
        if(review.OriginalInvoiceId.HasValue&&!VisibleInvoices(graph,actor,LeasingReader.Scope(actor).PaymentReader).Any(x=>x.Id==review.OriginalInvoiceId))throw new UnauthorizedAccessException();
        foreach(var part in review.Allocations.Where(x=>x.CreditedAllocationId.HasValue))
            if(!graph.Allocations.Any(x=>x.Id==part.CreditedAllocationId))throw new UnauthorizedAccessException();
        var visible=VisibleAcquisitions(graph,actor,LeasingReader.Scope(actor).PaymentReader).Select(x=>x.Id).ToHashSet();
        foreach(var part in review.Allocations)
            if(!graph.Installments.Any(x=>x.Id==part.InstallmentId&&visible.Contains(x.AcquisitionId)))throw new UnauthorizedAccessException();
        var before=i.ReviewJson;i.ReviewJson=Snapshot(review);i.FinanceOrganizationId=review.FinanceOrganizationId==Guid.Empty?null:review.FinanceOrganizationId;i.Number=review.Data.Number;i.Currency=review.Data.Currency;i.ReviewedByUserId=user;i.ReviewedUtc=clock.GetUtcNow();i.Revision=Guid.NewGuid();
        Invalidate(graph,i);
        PaymentEvent(history,account,null,id,user,"RentalReviewed",review.Reason,before,i.ReviewJson,Guid.NewGuid());await work.CommitAsync(history,null,ct);
    }
    public async Task UseRentalInterpretationAsync(Guid account,Guid id,Guid revision,Guid interpretation,CancellationToken ct=default)
    {
        var d=await GetRentalInvoiceAsync(account,id,ct);var source=d.Interpretations.SingleOrDefault(x=>x.Id==interpretation)??throw new UnauthorizedAccessException();
        d.Review.Data=JsonSerializer.Deserialize<InvoiceData>(source.ResultJson)!;d.Review.ContentConfirmed=false;await SaveRentalReviewAsync(account,id,revision,d.Review,ct);
    }
    public async Task RetryRentalInterpretationAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await store.OpenAsync(account,id,null,true,ct);actor=await Revalidate(actor,null,ct);var user=actor.UserId;var admin=LeasingReader.Scope(actor).Administrator;var graph=work.State;var history=new List<LeasingPaymentEvent>();
        var i=Authorized(graph,actor,false);CheckRevision(i.Revision,revision,true);
        if(i.Status!=LeasingInvoiceStatus.Review||string.IsNullOrEmpty(i.StorageKey)||i.Processing is LeasingInvoiceProcessing.Uploaded or LeasingInvoiceProcessing.Processing)throw new LeasingValidationException("InvoiceProcessingBusy");
        i.Processing=LeasingInvoiceProcessing.Uploaded;i.Revision=Guid.NewGuid();PaymentEvent(history,account,null,id,user,"RentalInterpretationRequested","","{}","{}",Guid.NewGuid());await work.CommitAsync(history,null,ct);
    }
    public async Task ApproveRentalInvoiceAsync(Guid account,Guid id,Guid revision,CancellationToken ct=default)
    {
        var actor=await Required(account,ct);await using var work=await store.OpenAsync(account,id,null,true,ct);actor=await Revalidate(actor,Permissions.Leasing.ApproveInvoices,ct);var user=actor.UserId;var graph=work.State;var history=new List<LeasingPaymentEvent>();
        var i=Authorized(graph,actor,true);if(i.Status==LeasingInvoiceStatus.Approved)return;
        CheckRevision(i.Revision,revision,true);var r=RentalReview(i);var d=r.Data;
        if(i.Status!=LeasingInvoiceStatus.Review||i.Processing is LeasingInvoiceProcessing.Processing or LeasingInvoiceProcessing.Uploaded||!r.ContentConfirmed||!Enum.IsDefined(d.Kind)||string.IsNullOrWhiteSpace(d.Number)||d.Number.Length>100||d.Date==null||d.Date==DateOnly.MinValue||d.DueDate==DateOnly.MinValue||!AgreementPeriodCalculator.Currencies.Contains(d.Currency))throw new LeasingValidationException("InvoiceReviewRequired");
        var amounts=InvoiceCalculator.Calculate(d);if(amounts.Errors.Count>0)throw new LeasingValidationException(amounts.Errors[0]);
        if(!graph.Organizations.Any(x=>x.Id==r.FinanceOrganizationId))throw new LeasingValidationException("LeasingInvalidParty");
        var party=graph.Organizations.Single(x=>x.Id==r.FinanceOrganizationId);
        if(!string.IsNullOrWhiteSpace(party.OrganizationNumber)&&LeasingPurchaseInvoiceService.Identity(d)!=new string(party.OrganizationNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant())throw new LeasingValidationException("RentalSupplierMismatch");
        var number=LeasingPurchaseInvoiceService.InvoiceNumber(d);
        if(graph.AccountInvoices.Any(x=>x.Status==LeasingInvoiceStatus.Approved&&x.Id!=id&&(i.FileHash!=""&&x.FileHash==i.FileHash||x.FinanceOrganizationId==r.FinanceOrganizationId&&x.Kind==d.Kind&&x.Number==number)))throw new LeasingValidationException("InvoiceCertainDuplicate");
        if(d.Kind==LeasingInvoiceKind.CreditNote)
        {
            var original=graph.AccountInvoices.SingleOrDefault(x=>x.Id==r.OriginalInvoiceId&&x.Status==LeasingInvoiceStatus.Approved&&x.Kind==LeasingInvoiceKind.Invoice&&x.FinanceOrganizationId==r.FinanceOrganizationId&&x.Currency==d.Currency)??throw new LeasingValidationException("PaymentCreditSourceRequired");
            var credits=graph.AccountInvoices.Where(x=>x.OriginalInvoiceId==original.Id&&x.Status==LeasingInvoiceStatus.Approved).ToList();
            if(credits.Sum(x=>x.Net)+amounts.Net>original.Net||credits.Sum(x=>x.Vat)+amounts.Vat>original.Vat)throw new LeasingValidationException("InvoiceOverCredit");
        }
        else if(r.OriginalInvoiceId.HasValue)throw new LeasingValidationException("PaymentInvalid");
        i.Kind=d.Kind;i.FinanceOrganizationId=r.FinanceOrganizationId;i.OriginalInvoiceId=r.OriginalInvoiceId;i.Number=number;i.InvoiceDate=d.Date;i.Currency=d.Currency;i.Net=amounts.Net;i.Vat=amounts.Vat;i.SupplierIdentity=LeasingPurchaseInvoiceService.Identity(d);
        i.ApprovedByUserId=user;i.ApprovedUtc=clock.GetUtcNow();i.Status=LeasingInvoiceStatus.Approved;i.ApprovedJson=i.ReviewJson;i.Revision=Guid.NewGuid();
        ApplyRentalAllocations(graph,history,i,r.Allocations,user);
        PaymentEvent(history,account,null,id,user,"RentalApproved",r.Reason,"{}",i.ApprovedJson,Guid.NewGuid());await work.CommitAsync(history,null,ct);
    }
    private void ApplyRentalAllocations(LeasingRentalInvoiceState graph,List<LeasingPaymentEvent> history,LeasingInvoice invoice,List<RentalAllocationInput> inputs,Guid user)
    {
        if(inputs.Count>1200||inputs.Any(x=>!PaymentMoney(x.Net)||!PaymentMoney(x.Vat)||x.Net+x.Vat<=0)||inputs.Sum(x=>x.Net)>invoice.Net||inputs.Sum(x=>x.Vat)>invoice.Vat)throw new LeasingValidationException("PaymentOverAllocation");
        var pendingCredits=new Dictionary<Guid,(decimal Net,decimal Vat)>();var existing=graph.Allocations.ToList();
        foreach(var input in inputs)
        {
            var obligation=graph.Installments.SingleOrDefault(x=>x.Id==input.InstallmentId)??throw new UnauthorizedAccessException();
            var a=graph.Acquisitions.Single(x=>x.Id==obligation.AcquisitionId);
            var term=graph.Plans.Where(p=>p.Status==LeasingPlanStatus.Active).SelectMany(p=>p.Terms).SingleOrDefault(x=>x.InstallmentId==obligation.Id)??throw new LeasingValidationException("PaymentActiveRequired");
            var revisions=graph.Financing.Where(x=>x.AcquisitionId==a.Id).ToList();var financing=TermFinancing(a,term,revisions);
            if(a.Currency!=invoice.Currency||financing.Currency!=invoice.Currency||financing.FinanceOrganizationId!=invoice.FinanceOrganizationId)throw new LeasingValidationException("PaymentMatchPartyCurrency");
            if(invoice.Kind==LeasingInvoiceKind.CreditNote)
            {
                var original=existing.SingleOrDefault(x=>x.Id==input.CreditedAllocationId&&!x.Reversed&&x.InvoiceId==invoice.OriginalInvoiceId&&x.InstallmentId==obligation.Id)??throw new LeasingValidationException("PaymentCreditSourceRequired");
                var credits=existing.Where(x=>x.CreditedAllocationId==original.Id&&!x.Reversed).ToList();var pending=pendingCredits.GetValueOrDefault(original.Id);
                if(credits.Sum(x=>x.Net)+pending.Net+input.Net>original.Net||credits.Sum(x=>x.Vat)+pending.Vat+input.Vat>original.Vat)throw new LeasingValidationException("InvoiceOverCredit");pendingCredits[original.Id]=(pending.Net+input.Net,pending.Vat+input.Vat);
            }
            else if(input.CreditedAllocationId.HasValue)throw new LeasingValidationException("PaymentInvalid");
            graph.Allocations.Add(new(){Id=Guid.NewGuid(),AccountId=invoice.AccountId,InvoiceId=invoice.Id,InstallmentId=obligation.Id,CreditedAllocationId=input.CreditedAllocationId,Net=input.Net,Vat=input.Vat,ActorUserId=user,RecordedUtc=clock.GetUtcNow()});
            obligation.BillingComplete=false;obligation.VarianceAccepted=false;obligation.ControlReason=null;obligation.Revision=Guid.NewGuid();
            PaymentEvent(history,invoice.AccountId,a.Id,invoice.Id,user,"RentalMatched","","{}",Snapshot(input),Guid.NewGuid());
        }
        Invalidate(graph,invoice);
    }
    public async Task CorrectRentalAllocationsAsync(Guid account,Guid id,Guid revision,List<RentalAllocationInput> inputs,string reason,Guid request,CancellationToken ct=default)
    {
        inputs=Copy(inputs);
        var actor=await Required(account,ct);await using var work=await store.OpenAsync(account,id,request,true,ct);actor=await Revalidate(actor,Permissions.Leasing.ApproveInvoices,ct);var user=actor.UserId;var graph=work.State;var history=new List<LeasingPaymentEvent>();
        var i=Authorized(graph,actor,true);if(Retry(graph,request,user,"RentalRematched",null,id))return;
        CheckRevision(i.Revision,revision,true);Reason(reason,true);if(i.Status!=LeasingInvoiceStatus.Approved)throw new LeasingValidationException("InvoiceImmutable");
        var old=graph.Allocations.Where(x=>x.InvoiceId==id&&!x.Reversed).ToList();var before=Snapshot(old);ReverseRentalAllocations(graph,history,i,old,user);await work.FlushReversalAsync(ct);
        ApplyRentalAllocations(graph,history,i,inputs,user);i.Revision=Guid.NewGuid();
        PaymentEvent(history,account,null,id,user,"RentalRematched",reason,before,Snapshot(inputs),request);await work.CommitAsync(history,null,ct);
    }
    private void ReverseRentalAllocations(LeasingRentalInvoiceState graph,List<LeasingPaymentEvent> history,LeasingInvoice i,List<LeasingPaymentAllocation> allocations,Guid user)
    {
        Invalidate(graph,i);
        var ids=allocations.Select(x=>x.Id).ToArray();if(graph.Allocations.Any(x=>x.CreditedAllocationId.HasValue&&ids.Contains(x.CreditedAllocationId.Value)&&!x.Reversed))throw new LeasingValidationException("InvoiceReverseDependencies");
        foreach(var link in allocations)
        {
            link.Reversed=true;var obligation=graph.Installments.Single(x=>x.Id==link.InstallmentId);obligation.BillingComplete=false;obligation.VarianceAccepted=false;obligation.ControlReason=null;obligation.Revision=Guid.NewGuid();
            PaymentEvent(history,i.AccountId,obligation.AcquisitionId,i.Id,user,"RentalAllocationReversed","",Snapshot(link),"{}",Guid.NewGuid());
        }
    }
    public async Task ReverseRentalInvoiceAsync(Guid account,Guid id,Guid revision,string reason,Guid request,CancellationToken ct=default)
    {
        var actor=await Required(account,ct);await using var work=await store.OpenAsync(account,id,request,true,ct);actor=await Revalidate(actor,Permissions.Leasing.ApproveInvoices,ct);var user=actor.UserId;var graph=work.State;var history=new List<LeasingPaymentEvent>();
        var i=Authorized(graph,actor,true);if(Retry(graph,request,user,"RentalReversed",null,id))return;if(i.Status==LeasingInvoiceStatus.Reversed)return;
        CheckRevision(i.Revision,revision,true);Reason(reason,true);if(i.Status!=LeasingInvoiceStatus.Approved)throw new LeasingValidationException("InvoiceImmutable");
        if(graph.AccountInvoices.Any(x=>x.OriginalInvoiceId==id&&x.Status==LeasingInvoiceStatus.Approved))throw new LeasingValidationException("InvoiceReverseDependencies");
        var old=graph.Allocations.Where(x=>x.InvoiceId==id&&!x.Reversed).ToList();ReverseRentalAllocations(graph,history,i,old,user);
        i.Status=LeasingInvoiceStatus.Reversed;i.Revision=Guid.NewGuid();PaymentEvent(history,account,null,id,user,"RentalReversed",reason,i.ApprovedJson??"{}",Snapshot(old),request);await work.CommitAsync(history,null,ct);
    }
}
