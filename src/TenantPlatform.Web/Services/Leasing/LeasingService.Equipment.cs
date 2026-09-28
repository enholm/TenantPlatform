using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public async Task<List<LeasingOption>> EquipmentBuildingsAsync(Guid account,CancellationToken ct=default){await using var db=await factory.CreateDbContextAsync(ct);await Member(db,account,ct);return await db.Buildings.AsNoTracking().Where(x=>x.AccountId==account).OrderBy(x=>x.Name).Select(x=>new LeasingOption(x.Id,x.Name)).ToListAsync(ct);}
    public async Task<EquipmentPage> ListEquipmentAsync(Guid account,string? search=null,LeasingEquipmentStatus? status=null,Guid? acquisition=null,int page=1,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);var visible=Acquisitions(db,account,user,await LifecycleReader(admin,ct)).Select(x=>x.Id);var query=db.LeasingEquipment.AsNoTracking().Where(x=>x.AccountId==account&&visible.Contains(x.AcquisitionId));
        if(acquisition.HasValue)query=query.Where(x=>x.AcquisitionId==acquisition);if(status.HasValue)query=query.Where(x=>x.Status==status);if(!string.IsNullOrWhiteSpace(search)){var q=search.ToLower();query=query.Where(x=>x.Description.ToLower().Contains(q)||(x.SerialNumber??"").ToLower().Contains(q)||(x.InternalId??"").ToLower().Contains(q));}
        var count=await query.CountAsync(ct);page=Math.Clamp(page,1,Math.Max(1,(count+49)/50));var rows=await query.OrderBy(x=>x.Description).ThenBy(x=>x.Id).Skip((page-1)*50).Take(50).ToListAsync(ct);var ids=rows.Select(x=>x.AcquisitionId).ToArray();return new(rows,count,await db.LeasingAcquisitions.Where(x=>x.AccountId==account&&ids.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct),await db.Buildings.Where(x=>x.AccountId==account).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct),page);
    }
    public async Task SetCountableAsync(Guid account,Guid acquisition,Guid item,bool countable,string reason,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanMaintainLeasingEquipmentAsync(ct))throw new UnauthorizedAccessException();await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);await LifecycleAcquisition(db,account,acquisition,user,admin,ct);Reason(reason,true);
        var line=await db.LeasingItems.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==acquisition&&x.Id==item,ct)??throw new UnauthorizedAccessException();
        if(countable&&line.Quantity!=decimal.Truncate(line.Quantity)||!countable&&(await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.ItemId==item,ct)||await db.LeasingDispositions.AnyAsync(x=>x.AccountId==account&&x.ItemId==item,ct)))throw new LeasingValidationException("LifeCountableRequired");
        var before=Snapshot(new{line.Id,line.CountableEquipment});line.CountableEquipment=countable;PaymentEvent(db,account,acquisition,null,user,"EquipmentCountable",reason,before,Snapshot(new{line.Id,line.CountableEquipment}),Guid.NewGuid());await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task<List<Guid>> SaveEquipmentAsync(Guid account,Guid acquisition,List<LeasingEquipment> inputs,bool duplicateSerialConfirmed,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanMaintainLeasingEquipmentAsync(ct))throw new UnauthorizedAccessException();
        await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);var a=await LifecycleAcquisition(db,account,acquisition,user,admin,ct);Reason(reason,true);
        if(await PaymentRetry(db,account,request,user,"EquipmentSaved",acquisition,null,ct)){var saved=await db.LeasingPaymentEvents.SingleAsync(x=>x.AccountId==account&&x.Id==request,ct);return JsonSerializer.Deserialize<List<LeasingEquipment>>(saved.AfterJson)!.Select(x=>x.Id).ToList();}
        if(inputs.Where(x=>x.Id!=Guid.Empty).Select(x=>x.Id).Distinct().Count()!=inputs.Count(x=>x.Id!=Guid.Empty)||inputs.Count is <1 or >1000||a.Status!=LeasingAcquisitionStatus.Registered||(await LifecycleState(db,a,ct)).Status==LeasingLifecycleStatus.Closed)throw new LeasingValidationException("LifeClosed");
        var before=new List<LeasingEquipment>();var result=new List<LeasingEquipment>();
        foreach(var input in inputs)
        {
            var item=await db.LeasingItems.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==acquisition&&x.Id==input.ItemId,ct)??throw new UnauthorizedAccessException();
            if(!item.CountableEquipment)throw new LeasingValidationException("LifeCountableRequired");
            if(string.IsNullOrWhiteSpace(input.Description)||input.Description.Length>500||input.Notes.Length>10000||input.SerialNumber?.Length>200||input.InternalId?.Length>100||input.RegisteredDate==default||input.Status is not(LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn))throw new LeasingValidationException("LifeInvalidEquipment");
            input.InternalId=string.IsNullOrWhiteSpace(input.InternalId)?null:input.InternalId.Trim().ToUpperInvariant();input.SerialNumber=string.IsNullOrWhiteSpace(input.SerialNumber)?null:input.SerialNumber.Trim();
            await LifecycleReferences(db,account,acquisition,input.OwnerUserId,input.SourceDocumentId,ct);
            if(input.BuildingId.HasValue&&!await db.Buildings.AnyAsync(x=>x.AccountId==account&&x.Id==input.BuildingId,ct))throw new UnauthorizedAccessException();
            var unit=input.Id==Guid.Empty?new LeasingEquipment{Id=Guid.NewGuid(),AccountId=account,AcquisitionId=acquisition,ItemId=item.Id}:await db.LeasingEquipment.SingleOrDefaultAsync(x=>x.AccountId==account&&x.AcquisitionId==acquisition&&x.Id==input.Id,ct)??throw new UnauthorizedAccessException();
            if(input.Id!=Guid.Empty){CheckRevision(unit.Revision,input.Revision,true);if(unit.ItemId!=item.Id||unit.Status is not(LeasingEquipmentStatus.InUse or LeasingEquipmentStatus.PlannedReturn))throw new LeasingValidationException("LifeEventRequired");before.Add(JsonSerializer.Deserialize<LeasingEquipment>(Snapshot(unit))!);}
            else
            {
                var existing=await db.LeasingEquipment.CountAsync(x=>x.AccountId==account&&x.ItemId==item.Id,ct);var untracked=await db.LeasingDispositions.Where(x=>x.AccountId==account&&x.ItemId==item.Id&&!x.Reversed&&x.EquipmentId==null).SumAsync(x=>x.Quantity,ct);
                if(existing+untracked+result.Count(x=>x.ItemId==item.Id&&inputs.All(i=>i.Id!=x.Id))+1>item.Quantity)throw new LeasingValidationException("LifeOverQuantity");db.LeasingEquipment.Add(unit);
            }
            if(input.InternalId!=null&&(result.Any(x=>x.InternalId==input.InternalId)||await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.Id!=unit.Id&&x.InternalId==input.InternalId,ct)))throw new LeasingValidationException("LifeDuplicateInternalId");
            if(!duplicateSerialConfirmed&&input.SerialNumber!=null&&(result.Any(x=>string.Equals(x.SerialNumber,input.SerialNumber,StringComparison.OrdinalIgnoreCase))||await db.LeasingEquipment.AnyAsync(x=>x.AccountId==account&&x.Id!=unit.Id&&x.SerialNumber!=null&&x.SerialNumber.ToUpper()==input.SerialNumber.ToUpper(),ct)))throw new LeasingValidationException("LifeDuplicateSerial");
            unit.InternalId=input.InternalId;unit.SerialNumber=input.SerialNumber;unit.Description=input.Description;unit.BuildingId=input.BuildingId;unit.OwnerUserId=input.OwnerUserId;unit.Status=input.Status;unit.StatusDate=input.StatusDate;unit.RegisteredDate=input.RegisteredDate;unit.SourceDocumentId=input.SourceDocumentId;unit.Notes=input.Notes;unit.Revision=Guid.NewGuid();result.Add(unit);
        }
        PaymentEvent(db,account,acquisition,null,user,"EquipmentSaved",reason,Snapshot(before),Snapshot(result),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return result.Select(x=>x.Id).ToList();
    }
    public async Task ReverseLifecycleEventAsync(Guid account,Guid id,Guid revision,string reason,Guid request,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);if(!await authorization.CanApproveLeasingLifecycleAsync(ct))throw new UnauthorizedAccessException();await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,account,ct);
        var e=await db.LeasingLifecycleEvents.SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==id,ct)??throw new UnauthorizedAccessException();if(await PaymentRetry(db,account,request,user,"LifecycleReversed",e.AcquisitionId,null,ct))return;CheckRevision(e.Revision,revision,true);Reason(reason,true);if(e.Decision!=LeasingLifecycleDecision.Approved)throw new LeasingValidationException("LeasingInvalidTransition");
        var before=JsonSerializer.Deserialize<LifecycleBefore>(e.BeforeJson)!;var after=JsonSerializer.Deserialize<LifecycleBefore>(e.AfterJson)!;var a=await LifecycleAcquisition(db,account,e.AcquisitionId,user,admin,ct);var state=await LifecycleState(db,a,ct);
        if(state.Revision!=after.State.Revision)throw new LeasingValidationException("LifeReverseLatest");
        foreach(var old in before.Units){var current=await db.LeasingEquipment.SingleAsync(x=>x.AccountId==account&&x.Id==old.Id,ct);if(current.Revision!=after.Units.Single(x=>x.Id==old.Id).Revision)throw new LeasingValidationException("LifeReverseLatest");db.Entry(current).CurrentValues.SetValues(old);current.Revision=Guid.NewGuid();}
        db.Entry(state).CurrentValues.SetValues(before.State);state.Revision=Guid.NewGuid();
        foreach(var row in await db.LeasingDispositions.Where(x=>x.AccountId==account&&x.EventId==id&&!x.Reversed).ToListAsync(ct))row.Reversed=true;
        e.Decision=LeasingLifecycleDecision.Reversed;e.Revision=Guid.NewGuid();PaymentEvent(db,account,a.Id,null,user,"LifecycleReversed",reason,e.AfterJson,Snapshot(state),request);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
}
