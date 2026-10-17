using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingPaymentService(IAuthorizationService authorization,ILeasingPaymentData data,TimeProvider clock)
{
    private Task<AccountActorScope> Actor(Guid account,CancellationToken ct)=>authorization.GetAccountActorScopeAsync(account,ct);
    private async Task<AccountActorScope> Required(Guid account,string permission,CancellationToken ct){var actor=await Actor(account,ct);if(!LeasingUserAccess.Has(actor,permission))throw new UnauthorizedAccessException();return actor;}
    private static bool Owns(AccountActorScope actor,LeasingPaymentState state)=>LeasingUserAccess.Create(actor).IsAdministrator||state.Acquisition?.OwnerUserId==actor.UserId||state.Framework?.OwnerUserId==actor.UserId;
    private async Task<AccountActorScope> Revalidate(AccountActorScope before,ILeasingPaymentWork work,bool write,string? permission,CancellationToken ct)
    {
        var actor=permission is null?await Actor(before.AccountId,ct):await Required(before.AccountId,permission,ct);var state=work.State;
        if(actor.UserId!=before.UserId||state.Acquisition is null||permission is null&&(write?!Owns(actor,state):!Owns(actor,state)&&!LeasingReader.Scope(actor).PaymentReader))throw new UnauthorizedAccessException();return actor;
    }
    private static T Copy<T>(T input)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(input))!;
    private static string Snapshot<T>(T value)=>JsonSerializer.Serialize(value);
    private static bool Precision(decimal value,int places)=>decimal.Round(value,places)==value;
    private static void Reason(string reason,bool required){if(reason.Length>2000||required&&string.IsNullOrWhiteSpace(reason))throw new LeasingValidationException("LeasingReasonRequired");}
    private static void CheckRevision(Guid actual,Guid expected,bool existing){if(existing&&actual!=expected)throw new LeasingValidationException("LeasingConcurrency");}
    private static void ValidateCommon(string name,string number,string currency,string? notes,LeasingTerms terms)=>LeasingRules.ValidateCommon(name,number,currency,notes,terms);
    private static async Task References(ILeasingPaymentWork work,Guid organization,Guid user,CancellationToken ct)
    {
        if(!await work.OrganizationExistsAsync(organization,ct))throw new LeasingValidationException("LeasingInvalidParty");
        if(!await work.MemberExistsAsync(user,ct))throw new LeasingValidationException("LeasingInvalidOwner");
    }
    private void EnsureInitialFinancing(LeasingPaymentState graph,LeasingAcquisition a,Guid user)
    {
        var initial=LeasingInitialFinancing.Create(a,user,clock.GetUtcNow(),graph.Financing.Count>0);if(initial is not null)graph.Financing.Add(initial);
    }
    private static bool Retry(LeasingPaymentState graph,Guid request,Guid user,string action,Guid? acquisition,Guid? invoice)
    {
        if(request==Guid.Empty)throw new LeasingValidationException("PaymentInvalid");if(graph.PreviousRequest is not {} e)return false;
        if(e.ActorUserId!=user||e.Action!=action||e.AcquisitionId!=acquisition||e.InvoiceId!=invoice)throw new LeasingValidationException("LeasingConcurrency");return true;
    }
    private LeasingPaymentEvent History(Guid account,Guid? acquisition,Guid? invoice,Guid user,string action,string reason,string before,string after,Guid request)=>new(){Id=request,AccountId=account,AcquisitionId=acquisition,InvoiceId=invoice,ActorUserId=user,Action=action,Reason=reason,BeforeJson=before,AfterJson=after,RecordedUtc=clock.GetUtcNow()};
    public static LeasingFinancingRevision? FinancingAt(IEnumerable<LeasingFinancingRevision> revisions, DateOnly date) => revisions.Where(x=>x.EffectiveFrom.HasValue&&x.EffectiveFrom<=date).OrderByDescending(x=>x.EffectiveFrom).FirstOrDefault();
    public async Task AddFinancingRevisionAsync(Guid account, Guid acquisition, Guid acquisitionRevision, DateOnly effectiveFrom, LeasingFinancingSnapshot input, string reason, Guid request, CancellationToken ct=default)
    {
        input=Copy(input);var actor=await Actor(account,ct);await using var work=await data.OpenAsync(account,acquisition,LeasingPaymentTarget.Acquisition,request,true,ct);actor=await Revalidate(actor,work,true,null,ct);var user=actor.UserId;var graph=work.State;var a=graph.Acquisition!;
        if(Retry(graph,request,user,"FinancingRevised",acquisition,null))return;
        CheckRevision(a.Revision,acquisitionRevision,true);Reason(reason,true);
        if(a.Status==LeasingAcquisitionStatus.Cancelled||effectiveFrom==default||input.Currency!=a.Currency||input.Terms.Months!=a.Terms.Months)throw new LeasingValidationException("PaymentInvalid");
        await References(work,input.FinanceOrganizationId,a.OwnerUserId,ct);
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
        if(graph.Financing.Any(x=>x.EffectiveFrom==effectiveFrom))throw new LeasingValidationException("PaymentEffectiveDuplicate");
        EnsureInitialFinancing(graph,a,user);
        var revision=new LeasingFinancingRevision{Id=request,AccountId=account,AcquisitionId=a.Id,EffectiveFrom=effectiveFrom,SnapshotJson=Snapshot(input),ActorUserId=user,RecordedUtc=clock.GetUtcNow(),Reason=reason};
        graph.Financing.Add(revision);
        var affected=graph.Plans.Where(p=>p.Status==LeasingPlanStatus.Active).SelectMany(p=>p.Terms).Where(x=>x.PeriodTo>=effectiveFrom).ToList();
        foreach(var term in affected){term.NeedsReview=true;term.ReviewReason=null;}
        a.Revision=Guid.NewGuid();
        await work.CommitAsync(History(account,a.Id,null,user,"FinancingRevised",reason,"{}",Snapshot(revision),request),ct);
    }
    public async Task<PaymentDetails> GetPaymentsAsync(Guid account,Guid acquisition,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await data.OpenAsync(account,acquisition,LeasingPaymentTarget.Acquisition,null,false,ct);actor=await Revalidate(actor,work,false,null,ct);var graph=work.State;var a=graph.Acquisition!;
        var active=graph.Plans.SingleOrDefault(x=>x.Status==LeasingPlanStatus.Active);var views=new List<PaymentTermView>();
        foreach(var term in active?.Terms.OrderBy(x=>x.DueDate).ThenBy(x=>x.Reference)??Enumerable.Empty<LeasingPlanTerm>())
        {
            var links=graph.Allocations.Where(x=>x.InstallmentId==term.InstallmentId&&!x.Reversed&&graph.Invoices.Any(i=>i.Id==x.InvoiceId&&i.Status==LeasingInvoiceStatus.Approved)).ToList();
            decimal net=0,vat=0;foreach(var link in links){var sign=graph.Invoices.Single(x=>x.Id==link.InvoiceId).Kind==LeasingInvoiceKind.Invoice?1:-1;net+=sign*link.Net;vat+=sign*link.Vat;}
            var obligation=graph.Installments.Single(x=>x.Id==term.InstallmentId);views.Add(new(term,obligation,net,vat,LeasingPaymentCalculator.Status(term,obligation,net,vat,links.Count>0),links));
        }
        foreach(var document in graph.Documents)document.StorageKey="";
        foreach(var invoice in graph.Invoices){invoice.StorageKey="";invoice.ProcessingToken=null;}
        bool Has(string p)=>LeasingUserAccess.Has(actor,p);
        return new(a,graph.Financing,graph.Plans,views,graph.Invoices,graph.Documents,graph.History,new(Owns(actor,graph)&&a.Status!=LeasingAcquisitionStatus.Cancelled,Has(Permissions.Leasing.ActivatePlans),Has(Permissions.Leasing.ApproveInvoices),Has(Permissions.Leasing.AcceptVariances)));
    }
    public async Task<List<PaymentPlanRow>> ListPaymentPlansAsync(Guid account,string? search=null,string? currency=null,Guid? finance=null,LeasingPlanStatus? status=null,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);var facts=await data.ReadListAsync(LeasingReader.Scope(actor),search,currency,ct);
        var today=AgreementReminderSchedule.Today(clock.GetUtcNow(),facts.TimeZoneId);var rows=new List<PaymentPlanRow>();
        foreach(var a in facts.Acquisitions)
        {
            var current=FinancingAt(facts.Financing.Where(x=>x.AcquisitionId==a.Id),today);var party=current==null?a.FinanceOrganizationId:JsonSerializer.Deserialize<LeasingFinancingSnapshot>(current.SnapshotJson)!.FinanceOrganizationId;
            if(finance.HasValue&&party!=finance.Value)continue;
            var plans=facts.Plans.Where(x=>x.AcquisitionId==a.Id);var shown=status.HasValue?plans.Where(x=>x.Status==status).ToList():plans.Where(x=>x.Status!=LeasingPlanStatus.Replaced).ToList();
            if(shown.Count==0&&!status.HasValue)rows.Add(new(a.Id,a.Name,facts.Organizations.GetValueOrDefault(party,""),a.Currency,null,null,null,null,null,null,true));
            foreach(var p in shown)rows.Add(new(a.Id,a.Name,facts.Organizations.GetValueOrDefault(party,""),p.Currency,p.Id,p.Version,p.Status,p.Terms.Select(x=>(DateOnly?)x.PeriodFrom).Min(),p.Terms.Select(x=>(DateOnly?)x.PeriodTo).Max(),p.Terms.Where(x=>x.DueDate>=today).Select(x=>(DateOnly?)x.DueDate).Min(),p.Terms.Any(x=>x.NeedsReview)||current==null));
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
        terms=Copy(terms);var actor=await Actor(account,ct);await using var work=await data.OpenAsync(account,acquisition,LeasingPaymentTarget.Acquisition,request,true,ct);actor=await Revalidate(actor,work,true,null,ct);var user=actor.UserId;var graph=work.State;var a=graph.Acquisition!;
        if(Retry(graph,request,user,"PlanDraft",acquisition,null))
        {
            var saved=graph.PreviousRequest!;
            return JsonSerializer.Deserialize<LeasingPaymentPlan>(saved.AfterJson)!.Id;
        }
        if(a.Status==LeasingAcquisitionStatus.Cancelled||terms.Count is <1 or >600||notes?.Length>10000||!Enum.IsDefined(source))throw new LeasingValidationException("PaymentInvalid");
        if(source==LeasingPlanSource.Imported&&!document.HasValue)throw new LeasingValidationException("PaymentSourceRequired");
        if(document.HasValue&&!graph.Documents.Any(x=>x.Id==document))throw new UnauthorizedAccessException();
        var active=graph.Plans.SingleOrDefault(x=>x.Status==LeasingPlanStatus.Active);
        if(active?.Id!=basedOn)throw new LeasingValidationException("LeasingConcurrency");
        var refs=new HashSet<string>();
        var financing=graph.Financing;
        foreach(var term in terms)
        {
            term.Reference=term.Reference.Trim().ToUpperInvariant();ValidatePlanTerm(term,a);if(!refs.Add(term.Reference))throw new LeasingValidationException("PaymentDuplicateTerm");
            if(term.FinancingRevisionId.HasValue&&!financing.Any(x=>x.Id==term.FinancingRevisionId))throw new UnauthorizedAccessException();
            var applicable=FinancingAt(financing,term.PeriodFrom);if(applicable==null||applicable.Id!=term.FinancingRevisionId)term.NeedsReview=true;
        }
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Snapshot(new{a.Currency,basedOn,notes,terms=terms.Select(x=>new{x.Reference,x.PeriodFrom,x.PeriodTo,x.DueDate,x.Type,x.Net,x.Vat,x.Gross,x.CapitalComponent,x.InterestComponent,x.FeeComponent,x.ComponentsComplete,x.FinancingRevisionId,x.ReviewReason,x.ObligationDocumentReference})}))));
        var duplicate=graph.Plans.SingleOrDefault(x=>x.ContentHash==fingerprint);
        if(duplicate!=null)
        {
            await work.CommitAsync(History(account,a.Id,null,user,"PlanDraft","Existing identical content","{}",Snapshot(duplicate),request),ct);return duplicate.Id;
        }
        var p=new LeasingPaymentPlan{Id=request,AccountId=account,AcquisitionId=a.Id,Version=1+(graph.Plans.Select(x=>(int?)x.Version).Max()??0),Status=LeasingPlanStatus.Draft,Source=source,SourceDocumentId=document,Currency=a.Currency,BasedOnPlanId=basedOn,ContentHash=fingerprint,Revision=Guid.NewGuid(),CreatedByUserId=user,CreatedUtc=clock.GetUtcNow(),Notes=notes};
        foreach(var input in terms)
        {
            var obligation=graph.Installments.SingleOrDefault(x=>x.Reference==input.Reference);
            if(input.InstallmentId!=Guid.Empty&&obligation?.Id!=input.InstallmentId)throw new LeasingValidationException("PaymentStableReference");
            if(obligation==null){obligation=new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=a.Id,Reference=input.Reference,Revision=Guid.NewGuid()};graph.Installments.Add(obligation);}
            var term=JsonSerializer.Deserialize<LeasingPlanTerm>(Snapshot(input))!;term.Id=Guid.NewGuid();term.AccountId=account;term.PlanId=p.Id;term.InstallmentId=obligation.Id;p.Terms.Add(term);
        }
        graph.Plans.Add(p);await work.CommitAsync(History(account,a.Id,null,user,"PlanDraft","","{}",Snapshot(p),request),ct);return p.Id;
    }
    public async Task ActivatePaymentPlanAsync(Guid account, Guid plan, Guid revision, string reason, bool invoicedChangesConfirmed, Guid request, CancellationToken ct=default)
    {
        var actor=await Required(account,Permissions.Leasing.ActivatePlans,ct);await using var work=await data.OpenAsync(account,plan,LeasingPaymentTarget.Plan,request,true,ct);actor=await Revalidate(actor,work,false,Permissions.Leasing.ActivatePlans,ct);var user=actor.UserId;var graph=work.State;var p=graph.Plans.SingleOrDefault(x=>x.Id==plan)??throw new UnauthorizedAccessException();
        if(Retry(graph,request,user,"PlanActivated",p.AcquisitionId,null))return;
        if(p.Status==LeasingPlanStatus.Active)return;CheckRevision(p.Revision,revision,true);Reason(reason,true);
        if(p.Status!=LeasingPlanStatus.Draft)throw new LeasingValidationException("LeasingInvalidTransition");
        var a=graph.Acquisition!;if(a.Status!=LeasingAcquisitionStatus.Registered)throw new LeasingValidationException("PaymentRegisteredRequired");
        var old=graph.Plans.SingleOrDefault(x=>x.Status==LeasingPlanStatus.Active);
        if(old?.Id!=p.BasedOnPlanId)throw new LeasingValidationException("LeasingConcurrency");
        var financing=graph.Financing;
        foreach(var term in p.Terms)
        {
            ValidatePlanTerm(term,a);var applicable=FinancingAt(financing,term.PeriodFrom);if(applicable==null||applicable.Id!=term.FinancingRevisionId)term.NeedsReview=true;
            if(term.Net==null||term.Vat==null||term.Gross==null)throw new LeasingValidationException("PaymentUnknownAmounts");
            if(term.NeedsReview&&string.IsNullOrWhiteSpace(term.ReviewReason))throw new LeasingValidationException("PaymentReviewRequired");
            term.NeedsReview=false;
        }
        var allocatedIds=graph.Allocations.Select(x=>x.InstallmentId).Distinct().ToList();
        if(allocatedIds.Any(id=>p.Terms.All(t=>t.InstallmentId!=id)))throw new LeasingValidationException("PaymentAllocatedRemoval");
        var changed=old?.Terms.Where(t=>allocatedIds.Contains(t.InstallmentId)&&p.Terms.Any(n=>n.InstallmentId==t.InstallmentId&&TermFinancialSnapshot(n)!=TermFinancialSnapshot(t))).ToList()??[];
        if(changed.Count>0&&!invoicedChangesConfirmed)throw new LeasingValidationException("PaymentConfirmInvoicedChange");
        foreach(var id in changed.Select(x=>x.InstallmentId)){var obligation=graph.Installments.Single(x=>x.Id==id);obligation.VarianceAccepted=false;obligation.BillingComplete=false;obligation.Revision=Guid.NewGuid();}
        var before=old==null?"{}":Snapshot(old);
        if(old!=null){old.Status=LeasingPlanStatus.Replaced;old.Revision=Guid.NewGuid();await work.FlushReplacedPlanAsync(ct);}
        p.Status=LeasingPlanStatus.Active;p.CheckedByUserId=user;p.CheckedUtc=clock.GetUtcNow();p.Revision=Guid.NewGuid();
        await work.CommitAsync(History(account,a.Id,null,user,"PlanActivated",reason,before,Snapshot(p),request),ct);
    }
    private static string TermFinancialSnapshot(LeasingPlanTerm t)=>Snapshot(new{t.PeriodFrom,t.PeriodTo,t.DueDate,t.Type,t.Net,t.Vat,t.Gross,t.CapitalComponent,t.InterestComponent,t.FeeComponent,t.ComponentsComplete,t.FinancingRevisionId});
    public async Task ReviewPaymentTermAsync(Guid account, Guid installment, Guid revision, bool billingComplete, bool acceptVariance, bool reviewPlan, string reason, Guid request, CancellationToken ct=default)
    {
        var permission=reviewPlan?Permissions.Leasing.ActivatePlans:Permissions.Leasing.AcceptVariances;
        var actor=await Required(account,permission,ct);await using var work=await data.OpenAsync(account,installment,LeasingPaymentTarget.Installment,request,true,ct);actor=await Revalidate(actor,work,false,permission,ct);var user=actor.UserId;var graph=work.State;var i=graph.Installments.SingleOrDefault(x=>x.Id==installment)??throw new UnauthorizedAccessException();
        if(Retry(graph,request,user,"TermReviewed",i.AcquisitionId,null))return;
        CheckRevision(i.Revision,revision,true);Reason(reason,true);
        var t=graph.Plans.Where(p=>p.Status==LeasingPlanStatus.Active).SelectMany(p=>p.Terms).SingleOrDefault(x=>x.InstallmentId==i.Id)??throw new LeasingValidationException("PaymentActiveRequired");
        var before=Snapshot(new{i,t});
        if(reviewPlan){t.NeedsReview=false;t.ReviewReason=reason;}
        else {if(acceptVariance&&!billingComplete)throw new LeasingValidationException("PaymentCompletionRequired");i.BillingComplete=billingComplete;i.VarianceAccepted=acceptVariance;i.ControlReason=reason;i.CheckedByUserId=user;i.CheckedUtc=clock.GetUtcNow();}
        i.Revision=Guid.NewGuid();await work.CommitAsync(History(account,i.AcquisitionId,null,user,"TermReviewed",reason,before,Snapshot(new{i,t}),request),ct);
    }
}
