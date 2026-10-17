using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed class LeasingLifecycleJob(ILeasingLifecycleJobData data, TimeProvider clock, ISystemWorkContext system)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.LeasingLifecycle);
        foreach (var account in await data.ReadActiveAccountsAsync(ct))
        {
            try { await RunAccountAsync(account, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { data.ReportFailure(account, ex.GetType().Name); }
        }
    }
    public async Task RunAccountAsync(Guid account,CancellationToken ct=default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.LeasingLifecycle);
        await using var tx = await data.BeginAsync(account, ct); var work = tx.State;
        if (!work.AccountActive) return;
        var settings = work.Settings; var zone = settings?.TimeZoneId ?? work.AgreementTimeZone ?? "Europe/Oslo";
        bool CanRead(Guid user, Guid? acquisition, Guid? framework)
        {
            var a = work.Acquisitions.SingleOrDefault(x => x.Id == acquisition);
            var f = work.Frameworks.SingleOrDefault(x => x.Id == (framework ?? a?.FrameworkId));
            return LeasingLifecycleRecipientRules.CanRead(new(work.Recipients.GetValueOrDefault(user), a, f));
        }
        var now=clock.GetUtcNow();var today=AgreementReminderSchedule.Today(now,zone);var wanted=new List<LeasingFollowup>();
        void Add(Guid? a,Guid? f,LeasingFollowupKind kind,DateOnly? due,Guid? owner,string suffix="")=>wanted.Add(new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=a,FrameworkId=f,Kind=kind,DueDate=due,OwnerUserId=owner,Key=$"{a??f}:{kind}:{due?.ToString("yyyyMMdd")??suffix}",Status=LeasingFollowupStatus.Open,Revision=Guid.NewGuid()});
        var acquisitions=work.Acquisitions.Where(x => x.Status == LeasingAcquisitionStatus.Registered).ToList();
        var states=work.Lifecycles.ToDictionary(x => x.AcquisitionId);
        foreach(var a in acquisitions)
        {
            var state=states.GetValueOrDefault(a.Id)??LeasingLifecycleCalculator.Initial(a);
            if(state.Status!=LeasingLifecycleStatus.Closed)
            {
                Add(a.Id,null,LeasingFollowupKind.Expiry,state.AgreedEndDate,state.OwnerUserId);
                if(state.NoticeDate.HasValue)Add(a.Id,null,LeasingFollowupKind.Notice,state.NoticeDate,state.OwnerUserId);
                if(state.ReturnDueDate.HasValue)Add(a.Id,null,LeasingFollowupKind.Return,state.ReturnDueDate,state.OwnerUserId);
                if(state.NoticeBasis==LeasingNoticeBasis.Unknown||state.AutomaticExtension==null)Add(a.Id,null,LeasingFollowupKind.UnknownTerms,null,state.OwnerUserId);
                if(state.AutomaticExtension==true&&state.NoticeDate<=today)Add(a.Id,null,LeasingFollowupKind.ExtensionOutcome,state.AgreedEndDate,state.OwnerUserId);
            }
            else if(!state.DocumentReviewComplete)Add(a.Id,null,LeasingFollowupKind.Documents,null,state.OwnerUserId);
            if(state.PaymentPlanReviewRequired)Add(a.Id,null,LeasingFollowupKind.PaymentPlan,null,state.OwnerUserId,state.Revision.ToString());
            if(work.Equipment.Any(x => x.AcquisitionId == a.Id && (x.Status == LeasingEquipmentStatus.PlannedReturn || x.Status == LeasingEquipmentStatus.LostOrDamaged)))Add(a.Id,null,LeasingFollowupKind.Equipment,state.ReturnDueDate,state.OwnerUserId);
            if(!state.OwnerUserId.HasValue||!CanRead(state.OwnerUserId.Value,a.Id,null))Add(a.Id,null,LeasingFollowupKind.MissingOwner,null,null);
        }
        foreach(var f in work.Frameworks.Where(x => x.Status != LeasingFrameworkStatus.Finished).ToList())
        {
            Add(null,f.Id,LeasingFollowupKind.FrameworkEnd,f.AcquisitionTo,f.OwnerUserId);
            if(f.AcquisitionTo<today){var orders=work.Orders.Where(x => x.FrameworkId == f.Id && x.Status == LeasingOrderStatus.Approved).ToList();if(orders.Any(x=>LeasingOrderCalculator.Reserved(x,f.IncludesVat)>0))Add(null,f.Id,LeasingFollowupKind.OpenReservation,f.AcquisitionTo,f.OwnerUserId);}
            if(!CanRead(f.OwnerUserId,null,f.Id))Add(null,f.Id,LeasingFollowupKind.MissingOwner,null,null);
        }
        var existing=work.Followups;
        foreach(var old in existing.Where(x=>!wanted.Any(w=>w.Key==x.Key)&&x.Status!=LeasingFollowupStatus.Obsolete)){old.Status=LeasingFollowupStatus.Obsolete;old.Revision=Guid.NewGuid();}
        foreach(var item in wanted){var old=existing.SingleOrDefault(x=>x.Key==item.Key);if(old==null){existing.Add(item);}else{if(!old.AssignedExplicitly)old.OwnerUserId=item.OwnerUserId;if(old.Status==LeasingFollowupStatus.Obsolete){old.Status=LeasingFollowupStatus.Open;old.Revision=Guid.NewGuid();}}}
        await tx.SaveFollowupsAsync(ct);
        var notifications=await tx.ReadNotificationsAsync(ct);
        var active=existing.Where(x=>x.Status is LeasingFollowupStatus.Open or LeasingFollowupStatus.InProgress).ToList();
        foreach(var n in notifications)
        {
            var task=active.FirstOrDefault(x=>x.Id==n.FollowupId);
            if(task==null||settings?.Enabled!=true||!settings.Days.Contains(n.DaysBefore)||!(settings.ExtraRecipientIds.Contains(n.RecipientUserId)||task.OwnerUserId==n.RecipientUserId)||!CanRead(n.RecipientUserId,task.AcquisitionId,task.FrameworkId))
            {n.Status=LeasingNotificationStatus.Skipped;n.ResultKey="LifeNotificationObsolete";}
        }
        if(settings?.Enabled==true&&settings.ActivatedUtc.HasValue)
        foreach(var task in active.Where(x=>x.DueDate.HasValue&&x.Kind is LeasingFollowupKind.FrameworkEnd or LeasingFollowupKind.Notice or LeasingFollowupKind.Expiry or LeasingFollowupKind.Return))
        foreach(var user in settings.ExtraRecipientIds.Concat(task.OwnerUserId.HasValue?[task.OwnerUserId.Value]:Array.Empty<Guid>()).Distinct())
        foreach(var days in settings.Days)
        {
            var row=notifications.SingleOrDefault(x=>x.FollowupId==task.Id&&x.RecipientUserId==user&&x.DaysBefore==days);
            if(row==null){row=new(){Id=Guid.NewGuid(),AccountId=account,FollowupId=task.Id,RecipientUserId=user,DaysBefore=days,Status=LeasingNotificationStatus.Pending};notifications.Add(row);}
            if(row.Status is LeasingNotificationStatus.Delivered or LeasingNotificationStatus.Skipped)continue;
            row.ScheduledUtc=AgreementReminderSchedule.Scheduled(task.DueDate!.Value,days,settings.TimeZoneId,new(9,0));
            if(row.ScheduledUtc<settings.ActivatedUtc||task.DueDate<today){row.Status=LeasingNotificationStatus.Skipped;row.ResultKey="LifeNotificationHistorical";}
            else if(!CanRead(user,task.AcquisitionId,task.FrameworkId)){row.Status=LeasingNotificationStatus.Failed;row.ResultKey="LifeInvalidOwner";}
            else if(row.ScheduledUtc<=now){row.Status=LeasingNotificationStatus.Delivered;row.DeliveredUtc=now;row.ResultKey=null;}
            else row.Status=LeasingNotificationStatus.Pending;
        }
        await tx.CommitAsync(notifications, ct);
    }
}
