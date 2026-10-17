using System.Text.Json;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public enum LeasingDocumentParent { Framework, Acquisition, Order }
public sealed record LeasingDocumentTarget(LeasingParentFacts Parents, LeasingOrder? Order);
public interface ILeasingDocumentData
{
    Task<LeasingDocumentTarget> ReadTargetAsync(Guid account, Guid parent, LeasingDocumentParent kind, Guid actor, CancellationToken ct);
    Task<LeasingDocument?> ReadDocumentAsync(Guid account, Guid id, CancellationToken ct);
    Task<ILeasingDocumentTransaction> BeginAsync(Guid account, Guid parent, LeasingDocumentParent kind, Guid actor, CancellationToken ct);
    Task CleanupUncommittedAsync(Guid account, string key);
}
public interface ILeasingDocumentTransaction : IAsyncDisposable
{
    LeasingDocumentTarget Target { get; }
    Task CommitAsync(LeasingDocument document, LeasingHistory? history, LeasingOrderEvent? orderHistory, CancellationToken ct);
}
public sealed class LeasingDocumentService(IAuthorizationService authorization, ILeasingDocumentData data, IAgreementDocumentStorage storage, IPaymentPlanSourceReader sourceReader, TimeProvider clock)
{
    private async Task<LeasingReadScope> Actor(Guid account, CancellationToken ct) => LeasingReader.Scope(await authorization.GetAccountActorScopeAsync(account, ct));
    private static bool Owner(LeasingReadScope actor, LeasingDocumentTarget target, LeasingDocumentParent kind) => kind == LeasingDocumentParent.Order
        ? target.Order is not null && (actor.Administrator || target.Order.OwnerUserId == actor.UserId || target.Parents.Framework?.OwnerUserId == actor.UserId)
        : (kind == LeasingDocumentParent.Framework ? target.Parents.Framework is not null : target.Parents.Acquisition is not null) && LeasingReader.Owns(actor, target.Parents);
    private static void Edit(LeasingReadScope actor, LeasingDocumentTarget target, LeasingDocumentParent kind)
    {
        if (!Owner(actor, target, kind) || kind == LeasingDocumentParent.Acquisition && target.Parents.Acquisition!.Status == LeasingAcquisitionStatus.Cancelled) throw new UnauthorizedAccessException();
    }
    private static Guid Revision(LeasingDocumentTarget target, LeasingDocumentParent kind) => kind switch { LeasingDocumentParent.Framework => target.Parents.Framework!.Revision, LeasingDocumentParent.Acquisition => target.Parents.Acquisition!.Revision, _ => target.Order!.Revision };
    private static void CheckRevision(LeasingDocumentTarget target, LeasingDocumentParent kind, Guid revision) { if (Revision(target, kind) != revision) throw new LeasingValidationException("LeasingConcurrency"); }
    public Task UploadAsync(Guid account, Guid id, bool framework, Guid revision, string fileName, Stream content, CancellationToken ct = default) => Upload(account, id, framework ? LeasingDocumentParent.Framework : LeasingDocumentParent.Acquisition, revision, fileName, content, ct);
    public Task UploadOrderAsync(Guid account, Guid id, Guid revision, string fileName, Stream content, CancellationToken ct = default) => Upload(account, id, LeasingDocumentParent.Order, revision, fileName, content, ct);
    private async Task Upload(Guid account, Guid id, LeasingDocumentParent kind, Guid revision, string fileName, Stream content, CancellationToken ct)
    {
        var actor = await Actor(account, ct); var target = await data.ReadTargetAsync(account, id, kind, actor.UserId, ct);
        Edit(actor, target, kind); CheckRevision(target, kind, revision);
        var stored = await storage.StoreAsync(content, fileName, ct);
        try
        {
            await using var tx = await data.BeginAsync(account, id, kind, actor.UserId, ct);
            var current = await Actor(account, ct); if (current.UserId != actor.UserId) throw new UnauthorizedAccessException();
            Edit(current, tx.Target, kind); CheckRevision(tx.Target, kind, revision);
            var before = kind == LeasingDocumentParent.Order ? JsonSerializer.Serialize(tx.Target.Order) : "{}";
            if (kind == LeasingDocumentParent.Framework) tx.Target.Parents.Framework!.Revision = Guid.NewGuid();
            else if (kind == LeasingDocumentParent.Acquisition) tx.Target.Parents.Acquisition!.Revision = Guid.NewGuid();
            else tx.Target.Order!.Revision = Guid.NewGuid();
            var document = Document(account, id, kind, actor.UserId, stored);
            LeasingOrderEvent? orderHistory = kind == LeasingDocumentParent.Order ? new() { Id = Guid.NewGuid(), AccountId = account, OrderId = id, ActorUserId = actor.UserId, Action = "DocumentAdded", Reason = stored.FileName, BeforeJson = before, AfterJson = JsonSerializer.Serialize(tx.Target.Order), RecordedUtc = clock.GetUtcNow() } : null;
            await tx.CommitAsync(document, kind == LeasingDocumentParent.Order ? null : History(document, "DocumentAdded", stored.FileName, JsonSerializer.Serialize(new { document.Id, document.FileName, document.Size })), orderHistory, ct);
        }
        catch { await data.CleanupUncommittedAsync(account, stored.StorageKey); throw; }
    }
    public async Task<AgreementDownload> DownloadAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct);
        var document = await data.ReadDocumentAsync(account, id, ct) ?? throw new UnauthorizedAccessException();
        var kind = Kind(document); var parent = Parent(document);
        var target = await data.ReadTargetAsync(account, parent, kind, actor.UserId, ct);
        var allowed = kind switch
        {
            LeasingDocumentParent.Order => target.Order is not null && (actor.OrderApprover || Owner(actor, target, kind)),
            LeasingDocumentParent.Framework => Owner(actor, target, kind),
            _ => target.Parents.Acquisition is not null && (actor.DocumentReader || Owner(actor, target, kind))
        };
        if (!allowed) throw new UnauthorizedAccessException();
        return new(await storage.OpenReadAsync(document.StorageKey, ct), document.FileName, document.MediaType);
    }
    public async Task<(Guid Document, PaymentImportGrid Grid)> UploadPaymentPlanSourceAsync(Guid account, Guid acquisition, string fileName, Stream stream, CancellationToken ct = default)
    {
        var kind = LeasingDocumentParent.Acquisition; var actor = await Actor(account, ct);
        Edit(actor, await data.ReadTargetAsync(account, acquisition, kind, actor.UserId, ct), kind);
        await using var buffer = new MemoryStream(); var bytes = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(bytes, ct)) > 0) { if (buffer.Length + read > Math.Min(storage.MaxFileSizeBytes, 10_000_000)) throw new LeasingValidationException("PaymentImportSize"); await buffer.WriteAsync(bytes.AsMemory(0, read), ct); }
        buffer.Position = 0; var grid = sourceReader.Read(buffer, fileName); buffer.Position = 0;
        var stored = await storage.StoreAsync(buffer, fileName, ct);
        try
        {
            await using var tx = await data.BeginAsync(account, acquisition, kind, actor.UserId, ct);
            var current = await Actor(account, ct); if (current.UserId != actor.UserId) throw new UnauthorizedAccessException(); Edit(current, tx.Target, kind);
            var document = Document(account, acquisition, kind, actor.UserId, stored);
            await tx.CommitAsync(document, History(document, "PaymentSourceAdded", fileName, JsonSerializer.Serialize(new { document.Id, document.FileName })), null, ct);
            return (document.Id, grid);
        }
        catch { await data.CleanupUncommittedAsync(account, stored.StorageKey); throw; }
    }
    public async Task<PaymentImportGrid> ReadPaymentPlanSourceAsync(Guid account, Guid acquisition, Guid document, string? sheet = null, CancellationToken ct = default)
    {
        var actor = await Actor(account, ct); var kind = LeasingDocumentParent.Acquisition;
        var target = await data.ReadTargetAsync(account, acquisition, kind, actor.UserId, ct);
        if (target.Parents.Acquisition is null || !actor.PaymentReader && !Owner(actor, target, kind)) throw new UnauthorizedAccessException();
        var doc = await data.ReadDocumentAsync(account, document, ct) ?? throw new UnauthorizedAccessException();
        if (doc.AcquisitionId != acquisition) throw new UnauthorizedAccessException();
        await using var stream = await storage.OpenReadAsync(doc.StorageKey, ct);
        return sourceReader.Read(stream, doc.FileName, sheet);
    }
    private LeasingDocument Document(Guid account, Guid parent, LeasingDocumentParent kind, Guid actor, StoredAgreementFile stored) => new()
    {
        Id = Guid.NewGuid(), AccountId = account, FrameworkId = kind == LeasingDocumentParent.Framework ? parent : null, AcquisitionId = kind == LeasingDocumentParent.Acquisition ? parent : null, OrderId = kind == LeasingDocumentParent.Order ? parent : null,
        FileName = stored.FileName, StorageKey = stored.StorageKey, MediaType = stored.MediaType, Size = stored.Size, UploadedByUserId = actor, UploadedUtc = clock.GetUtcNow()
    };
    private LeasingHistory History(LeasingDocument document, string action, string reason, string after) => new() { Id = Guid.NewGuid(), AccountId = document.AccountId, FrameworkId = document.FrameworkId, AcquisitionId = document.AcquisitionId, ActorUserId = document.UploadedByUserId, RecordedUtc = clock.GetUtcNow(), Action = action, Reason = reason, BeforeJson = "{}", AfterJson = after };
    private static LeasingDocumentParent Kind(LeasingDocument doc) => doc.OrderId.HasValue ? LeasingDocumentParent.Order : doc.FrameworkId.HasValue ? LeasingDocumentParent.Framework : LeasingDocumentParent.Acquisition;
    private static Guid Parent(LeasingDocument doc) => doc.OrderId ?? doc.FrameworkId ?? doc.AcquisitionId ?? throw new UnauthorizedAccessException();
}
