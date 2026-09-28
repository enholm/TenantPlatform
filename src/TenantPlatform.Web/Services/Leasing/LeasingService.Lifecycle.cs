using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Web.Services.Leasing;

public sealed class LifecycleEventInput
{
    public LeasingLifecycleKind Kind {get;set;}=LeasingLifecycleKind.Return;
    public DateOnly Date {get;set;}=DateOnly.FromDateTime(DateTime.Today);
    public Guid? ItemId {get;set;}
    public Guid[] EquipmentIds {get;set;}=[];
    public decimal Quantity {get;set;}=1;
    public Guid? ReplacementEquipmentId {get;set;}
    public DateOnly? PreviousEndDate {get;set;}
    public DateOnly? NewEndDate {get;set;}
    public decimal? BuyoutAmount {get;set;}
    public bool PaymentsChanged {get;set;}
    public string TermsReference {get;set;}="";
    public string Reason {get;set;}="";
    public Guid? CounterpartyId {get;set;}
    public string Reference {get;set;}="";
    public Guid? SourceDocumentId {get;set;}
    public bool UnresolvedConfirmed {get;set;}
}
public sealed record LifecycleDetails(LeasingAcquisition Acquisition,LeasingLifecycle Lifecycle,List<LeasingEquipment> Equipment,List<LeasingLifecycleEvent> Events,List<LeasingDisposition> Dispositions,List<LeasingDocument> Documents,List<LeasingPaymentEvent> Changes,List<string> ClosureIssues,bool CanManage,bool CanApprove,bool CanMaintainEquipment);
public sealed record EquipmentPage(List<LeasingEquipment> Rows,int Count,Dictionary<Guid,string> Acquisitions,Dictionary<Guid,string> Buildings,int Page);
public static class LeasingLifecycleAccess
{
    public static async Task<bool> CanRead(TenantPlatformDbContext db,Guid account,Guid user,Guid? acquisition,Guid? framework,CancellationToken ct)
    {
        var memberships=db.UserAccounts.Where(x=>x.AccountId==account&&x.UserId==user&&x.User.IsActive);
        if(!await memberships.AnyAsync(ct))return false;
        var memberIds=memberships.Select(x=>x.Id);
        if(await db.UserAccountRoles.AnyAsync(x=>memberIds.Contains(x.UserAccountId)&&(x.Role==UserRole.AccountAdmin||x.Role==UserRole.LeasingEquipmentManager||x.Role==UserRole.LeasingLifecycleManager||x.Role==UserRole.LeasingLifecycleApprover||x.Role==UserRole.LeasingReportReader||x.Role==UserRole.LeasingReportExporter),ct))return true;
        return acquisition.HasValue?await db.LeasingAcquisitions.AnyAsync(x=>x.AccountId==account&&x.Id==acquisition&&(x.OwnerUserId==user||db.LeasingFrameworks.Any(f=>f.AccountId==account&&f.Id==x.FrameworkId&&f.OwnerUserId==user)),ct):await db.LeasingFrameworks.AnyAsync(x=>x.AccountId==account&&x.Id==framework&&x.OwnerUserId==user,ct);
    }
}
public sealed partial class LeasingService
{
    private async Task<bool> LifecycleReader(bool admin,CancellationToken ct)=>admin||await authorization.CanMaintainLeasingEquipmentAsync(ct)||await authorization.CanManageLeasingLifecycleAsync(ct)||await authorization.CanApproveLeasingLifecycleAsync(ct)||await authorization.CanReadLeasingReportsAsync(ct)||await authorization.CanExportLeasingReportsAsync(ct);
    private async Task<LeasingAcquisition> LifecycleAcquisition(TenantPlatformDbContext db,Guid account,Guid id,Guid user,bool admin,CancellationToken ct)=>await Acquisitions(db,account,user,await LifecycleReader(admin,ct)).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new UnauthorizedAccessException();
    private async Task<bool> LifecycleManage(TenantPlatformDbContext db,Guid account,Guid id,Guid user,bool admin,CancellationToken ct)=>await authorization.CanManageLeasingLifecycleAsync(ct)||await Acquisitions(db,account,user,admin).AnyAsync(x=>x.Id==id,ct);
    private static async Task<LeasingLifecycle> LifecycleState(TenantPlatformDbContext db,LeasingAcquisition a,CancellationToken ct)=>await db.LeasingLifecycles.SingleOrDefaultAsync(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id,ct)??LeasingLifecycleCalculator.Initial(a);
    private static void TrackLifecycle(TenantPlatformDbContext db,LeasingLifecycle state){if(db.Entry(state).State==EntityState.Detached)db.LeasingLifecycles.Add(state);state.Revision=Guid.NewGuid();}
    private static async Task LifecycleReferences(TenantPlatformDbContext db,Guid account,Guid acquisition,Guid? owner,Guid? document,CancellationToken ct)
    {
        if(owner.HasValue&&!await LeasingLifecycleAccess.CanRead(db,account,owner.Value,acquisition,null,ct))throw new LeasingValidationException("LifeInvalidOwner");
        if(document.HasValue&&!await db.LeasingDocuments.AnyAsync(x=>x.AccountId==account&&x.Id==document&&x.AcquisitionId==acquisition,ct))throw new UnauthorizedAccessException();
    }
    public async Task<LifecycleDetails> GetLifecycleAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var a=await LifecycleAcquisition(db,account,id,user,admin,ct);
        a.Items=await db.LeasingItems.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==id).OrderBy(x=>x.Position).ToListAsync(ct);
        var state=await LifecycleState(db,a,ct);
        return new(a,state,await db.LeasingEquipment.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==id).OrderBy(x=>x.InternalId).ToListAsync(ct),await db.LeasingLifecycleEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==id).OrderByDescending(x=>x.RecordedUtc).ThenByDescending(x=>x.Id).ToListAsync(ct),await db.LeasingDispositions.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==id).ToListAsync(ct),await db.LeasingDocuments.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==id).ToListAsync(ct),await db.LeasingPaymentEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.AcquisitionId==id&&x.Action.StartsWith("Lifecycle")).OrderByDescending(x=>x.RecordedUtc).ToListAsync(ct),await ClosureIssues(db,a,state,ct),await LifecycleManage(db,account,id,user,admin,ct),await authorization.CanApproveLeasingLifecycleAsync(ct),admin||await authorization.CanMaintainLeasingEquipmentAsync(ct));
    }
    public async Task SaveLifecycleTermsAsync(Guid account,Guid id,LeasingLifecycle input,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await LifecycleManage(db,account,id,user,admin,ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);var a=await LifecycleAcquisition(db,account,id,user,admin,ct);
        if(await PaymentRetry(db,account,request,user,"LifecycleTerms",id,null,ct))return;
        var state=await LifecycleState(db,a,ct);CheckRevision(state.Revision,input.Revision,true);Reason(reason,true);
        if(input.ExplicitNoticeDate==DateOnly.MinValue||input.ReturnDueDate==DateOnly.MinValue||!Enum.IsDefined(input.NoticeBasis)||input.Notes.Length>10000||input.NoticeCount is <0 or >36500||input.ExtensionMonths is <1 or >600||input.AutomaticExtension==true&&!input.ExtensionMonths.HasValue||input.NoticeBasis==LeasingNoticeBasis.Explicit&&!input.ExplicitNoticeDate.HasValue||input.NoticeBasis is LeasingNoticeBasis.Days or LeasingNoticeBasis.CalendarMonths&&(!input.NoticeCount.HasValue||LeasingLifecycleCalculator.Notice(state.AgreedEndDate,input.NoticeBasis,null,input.NoticeCount)==null))throw new LeasingValidationException("LifeInvalidTerms");
        if(input.AgreedEndDate!=state.AgreedEndDate)throw new LeasingValidationException("LifeUseExtension");
        await LifecycleReferences(db,account,id,input.OwnerUserId,input.SourceDocumentId,ct);var before=Snapshot(state);
        state.NoticeBasis=input.NoticeBasis;state.NoticeCount=input.NoticeCount;state.ExplicitNoticeDate=input.ExplicitNoticeDate;state.AutomaticExtension=input.AutomaticExtension;state.ExtensionMonths=input.ExtensionMonths;state.ReturnDueDate=input.ReturnDueDate;state.OwnerUserId=input.OwnerUserId;state.SourceDocumentId=input.SourceDocumentId;state.Notes=input.Notes;TrackLifecycle(db,state);
        PaymentEvent(db,account,id,null,user,"LifecycleTerms",reason,before,Snapshot(state),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<Guid> ProposeLifecycleEventAsync(Guid account,Guid id,LifecycleEventInput input,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await LifecycleManage(db,account,id,user,admin,ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);var a=await LifecycleAcquisition(db,account,id,user,admin,ct);
        if(await PaymentRetry(db,account,request,user,"LifecycleProposed",id,null,ct))return request;
        Reason(input.Reason,true);if(!Enum.IsDefined(input.Kind)||input.Kind==LeasingLifecycleKind.Correct||input.Date==default||input.Reference.Length>500||input.TermsReference.Length>2000||input.EquipmentIds.Length>1000)throw new LeasingValidationException("LifeInvalidEvent");
        await LifecycleReferences(db,account,id,null,input.SourceDocumentId,ct);
        if(input.CounterpartyId.HasValue&&!await db.Organizations.AnyAsync(x=>x.AccountId==account&&x.Id==input.CounterpartyId,ct))throw new UnauthorizedAccessException();
        if(input.ItemId.HasValue&&!await db.LeasingItems.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==id&&x.Id==input.ItemId,ct))throw new UnauthorizedAccessException();
        foreach(var unit in input.EquipmentIds)if(!await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==id&&x.Id==unit,ct))throw new UnauthorizedAccessException();
        if(input.ReplacementEquipmentId.HasValue&&!await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.Id==input.ReplacementEquipmentId&&Acquisitions(db,account,user,true).Select(a=>a.Id).Contains(x.AcquisitionId),ct))throw new UnauthorizedAccessException();
        var e=new LeasingLifecycleEvent{Id=request,AccountId=account,AcquisitionId=id,Kind=input.Kind,Decision=LeasingLifecycleDecision.Pending,Date=input.Date,ActorUserId=user,RecordedUtc=clock.GetUtcNow(),Reason=input.Reason,Reference=input.Reference,CounterpartyId=input.CounterpartyId,SourceDocumentId=input.SourceDocumentId,InputJson=Snapshot(input),Revision=Guid.NewGuid()};db.LeasingLifecycleEvents.Add(e);PaymentEvent(db,account,id,null,user,"LifecycleProposed",input.Reason,"{}",Snapshot(e),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return request;
    }
    private sealed record LifecycleBefore(LeasingLifecycle State,List<LeasingEquipment> Units);
    public async Task DecideLifecycleEventAsync(Guid account,Guid id,Guid revision,bool approve,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanApproveLeasingLifecycleAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);var e=await db.LeasingLifecycleEvents.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==id,ct)??throw new UnauthorizedAccessException();
        if(await PaymentRetry(db,account,request,user,"LifecycleDecision",e.AcquisitionId,null,ct))return;
        CheckRevision(e.Revision,revision,true);Reason(reason,true);if(e.Decision!=LeasingLifecycleDecision.Pending)throw new LeasingValidationException("LeasingInvalidTransition");
        var a=await db.LeasingAcquisitions.SingleAsync(x=>x.AccountId==account&&x.Id==e.AcquisitionId,ct);var state=await LifecycleState(db,a,ct);var input=JsonSerializer.Deserialize<LifecycleEventInput>(e.InputJson)!;
        var units=await db.LeasingEquipment.Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&input.EquipmentIds.Contains(x.Id)).ToListAsync(ct);e.BeforeJson=Snapshot(new LifecycleBefore(state,units));
        if(approve)
        {
            if(a.Status!=LeasingAcquisitionStatus.Registered||state.Status==LeasingLifecycleStatus.Closed)throw new LeasingValidationException("LifeClosed");
            await LifecycleReferences(db,account,a.Id,state.OwnerUserId,input.SourceDocumentId,ct);
            if(input.Kind is LeasingLifecycleKind.Extend or LeasingLifecycleKind.Buyout or LeasingLifecycleKind.Close or LeasingLifecycleKind.EarlyClose && !input.SourceDocumentId.HasValue)throw new LeasingValidationException("LifeDocumentRequired");
            if(input.Kind==LeasingLifecycleKind.Extend)
            {
                if(input.PreviousEndDate!=state.AgreedEndDate||!input.NewEndDate.HasValue||input.NewEndDate<=state.AgreedEndDate||input.NewEndDate<input.Date||string.IsNullOrWhiteSpace(input.TermsReference))throw new LeasingValidationException("LifeInvalidExtension");
                state.AgreedEndDate=input.NewEndDate.Value;state.Status=LeasingLifecycleStatus.Extended;state.PaymentPlanReviewRequired=true;state.PaymentsChanged=input.PaymentsChanged;state.PlanAtExtensionId=await db.LeasingPaymentPlans.Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct);
            }
            else if(input.Kind is LeasingLifecycleKind.Close or LeasingLifecycleKind.EarlyClose)
            {
                if(input.Date>await OrderToday(db,account,ct)||input.Kind==LeasingLifecycleKind.Close&&input.Date<state.AgreedEndDate)throw new LeasingValidationException("LifeInvalidEvent");
                var issues=await ClosureIssues(db,a,state,ct);if(issues.Count>0&&!input.UnresolvedConfirmed)throw new LeasingValidationException("LifeConfirmUnresolved");
                state.Status=LeasingLifecycleStatus.Closed;state.ClosedDate=input.Date;state.ClosureKind=input.Kind;state.DocumentReviewComplete=false;
                if(input.Kind==LeasingLifecycleKind.EarlyClose){state.PaymentPlanReviewRequired=true;state.PaymentsChanged=true;state.PlanAtExtensionId=await db.LeasingPaymentPlans.Where(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct);}
            }
            else if(input.Kind==LeasingLifecycleKind.StartClosing)state.Status=LeasingLifecycleStatus.Closing;
            else
            {
                if(input.Date>await OrderToday(db,account,ct)||input.ItemId==null||input.Quantity<=0||input.Quantity!=decimal.Truncate(input.Quantity))throw new LeasingValidationException("LifeInvalidQuantity");
                var item=await db.LeasingItems.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&x.Id==input.ItemId,ct)??throw new UnauthorizedAccessException();
                if(!item.CountableEquipment||item.Quantity!=decimal.Truncate(item.Quantity))throw new LeasingValidationException("LifeCountableRequired");
                var processed=await db.LeasingDispositions.Where(x=>x.AccountId==account&&x.ItemId==item.Id&&!x.Reversed).SumAsync(x=>x.Quantity,ct);
                if(input.EquipmentIds.Distinct().Count()!=input.EquipmentIds.Length||input.EquipmentIds.Length>0&&(units.Count!=input.EquipmentIds.Length||units.Count!=input.Quantity||units.Any(x=>x.ItemId!=item.Id||x.Status is not (LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn))))throw new LeasingValidationException("LifeInvalidQuantity");
                if(input.EquipmentIds.Length==0){var tracked=await db.LeasingEquipment.CountAsync(x=>x.AccountId==account&&x.ItemId==item.Id&&(x.Status==LeasingEquipmentStatus.InUse||x.Status==LeasingEquipmentStatus.PlannedReturn),ct);if(input.Quantity>item.Quantity-processed-tracked)throw new LeasingValidationException("LifeSelectUnits");}
                if(processed+input.Quantity>item.Quantity)throw new LeasingValidationException("LifeOverQuantity");
                if(input.Kind==LeasingLifecycleKind.Buyout&&(!input.BuyoutAmount.HasValue||!PaymentMoney(input.BuyoutAmount)))throw new LeasingValidationException("LifeBuyoutRequired");
                if(input.Kind==LeasingLifecycleKind.Replace)
                {
                    if(input.Quantity!=1||input.ReplacementEquipmentId==null||input.EquipmentIds.Contains(input.ReplacementEquipmentId.Value))throw new LeasingValidationException("LifeReplacementRequired");
                    var replacement=await db.LeasingEquipment.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==input.ReplacementEquipmentId&&x.Status==LeasingEquipmentStatus.InUse,ct)??throw new LeasingValidationException("LifeReplacementRequired");
                    var replacements=await db.LeasingLifecycleEvents.AsNoTracking().Where(x=>x.AccountId==account&&x.Kind==LeasingLifecycleKind.Replace&&x.Decision==LeasingLifecycleDecision.Approved).Select(x=>x.InputJson).ToListAsync(ct);
                    if(replacements.Any(x=>JsonSerializer.Deserialize<LifecycleEventInput>(x)!.ReplacementEquipmentId==replacement.Id))throw new LeasingValidationException("LifeReplacementRequired");
                    if(!await db.LeasingAcquisitions.AnyAsync(x=>x.AccountId==account&&x.Id==replacement.AcquisitionId&&x.Status==LeasingAcquisitionStatus.Registered,ct)||await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.ReplacementEquipmentId==replacement.Id,ct))throw new LeasingValidationException("LifeReplacementRequired");
                }
                foreach(var unit in units){unit.Status=input.Kind switch{LeasingLifecycleKind.Return=>LeasingEquipmentStatus.Returned,LeasingLifecycleKind.Replace=>LeasingEquipmentStatus.Replaced,LeasingLifecycleKind.Buyout=>LeasingEquipmentStatus.BoughtOut,_=>LeasingEquipmentStatus.LostOrDamaged};unit.StatusDate=input.Date;unit.ReplacementEquipmentId=input.ReplacementEquipmentId;unit.Revision=Guid.NewGuid();}
                foreach(var unit in units.Select(x=>(Guid?)x.Id).DefaultIfEmpty(null))db.LeasingDispositions.Add(new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=a.Id,EventId=e.Id,ItemId=item.Id,EquipmentId=unit,Quantity=unit.HasValue?1:input.Quantity});
            }
            TrackLifecycle(db,state);
        }
        e.Decision=approve?LeasingLifecycleDecision.Approved:LeasingLifecycleDecision.Rejected;e.ApprovedByUserId=user;e.ApprovedUtc=clock.GetUtcNow();e.Revision=Guid.NewGuid();e.AfterJson=Snapshot(new LifecycleBefore(state,units));
        PaymentEvent(db,account,a.Id,null,user,"LifecycleDecision",reason,e.BeforeJson,Snapshot(e),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task ReviewLifecycleDocumentsAsync(Guid account,Guid id,Guid revision,bool plan,bool complete,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanApproveLeasingLifecycleAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);var a=await LifecycleAcquisition(db,account,id,user,admin,ct);var state=await LifecycleState(db,a,ct);if(await PaymentRetry(db,account,request,user,"LifecycleReviewed",id,null,ct))return;CheckRevision(state.Revision,revision,true);Reason(reason,true);var before=Snapshot(state);
        if(plan){if(state.PaymentsChanged&&!await db.LeasingPaymentPlans.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==id&&x.Status==LeasingPlanStatus.Active&&x.Id!=state.PlanAtExtensionId,ct))throw new LeasingValidationException("LifePlanRevisionRequired");state.PaymentPlanReviewRequired=false;}
        else { if(complete&&state.PaymentPlanReviewRequired)throw new LeasingValidationException("LifePlanRevisionRequired");
            if(complete&&(await ClosureIssues(db,a,state,ct)).Any(x=>x is "LifeDocumentIssues" or "LifeUnallocatedPartyInvoices"))throw new LeasingValidationException("LifeDocumentIssues");state.DocumentReviewComplete=complete;}
        TrackLifecycle(db,state);PaymentEvent(db,account,id,null,user,"LifecycleReviewed",reason,before,Snapshot(state),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    private static async Task InvalidateLifecycleDocumentReview(TenantPlatformDbContext db,LeasingInvoice invoice,CancellationToken ct)
    {
        var revisions=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==invoice.AccountId).Select(x=>new{x.AcquisitionId,x.SnapshotJson}).ToListAsync(ct);
        var revisedIds=revisions.Where(x=>{var f=JsonSerializer.Deserialize<LeasingFinancingSnapshot>(x.SnapshotJson)!;return f.FinanceOrganizationId==invoice.FinanceOrganizationId&&f.Currency==invoice.Currency;}).Select(x=>x.AcquisitionId).Distinct().ToArray();
        var candidates=db.LeasingAcquisitions.Where(a=>a.AccountId==invoice.AccountId&&a.Currency==invoice.Currency&&(a.FinanceOrganizationId==invoice.FinanceOrganizationId||revisedIds.Contains(a.Id))).Select(a=>a.Id);
        foreach(var state in await db.LeasingLifecycles.Where(x=>x.AccountId==invoice.AccountId&&candidates.Contains(x.AcquisitionId)&&x.DocumentReviewComplete).ToListAsync(ct)){state.DocumentReviewComplete=false;state.Revision=Guid.NewGuid();}
    }
    private static async Task<List<string>> ClosureIssues(TenantPlatformDbContext db,LeasingAcquisition a,LeasingLifecycle state,CancellationToken ct)
    {
        var issues=new List<string>();
        if(await db.LeasingEquipment.AnyAsync(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id&&(x.Status==LeasingEquipmentStatus.InUse||x.Status==LeasingEquipmentStatus.PlannedReturn),ct))issues.Add("LifeOpenEquipment");
        if(await db.LeasingFollowups.AnyAsync(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id&&(x.Status==LeasingFollowupStatus.Open||x.Status==LeasingFollowupStatus.InProgress),ct))issues.Add("LifeOpenFollowups");
        var plans=await db.LeasingPaymentPlans.Where(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id&&x.Status==LeasingPlanStatus.Active).Include(x=>x.Terms).ToListAsync(ct);
        if(plans.Count==0||state.PaymentPlanReviewRequired||plans.SelectMany(x=>x.Terms).Any(x=>x.NeedsReview))issues.Add("LifePlanIncomplete");
        if(plans.SelectMany(x=>x.Terms).Any(x=>x.DueDate>=(state.ClosedDate??state.AgreedEndDate)))issues.Add("LifeRemainingTerms");
        var obligations=await db.LeasingInstallments.Where(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id).ToListAsync(ct);
        var ids=obligations.Select(x=>x.Id).ToArray();
        var allocations=await db.LeasingPaymentAllocations.AsNoTracking().Where(x=>x.AccountId==a.AccountId&&ids.Contains(x.InstallmentId)&&!x.Reversed).ToListAsync(ct);
        var invoiceIds=allocations.Select(x=>x.InvoiceId).ToArray();var approved=await db.LeasingInvoices.AsNoTracking().Where(x=>x.AccountId==a.AccountId&&invoiceIds.Contains(x.Id)&&x.Status==LeasingInvoiceStatus.Approved).ToDictionaryAsync(x=>x.Id,ct);
        foreach(var term in plans.SelectMany(x=>x.Terms)){var links=allocations.Where(x=>x.InstallmentId==term.InstallmentId&&approved.ContainsKey(x.InvoiceId)).ToList();var net=links.Sum(x=>(approved[x.InvoiceId].Kind==LeasingInvoiceKind.CreditNote?-1:1)*x.Net);var vat=links.Sum(x=>(approved[x.InvoiceId].Kind==LeasingInvoiceKind.CreditNote?-1:1)*x.Vat);var status=LeasingPaymentCalculator.Status(term,obligations.Single(x=>x.Id==term.InstallmentId),net,vat,links.Count>0);if(status is not(LeasingBillingStatus.Reconciled or LeasingBillingStatus.Accepted))issues.Add("LifeDocumentIssues");}
        foreach(var item in await db.LeasingItems.AsNoTracking().Where(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id&&x.CountableEquipment).ToListAsync(ct))if(await db.LeasingDispositions.Where(x=>x.AccountId==a.AccountId&&x.ItemId==item.Id&&!x.Reversed).SumAsync(x=>x.Quantity,ct)<item.Quantity)issues.Add("LifeOpenEquipment");
        // An invoice may cover multiple acquisitions: show party/currency candidates, never pretend unallocated money has an acquisition allocation.
        var financing=await db.LeasingFinancingRevisions.AsNoTracking().Where(x=>x.AccountId==a.AccountId&&x.AcquisitionId==a.Id).Select(x=>x.SnapshotJson).ToListAsync(ct);
        var financeIds=financing.Select(x=>JsonSerializer.Deserialize<LeasingFinancingSnapshot>(x)!.FinanceOrganizationId).Append(a.FinanceOrganizationId).Distinct().ToArray();
        var docs=await db.LeasingInvoices.Where(x=>x.AccountId==a.AccountId&&x.Category==LeasingInvoiceCategory.Rental&&x.FinanceOrganizationId.HasValue&&financeIds.Contains(x.FinanceOrganizationId.Value)&&x.Currency==a.Currency&&x.Status!=LeasingInvoiceStatus.Reversed&&x.Status!=LeasingInvoiceStatus.Rejected).ToListAsync(ct);
        foreach(var doc in docs){var links=await db.LeasingPaymentAllocations.Where(x=>x.AccountId==a.AccountId&&x.InvoiceId==doc.Id&&!x.Reversed).ToListAsync(ct);if(doc.Status==LeasingInvoiceStatus.Review||links.Sum(x=>x.Net)!=doc.Net||links.Sum(x=>x.Vat)!=doc.Vat){issues.Add("LifeUnallocatedPartyInvoices");break;}}
        return issues.Distinct().ToList();
    }
}
