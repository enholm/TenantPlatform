using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Leasing;

namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public async Task UploadOrderAsync(Guid account, Guid id, Guid revision, string fileName, Stream content, CancellationToken ct = default)
    {
        await GetOrderAsync(account, id, ct);
        var stored = await storage.StoreAsync(content, fileName, ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await Member(db, account, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
            var o = await Orders(db, account, user, admin).Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new UnauthorizedAccessException();
            CheckRevision(o.Revision, revision, true); var before = Snapshot(o); o.Revision = Guid.NewGuid();
            db.LeasingDocuments.Add(new() { Id = Guid.NewGuid(), AccountId = account, OrderId = id, FileName = stored.FileName, StorageKey = stored.StorageKey,
                MediaType = stored.MediaType, Size = stored.Size, UploadedByUserId = user, UploadedUtc = clock.GetUtcNow() });
            OrderEvent(db, o, user, "DocumentAdded", stored.FileName, before, Guid.NewGuid());
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch
        {
            await using var check = await factory.CreateDbContextAsync(CancellationToken.None);
            if (!await check.LeasingDocuments.AnyAsync(x => x.AccountId == account && x.StorageKey == stored.StorageKey)) await storage.DiscardUncommittedAsync(stored.StorageKey);
            throw;
        }
    }
}
