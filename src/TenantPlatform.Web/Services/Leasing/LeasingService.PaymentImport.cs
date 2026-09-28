using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public async Task<(Guid Document,PaymentImportGrid Grid)> UploadPaymentPlanSourceAsync(Guid account,Guid acquisition,string fileName,Stream stream,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        if(!await Acquisitions(db,account,user,admin).AnyAsync(x=>x.Id==acquisition&&x.Status!=LeasingAcquisitionStatus.Cancelled,ct))throw new UnauthorizedAccessException();
        await using var buffer=new MemoryStream();var bytes=new byte[81920];int read;while((read=await stream.ReadAsync(bytes,ct))>0){if(buffer.Length+read>Math.Min(MaxFileSizeBytes,10_000_000))throw new LeasingValidationException("PaymentImportSize");await buffer.WriteAsync(bytes.AsMemory(0,read),ct);}buffer.Position=0;
        var grid=PaymentPlanImport.Read(buffer,fileName);buffer.Position=0;
        var stored=await storage.StoreAsync(buffer,fileName,ct);
        try
        {
            var doc=new LeasingDocument{Id=Guid.NewGuid(),AccountId=account,AcquisitionId=acquisition,FileName=stored.FileName,StorageKey=stored.StorageKey,MediaType=stored.MediaType,Size=stored.Size,UploadedByUserId=user,UploadedUtc=clock.GetUtcNow()};db.LeasingDocuments.Add(doc);
            History(db,account,null,acquisition,user,"PaymentSourceAdded",fileName,"{}",Snapshot(new{doc.Id,doc.FileName}));await db.SaveChangesAsync(ct);return(doc.Id,grid);
        }
        catch {await using var check=await factory.CreateDbContextAsync(CancellationToken.None);if(!await check.LeasingDocuments.AnyAsync(x=>x.AccountId==account&&x.StorageKey==stored.StorageKey))await storage.DiscardUncommittedAsync(stored.StorageKey);throw;}
    }
    public async Task<PaymentImportGrid> ReadPaymentPlanSourceAsync(Guid account,Guid acquisition,Guid document,string? sheet=null,CancellationToken ct=default)
    {
        await using var db=await factory.CreateDbContextAsync(ct);var(user,admin)=await Member(db,account,ct);
        if(!await Acquisitions(db,account,user,await PaymentReader(admin,ct)).AnyAsync(x=>x.Id==acquisition,ct))throw new UnauthorizedAccessException();
        var doc=await db.LeasingDocuments.AsNoTracking().SingleOrDefaultAsync(x=>x.AccountId==account&&x.Id==document&&x.AcquisitionId==acquisition,ct)??throw new UnauthorizedAccessException();
        await using var stream=await storage.OpenReadAsync(doc.StorageKey,ct);return PaymentPlanImport.Read(stream,doc.FileName,sheet);
    }
}
