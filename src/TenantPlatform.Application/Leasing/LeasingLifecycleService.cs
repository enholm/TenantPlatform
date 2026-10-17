using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingLifecycleService(IAuthorizationService authorization,ILeasingLifecycleData data,TimeProvider clock)
{
    private Task<AccountActorScope> Actor(Guid account,CancellationToken ct)=>authorization.GetAccountActorScopeAsync(account,ct);
    private async Task<AccountActorScope> Approver(Guid account,CancellationToken ct)
    {
        var actor=await Actor(account,ct);if(!LeasingUserAccess.Has(actor,Permissions.Leasing.ApproveLifecycle))throw new UnauthorizedAccessException();return actor;
    }
    private async Task<AccountActorScope> Revalidate(AccountActorScope previous,ILeasingLifecycleWork work,bool manage,bool approve,CancellationToken ct)
    {
        var actor=approve?await Approver(previous.AccountId,ct):await Actor(previous.AccountId,ct);
        if(actor.UserId!=previous.UserId)throw new UnauthorizedAccessException();Authorize(actor,work.State,manage);return actor;
    }
    private static void Authorize(AccountActorScope actor,LeasingLifecycleState state,bool manage)
    {
        var scope=LeasingUserAccess.Create(actor);var owns=state.Acquisition?.OwnerUserId==actor.UserId||state.Framework?.OwnerUserId==actor.UserId;
        if(state.Acquisition is null||!scope.CanReadLifecycle&&!owns||manage&&!scope.CanManageLifecycle&&!owns)throw new UnauthorizedAccessException();
    }
    private static T Copy<T>(T input)=>JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(input))!;
    private static string Snapshot<T>(T value)=>JsonSerializer.Serialize(value);
    private static LeasingLifecycle State(LeasingLifecycleState graph)=>graph.Lifecycle??LeasingLifecycleCalculator.Initial(graph.Acquisition!);
    private static void Track(LeasingLifecycleState graph,LeasingLifecycle state){graph.Lifecycle=state;state.Revision=Guid.NewGuid();}
    private static void Reason(string reason,bool required){if(reason.Length>2000||required&&string.IsNullOrWhiteSpace(reason))throw new LeasingValidationException("LeasingReasonRequired");}
    private static void CheckRevision(Guid actual,Guid expected,bool existing){if(existing&&actual!=expected)throw new LeasingValidationException("LeasingConcurrency");}
    private static bool PaymentMoney(decimal? value)=>!value.HasValue||value>=0&&value<=1000000000000m&&decimal.Round(value.Value,2)==value.Value;
    private static bool Retry(LeasingLifecycleState graph,Guid request,Guid user,string action,Guid? acquisition,Guid? invoice)
    {
        if(request==Guid.Empty)throw new LeasingValidationException("PaymentInvalid");
        if(graph.PreviousRequest is not {} e)return false;
        if(e.ActorUserId!=user||e.Action!=action||e.AcquisitionId!=acquisition||e.InvoiceId!=invoice)throw new LeasingValidationException("LeasingConcurrency");return true;
    }
    private LeasingPaymentEvent History(Guid account,Guid? acquisition,Guid? invoice,Guid user,string action,string reason,string before,string after,Guid request)=>new(){Id=request,AccountId=account,AcquisitionId=acquisition,InvoiceId=invoice,ActorUserId=user,Action=action,Reason=reason,BeforeJson=before,AfterJson=after,RecordedUtc=clock.GetUtcNow()};
    private static async Task References(ILeasingLifecycleWork work,Guid? owner,Guid? document,CancellationToken ct)
    {
        if(owner.HasValue&&!LeasingLifecycleRecipientRules.CanRead(await work.ReadRecipientAsync(owner.Value,ct)))throw new LeasingValidationException("LifeInvalidOwner");
        if(document.HasValue&&!work.State.Documents.Any(x=>x.Id==document))throw new UnauthorizedAccessException();
    }
    public async Task<LifecycleDetails> GetLifecycleAsync(Guid account,Guid id,CancellationToken ct=default)
    {
        var actor=await Actor(account,ct);await using var work=await data.OpenAsync(account,id,false,null,false,ct);actor=await Revalidate(actor,work,false,false,ct);
        var graph=work.State;var a=graph.Acquisition!;a.Items=a.Items.OrderBy(x=>x.Position).ToList();var state=State(graph);var scope=LeasingUserAccess.Create(actor);
        foreach(var document in graph.Documents)document.StorageKey="";
        return new(a,state,graph.Equipment,graph.Events,graph.Dispositions,graph.Documents,graph.Changes,await ClosureIssues(work,state,ct),scope.CanManageLifecycle||a.OwnerUserId==actor.UserId||graph.Framework?.OwnerUserId==actor.UserId,LeasingUserAccess.Has(actor,Permissions.Leasing.ApproveLifecycle),scope.IsAdministrator||LeasingUserAccess.Has(actor,Permissions.Leasing.MaintainEquipment));
    }
    public async Task SaveLifecycleTermsAsync(Guid account,Guid id,LeasingLifecycle input,string reason,Guid request,CancellationToken ct=default)
    {
        input=Copy(input);var actor=await Actor(account,ct); await using var work=await data.OpenAsync(account,id,false,request,true,ct); actor=await Revalidate(actor,work,true,false,ct); var user=actor.UserId;var graph=work.State;var a=graph.Acquisition!;
        if(Retry(graph,request,user,"LifecycleTerms",id,null))return;
        var state=State(graph);CheckRevision(state.Revision,input.Revision,true);Reason(reason,true);
        if(input.NoticeBasis==LeasingNoticeBasis.None){input.ExplicitNoticeDate=null;input.NoticeCount=null;}
        if(input.ExplicitNoticeDate==DateOnly.MinValue||input.ReturnDueDate==DateOnly.MinValue||!Enum.IsDefined(input.NoticeBasis)||input.Notes.Length>10000||input.NoticeCount is <0 or >36500||input.ExtensionMonths is <1 or >600||input.AutomaticExtension==true&&!input.ExtensionMonths.HasValue||input.NoticeBasis==LeasingNoticeBasis.Explicit&&!input.ExplicitNoticeDate.HasValue||input.NoticeBasis is LeasingNoticeBasis.Days or LeasingNoticeBasis.CalendarMonths&&(!input.NoticeCount.HasValue||LeasingLifecycleCalculator.Notice(state.AgreedEndDate,input.NoticeBasis,null,input.NoticeCount)==null))throw new LeasingValidationException("LifeInvalidTerms");
        if(input.AgreedEndDate!=state.AgreedEndDate)throw new LeasingValidationException("LifeUseExtension");
        await References(work,input.OwnerUserId,input.SourceDocumentId,ct);var before=Snapshot(state);
        state.NoticeBasis=input.NoticeBasis;state.NoticeCount=input.NoticeCount;state.ExplicitNoticeDate=input.ExplicitNoticeDate;state.AutomaticExtension=input.AutomaticExtension;state.ExtensionMonths=input.ExtensionMonths;state.ReturnDueDate=input.ReturnDueDate;state.OwnerUserId=input.OwnerUserId;state.SourceDocumentId=input.SourceDocumentId;state.Notes=input.Notes;Track(graph,state);
        await work.CommitAsync(History(account,id,null,user,"LifecycleTerms",reason,before,Snapshot(state),request),ct);
    }
    public async Task<Guid> ProposeLifecycleEventAsync(Guid account,Guid id,LifecycleEventInput input,Guid request,CancellationToken ct=default)
    {
        input=Copy(input);var actor=await Actor(account,ct); await using var work=await data.OpenAsync(account,id,false,request,true,ct); actor=await Revalidate(actor,work,true,false,ct); var user=actor.UserId;var graph=work.State;var a=graph.Acquisition!;
        if(Retry(graph,request,user,"LifecycleProposed",id,null))return request;
        Reason(input.Reason,true);if(!Enum.IsDefined(input.Kind)||input.Kind==LeasingLifecycleKind.Correct||input.Date==default||input.Reference.Length>500||input.TermsReference.Length>2000||input.EquipmentIds.Length>1000)throw new LeasingValidationException("LifeInvalidEvent");
        await References(work,null,input.SourceDocumentId,ct);
        if(input.CounterpartyId.HasValue&&!await work.OrganizationExistsAsync(input.CounterpartyId.Value,ct))throw new UnauthorizedAccessException();
        if(input.ItemId.HasValue&&!a.Items.Any(x=>x.Id==input.ItemId))throw new UnauthorizedAccessException();
        foreach(var unit in input.EquipmentIds)if(!graph.Equipment.Any(x=>x.Id==unit))throw new UnauthorizedAccessException();
        if(input.ReplacementEquipmentId.HasValue&&(await work.ReadReplacementAsync(input.ReplacementEquipmentId.Value,ct)).Acquisition is null)throw new UnauthorizedAccessException();
        var e=new LeasingLifecycleEvent{Id=request,AccountId=account,AcquisitionId=id,Kind=input.Kind,Decision=LeasingLifecycleDecision.Pending,Date=input.Date,ActorUserId=user,RecordedUtc=clock.GetUtcNow(),Reason=input.Reason,Reference=input.Reference,CounterpartyId=input.CounterpartyId,SourceDocumentId=input.SourceDocumentId,InputJson=Snapshot(input),Revision=Guid.NewGuid()};graph.Events.Add(e);await work.CommitAsync(History(account,id,null,user,"LifecycleProposed",input.Reason,"{}",Snapshot(e),request),ct);return request;
    }
    private sealed record LifecycleBefore(LeasingLifecycle State,List<LeasingEquipment> Units);
    public async Task DecideLifecycleEventAsync(Guid account,Guid id,Guid revision,bool approve,string reason,Guid request,CancellationToken ct=default)
    {
        var actor=await Approver(account,ct); await using var work=await data.OpenAsync(account,id,true,request,true,ct); actor=await Revalidate(actor,work,false,true,ct);var user=actor.UserId;var graph=work.State;var e=graph.Events.SingleOrDefault(x=>x.Id==id)??throw new UnauthorizedAccessException();
        if(Retry(graph,request,user,"LifecycleDecision",e.AcquisitionId,null))return;
        CheckRevision(e.Revision,revision,true);Reason(reason,true);if(e.Decision!=LeasingLifecycleDecision.Pending)throw new LeasingValidationException("LeasingInvalidTransition");
        var a=graph.Acquisition!;var state=State(graph);var input=JsonSerializer.Deserialize<LifecycleEventInput>(e.InputJson)!;
        var units=graph.Equipment.Where(x=>input.EquipmentIds.Contains(x.Id)).ToList();e.BeforeJson=Snapshot(new LifecycleBefore(state,units));
        if(approve)
        {
            if(a.Status!=LeasingAcquisitionStatus.Registered||state.Status==LeasingLifecycleStatus.Closed)throw new LeasingValidationException("LifeClosed");
            await References(work,state.OwnerUserId,input.SourceDocumentId,ct);
            if(input.Kind is LeasingLifecycleKind.Extend or LeasingLifecycleKind.Buyout or LeasingLifecycleKind.Close or LeasingLifecycleKind.EarlyClose && !input.SourceDocumentId.HasValue)throw new LeasingValidationException("LifeDocumentRequired");
            if(input.Kind==LeasingLifecycleKind.Extend)
            {
                if(input.PreviousEndDate!=state.AgreedEndDate||!input.NewEndDate.HasValue||input.NewEndDate<=state.AgreedEndDate||input.NewEndDate<input.Date||string.IsNullOrWhiteSpace(input.TermsReference))throw new LeasingValidationException("LifeInvalidExtension");
                state.AgreedEndDate=input.NewEndDate.Value;state.Status=LeasingLifecycleStatus.Extended;state.PaymentPlanReviewRequired=true;state.PaymentsChanged=input.PaymentsChanged;state.PlanAtExtensionId=graph.Plans.Where(x=>x.Status==LeasingPlanStatus.Active).Select(x=>(Guid?)x.Id).SingleOrDefault();
            }
            else if(input.Kind is LeasingLifecycleKind.Close or LeasingLifecycleKind.EarlyClose)
            {
                if(input.Date>AgreementReminderSchedule.Today(clock.GetUtcNow(),graph.TimeZoneId)||input.Kind==LeasingLifecycleKind.Close&&input.Date<state.AgreedEndDate)throw new LeasingValidationException("LifeInvalidEvent");
                var issues=await ClosureIssues(work,state,ct);if(issues.Count>0&&!input.UnresolvedConfirmed)throw new LeasingValidationException("LifeConfirmUnresolved");
                state.Status=LeasingLifecycleStatus.Closed;state.ClosedDate=input.Date;state.ClosureKind=input.Kind;state.DocumentReviewComplete=false;
                if(input.Kind==LeasingLifecycleKind.EarlyClose){state.PaymentPlanReviewRequired=true;state.PaymentsChanged=true;state.PlanAtExtensionId=graph.Plans.Where(x=>x.Status==LeasingPlanStatus.Active).Select(x=>(Guid?)x.Id).SingleOrDefault();}
            }
            else if(input.Kind==LeasingLifecycleKind.StartClosing)state.Status=LeasingLifecycleStatus.Closing;
            else
            {
                if(input.Date>AgreementReminderSchedule.Today(clock.GetUtcNow(),graph.TimeZoneId)||input.ItemId==null||input.Quantity<=0||input.Quantity!=decimal.Truncate(input.Quantity))throw new LeasingValidationException("LifeInvalidQuantity");
                var item=a.Items.SingleOrDefault(x=>x.Id==input.ItemId)??throw new UnauthorizedAccessException();
                if(!item.CountableEquipment||item.Quantity!=decimal.Truncate(item.Quantity))throw new LeasingValidationException("LifeCountableRequired");
                var processed=graph.Dispositions.Where(x=>x.ItemId==item.Id&&!x.Reversed).Sum(x=>x.Quantity);
                if(input.EquipmentIds.Distinct().Count()!=input.EquipmentIds.Length||input.EquipmentIds.Length>0&&(units.Count!=input.EquipmentIds.Length||units.Count!=input.Quantity||units.Any(x=>x.ItemId!=item.Id||x.Status is not (LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn))))throw new LeasingValidationException("LifeInvalidQuantity");
                if(input.EquipmentIds.Length==0){var tracked=graph.Equipment.Count(x=>x.ItemId==item.Id&&(x.Status==LeasingEquipmentStatus.InUse||x.Status==LeasingEquipmentStatus.PlannedReturn));if(input.Quantity>item.Quantity-processed-tracked)throw new LeasingValidationException("LifeSelectUnits");}
                if(processed+input.Quantity>item.Quantity)throw new LeasingValidationException("LifeOverQuantity");
                if(input.Kind==LeasingLifecycleKind.Buyout&&(!input.BuyoutAmount.HasValue||!PaymentMoney(input.BuyoutAmount)))throw new LeasingValidationException("LifeBuyoutRequired");
                if(input.Kind==LeasingLifecycleKind.Replace)
                {
                    if(input.Quantity!=1||input.ReplacementEquipmentId==null||input.EquipmentIds.Contains(input.ReplacementEquipmentId.Value))throw new LeasingValidationException("LifeReplacementRequired");
                    var facts=await work.ReadReplacementAsync(input.ReplacementEquipmentId.Value,ct);
                    if(facts.Equipment is not {Status:LeasingEquipmentStatus.InUse} replacement||facts.Acquisition?.Status!=LeasingAcquisitionStatus.Registered||facts.ReferencedByEquipment||facts.ApprovedReplacementInputs.Any(x=>JsonSerializer.Deserialize<LifecycleEventInput>(x)!.ReplacementEquipmentId==replacement.Id))throw new LeasingValidationException("LifeReplacementRequired");
                }
                foreach(var unit in units){unit.Status=input.Kind switch{LeasingLifecycleKind.Return=>LeasingEquipmentStatus.Returned,LeasingLifecycleKind.Replace=>LeasingEquipmentStatus.Replaced,LeasingLifecycleKind.Buyout=>LeasingEquipmentStatus.BoughtOut,_=>LeasingEquipmentStatus.LostOrDamaged};unit.StatusDate=input.Date;unit.ReplacementEquipmentId=input.ReplacementEquipmentId;unit.Revision=Guid.NewGuid();}
                foreach(var unit in units.Select(x=>(Guid?)x.Id).DefaultIfEmpty(null))graph.Dispositions.Add(new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=a.Id,EventId=e.Id,ItemId=item.Id,EquipmentId=unit,Quantity=unit.HasValue?1:input.Quantity});
            }
            Track(graph,state);
        }
        e.Decision=approve?LeasingLifecycleDecision.Approved:LeasingLifecycleDecision.Rejected;e.ApprovedByUserId=user;e.ApprovedUtc=clock.GetUtcNow();e.Revision=Guid.NewGuid();e.AfterJson=Snapshot(new LifecycleBefore(state,units));
        await work.CommitAsync(History(account,a.Id,null,user,"LifecycleDecision",reason,e.BeforeJson,Snapshot(e),request),ct);
    }
    public async Task ReviewLifecycleDocumentsAsync(Guid account,Guid id,Guid revision,bool plan,bool complete,string reason,Guid request,CancellationToken ct=default)
    {
        var actor=await Approver(account,ct); await using var work=await data.OpenAsync(account,id,false,request,true,ct);actor=await Revalidate(actor,work,false,true,ct);var user=actor.UserId;var graph=work.State;var a=graph.Acquisition!;var state=State(graph);if(Retry(graph,request,user,"LifecycleReviewed",id,null))return;CheckRevision(state.Revision,revision,true);Reason(reason,true);var before=Snapshot(state);
        if(plan){if(state.PaymentsChanged&&!graph.Plans.Any(x=>x.Status==LeasingPlanStatus.Active&&x.Id!=state.PlanAtExtensionId))throw new LeasingValidationException("LifePlanRevisionRequired");state.PaymentPlanReviewRequired=false;}
        else { if(complete&&state.PaymentPlanReviewRequired)throw new LeasingValidationException("LifePlanRevisionRequired");
            if(complete&&(await ClosureIssues(work,state,ct)).Any(x=>x is "LifeDocumentIssues" or "LifeUnallocatedPartyInvoices"))throw new LeasingValidationException("LifeDocumentIssues");state.DocumentReviewComplete=complete;}
        Track(graph,state);await work.CommitAsync(History(account,id,null,user,"LifecycleReviewed",reason,before,Snapshot(state),request),ct);
    }

    private static async Task<List<string>> ClosureIssues(ILeasingLifecycleWork work,LeasingLifecycle state,CancellationToken ct)
    {
        var graph=work.State;var a=graph.Acquisition!;
        var financing=graph.Financing.Select(x=>JsonSerializer.Deserialize<LeasingFinancingSnapshot>(x.SnapshotJson)!.FinanceOrganizationId).Append(a.FinanceOrganizationId).Distinct().ToArray();
        return LeasingLifecycleRules.ClosureIssues(graph,state,await work.ReadPartyInvoicesAsync(financing,a.Currency,ct));
    }
    public async Task ReverseLifecycleEventAsync(Guid account,Guid id,Guid revision,string reason,Guid request,CancellationToken ct=default)
    {
        var actor=await Approver(account,ct);await using var work=await data.OpenAsync(account,id,true,request,true,ct);actor=await Revalidate(actor,work,false,true,ct);var graph=work.State;
        var e=graph.Events.SingleOrDefault(x=>x.Id==id)??throw new UnauthorizedAccessException();
        if(Retry(graph,request,actor.UserId,"LifecycleReversed",e.AcquisitionId,null))return;
        CheckRevision(e.Revision,revision,true);Reason(reason,true);if(e.Decision!=LeasingLifecycleDecision.Approved)throw new LeasingValidationException("LeasingInvalidTransition");
        var before=JsonSerializer.Deserialize<LifecycleBefore>(e.BeforeJson)!;var after=JsonSerializer.Deserialize<LifecycleBefore>(e.AfterJson)!;var state=State(graph);
        if(state.Revision!=after.State.Revision)throw new LeasingValidationException("LifeReverseLatest");
        if(before.State.AccountId!=account||before.State.AcquisitionId!=e.AcquisitionId||before.State.Id!=state.Id)throw new UnauthorizedAccessException();
        foreach(var old in before.Units)
        {
            if(old.AccountId!=account||old.AcquisitionId!=e.AcquisitionId)throw new UnauthorizedAccessException();
            var current=graph.Equipment.SingleOrDefault(x=>x.Id==old.Id)??throw new UnauthorizedAccessException();
            if(current.Revision!=after.Units.Single(x=>x.Id==old.Id).Revision)throw new LeasingValidationException("LifeReverseLatest");
            Restore(current,old);current.Revision=Guid.NewGuid();
        }
        Restore(state,before.State);Track(graph,state);
        foreach(var row in graph.Dispositions.Where(x=>x.EventId==id&&!x.Reversed))row.Reversed=true;
        e.Decision=LeasingLifecycleDecision.Reversed;e.Revision=Guid.NewGuid();
        await work.CommitAsync(History(account,e.AcquisitionId,null,actor.UserId,"LifecycleReversed",reason,e.AfterJson,Snapshot(state),request),ct);
    }
    private static void Restore(LeasingLifecycle target,LeasingLifecycle source)
    {
        target.OriginalEndDate=source.OriginalEndDate; target.AgreedEndDate=source.AgreedEndDate; target.NoticeBasis=source.NoticeBasis; target.ExplicitNoticeDate=source.ExplicitNoticeDate; target.NoticeCount=source.NoticeCount; target.AutomaticExtension=source.AutomaticExtension; target.ExtensionMonths=source.ExtensionMonths; target.ReturnDueDate=source.ReturnDueDate; target.OwnerUserId=source.OwnerUserId; target.SourceDocumentId=source.SourceDocumentId; target.Notes=source.Notes; target.Status=source.Status; target.ClosedDate=source.ClosedDate; target.ClosureKind=source.ClosureKind; target.DocumentReviewComplete=source.DocumentReviewComplete; target.PaymentPlanReviewRequired=source.PaymentPlanReviewRequired; target.PaymentsChanged=source.PaymentsChanged; target.PlanAtExtensionId=source.PlanAtExtensionId;
    }
    private static void Restore(LeasingEquipment target,LeasingEquipment source)
    {
        target.ItemId=source.ItemId; target.SerialNumber=source.SerialNumber; target.InternalId=source.InternalId; target.Description=source.Description; target.BuildingId=source.BuildingId; target.OwnerUserId=source.OwnerUserId; target.Status=source.Status; target.RegisteredDate=source.RegisteredDate; target.StatusDate=source.StatusDate; target.ReplacementEquipmentId=source.ReplacementEquipmentId; target.SourceDocumentId=source.SourceDocumentId; target.Notes=source.Notes;
    }
}
