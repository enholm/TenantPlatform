using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Agreements;
namespace TenantPlatform.Web.Services.Leasing;

// Reuses the existing agreement scheduler's time-zone and DST rules. Leasing links cannot use the agreement-only FK queue.
public sealed class LeasingLifecycleProcessor(IDbContextFactory<TenantPlatformDbContext> factory,TimeProvider clock)
{
    public async Task RunAccountAsync(Guid account,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);if(!await db.Accounts.AnyAsync(x=>x.Id==account&&x.IsActive,ct))return;
        await using var tx=await db.Database.BeginTransactionAsync(ct);await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\"={account} FOR UPDATE",ct);
        var settings=await db.LeasingNotificationSettings.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account,ct);
        var zone=settings?.TimeZoneId??await db.AgreementReminderSettings.Where(x=>x.AccountId==account).Select(x=>x.TimeZoneId).SingleOrDefaultAsync(ct)??"Europe/Oslo";
        var now=clock.GetUtcNow();var today=AgreementReminderSchedule.Today(now,zone);var wanted=new List<LeasingFollowup>();
        void Add(Guid? a,Guid? f,LeasingFollowupKind kind,DateOnly? due,Guid? owner,string suffix="")=>wanted.Add(new(){Id=Guid.NewGuid(),AccountId=account,AcquisitionId=a,FrameworkId=f,Kind=kind,DueDate=due,OwnerUserId=owner,Key=$"{a??f}:{kind}:{due?.ToString("yyyyMMdd")??suffix}",Status=LeasingFollowupStatus.Open,Revision=Guid.NewGuid()});
        var acquisitions=await db.LeasingAcquisitions.AsNoTracking().Where(x=>x.AccountId==account&&x.Status==LeasingAcquisitionStatus.Registered).ToListAsync(ct);
        var states=await db.LeasingLifecycles.AsNoTracking().Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.AcquisitionId,ct);
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
            if(await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.AcquisitionId==a.Id&&(x.Status==LeasingEquipmentStatus.PlannedReturn||x.Status==LeasingEquipmentStatus.LostOrDamaged),ct))Add(a.Id,null,LeasingFollowupKind.Equipment,state.ReturnDueDate,state.OwnerUserId);
            if(!state.OwnerUserId.HasValue||!await LeasingLifecycleAccess.CanRead(db,account,state.OwnerUserId.Value,a.Id,null,ct))Add(a.Id,null,LeasingFollowupKind.MissingOwner,null,null);
        }
        foreach(var f in await db.LeasingFrameworks.AsNoTracking().Where(x=>x.AccountId==account&&x.Status!=LeasingFrameworkStatus.Finished).ToListAsync(ct))
        {
            Add(null,f.Id,LeasingFollowupKind.FrameworkEnd,f.AcquisitionTo,f.OwnerUserId);
            if(f.AcquisitionTo<today){var orders=await db.LeasingOrders.AsNoTracking().Where(x=>x.AccountId==account&&x.FrameworkId==f.Id&&x.Status==LeasingOrderStatus.Approved).Include(x=>x.Lines).ToListAsync(ct);if(orders.Any(x=>LeasingOrderCalculator.Reserved(x,f.IncludesVat)>0))Add(null,f.Id,LeasingFollowupKind.OpenReservation,f.AcquisitionTo,f.OwnerUserId);}
            if(!await LeasingLifecycleAccess.CanRead(db,account,f.OwnerUserId,null,f.Id,ct))Add(null,f.Id,LeasingFollowupKind.MissingOwner,null,null);
        }
        var existing=await db.LeasingFollowups.Where(x=>x.AccountId==account).ToListAsync(ct);
        foreach(var old in existing.Where(x=>!wanted.Any(w=>w.Key==x.Key)&&x.Status!=LeasingFollowupStatus.Obsolete)){old.Status=LeasingFollowupStatus.Obsolete;old.Revision=Guid.NewGuid();}
        foreach(var item in wanted){var old=existing.SingleOrDefault(x=>x.Key==item.Key);if(old==null){db.LeasingFollowups.Add(item);existing.Add(item);}else{if(!old.AssignedExplicitly)old.OwnerUserId=item.OwnerUserId;if(old.Status==LeasingFollowupStatus.Obsolete){old.Status=LeasingFollowupStatus.Open;old.Revision=Guid.NewGuid();}}}
        await db.SaveChangesAsync(ct);
        var notifications=await db.LeasingNotifications.Where(x=>x.AccountId==account).ToListAsync(ct);
        var active=existing.Where(x=>x.Status is LeasingFollowupStatus.Open or LeasingFollowupStatus.InProgress).ToList();
        foreach(var n in notifications)
        {
            var task=active.FirstOrDefault(x=>x.Id==n.FollowupId);
            if(task==null||settings?.Enabled!=true||!settings.Days.Contains(n.DaysBefore)||!(settings.ExtraRecipientIds.Contains(n.RecipientUserId)||task.OwnerUserId==n.RecipientUserId)||!await LeasingLifecycleAccess.CanRead(db,account,n.RecipientUserId,task.AcquisitionId,task.FrameworkId,ct))
            {n.Status=LeasingNotificationStatus.Skipped;n.ResultKey="LifeNotificationObsolete";}
        }
        if(settings?.Enabled==true&&settings.ActivatedUtc.HasValue)
        foreach(var task in active.Where(x=>x.DueDate.HasValue&&x.Kind is LeasingFollowupKind.FrameworkEnd or LeasingFollowupKind.Notice or LeasingFollowupKind.Expiry or LeasingFollowupKind.Return))
        foreach(var user in settings.ExtraRecipientIds.Concat(task.OwnerUserId.HasValue?[task.OwnerUserId.Value]:Array.Empty<Guid>()).Distinct())
        foreach(var days in settings.Days)
        {
            var row=notifications.SingleOrDefault(x=>x.FollowupId==task.Id&&x.RecipientUserId==user&&x.DaysBefore==days);
            if(row==null){row=new(){Id=Guid.NewGuid(),AccountId=account,FollowupId=task.Id,RecipientUserId=user,DaysBefore=days,Status=LeasingNotificationStatus.Pending};db.LeasingNotifications.Add(row);notifications.Add(row);}
            if(row.Status is LeasingNotificationStatus.Delivered or LeasingNotificationStatus.Skipped)continue;
            row.ScheduledUtc=AgreementReminderSchedule.Scheduled(task.DueDate!.Value,days,settings.TimeZoneId,new(9,0));
            if(row.ScheduledUtc<settings.ActivatedUtc||task.DueDate<today){row.Status=LeasingNotificationStatus.Skipped;row.ResultKey="LifeNotificationHistorical";}
            else if(!await LeasingLifecycleAccess.CanRead(db,account,user,task.AcquisitionId,task.FrameworkId,ct)){row.Status=LeasingNotificationStatus.Failed;row.ResultKey="LifeInvalidOwner";}
            else if(row.ScheduledUtc<=now){row.Status=LeasingNotificationStatus.Delivered;row.DeliveredUtc=now;row.ResultKey=null;}
            else row.Status=LeasingNotificationStatus.Pending;
        }
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
public sealed class LeasingLifecycleWorker(IServiceScopeFactory scopes,ILogger<LeasingLifecycleWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            try{using var scope=scopes.CreateScope();var factory=scope.ServiceProvider.GetRequiredService<IDbContextFactory<TenantPlatformDbContext>>();await using var db=await factory.CreateDbContextAsync(stoppingToken);var accounts=await db.Accounts.AsNoTracking().Where(x=>x.IsActive).Select(x=>x.Id).ToListAsync(stoppingToken);foreach(var id in accounts){try{await scope.ServiceProvider.GetRequiredService<LeasingLifecycleProcessor>().RunAccountAsync(id,stoppingToken);}catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){throw;}catch(Exception ex){logger.LogError(ex,"Leasing follow-up failed for account {AccountId}",id);}}}
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"Leasing follow-up worker failed");}
            await Task.Delay(TimeSpan.FromMinutes(5),stoppingToken);
        }
    }
}
