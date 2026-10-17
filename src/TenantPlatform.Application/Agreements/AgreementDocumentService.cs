using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public interface IAgreementDocumentData
{
    Task<Guid?> ReadRevisionAsync(Guid account, Guid agreement, CancellationToken ct);
    Task<AgreementDocument?> ReadDocumentAsync(Guid account, Guid document, CancellationToken ct);
    Task<IAgreementDocumentTransaction> BeginAsync(Guid account, Guid agreement, CancellationToken ct);
    Task CleanupUncommittedAsync(Guid account, string key);
}
public interface IAgreementDocumentTransaction : IAsyncDisposable
{
    Agreement Agreement { get; }
    Task CommitAsync(AgreementDocument document, CancellationToken ct);
}
public sealed class AgreementDocumentService(IAuthorizationService authorization, IAgreementDocumentData data, IAgreementDocumentStorage storage, TimeProvider clock)
{
    public long MaxFileSizeBytes => storage.MaxFileSizeBytes;
    private async Task<Guid> RequireEdit(Guid account, Guid agreement, CancellationToken ct)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        if (scope.IsArchived && scope.CanEditBeforeArchive) throw new AgreementValidationException("FollowupArchived");
        if (!scope.CanEdit) throw new UnauthorizedAccessException();
        return scope.ListScope.UserId;
    }
    private static void Check(Guid? actual, Guid expected)
    { if (actual != expected) throw new AgreementValidationException("AgreementConcurrencyConflict"); }
    public async Task UploadAsync(Guid accountId, Guid agreementId, Guid revision, string fileName, Stream content, AgreementDocumentCategory category, string? description, CancellationToken cancellationToken = default)
    {
        var user = await RequireEdit(accountId, agreementId, cancellationToken);
        Check(await data.ReadRevisionAsync(accountId, agreementId, cancellationToken), revision);
        if (!Enum.IsDefined(category) || description?.Length > 2000) throw new AgreementValidationException("AgreementInvalidDocument");
        var stored = await storage.StoreAsync(content, fileName, cancellationToken);
        try
        {
            // No persistence context survives the stream. Reauthorize the final metadata transaction.
            await using var tx = await data.BeginAsync(accountId, agreementId, cancellationToken);
            if (await RequireEdit(accountId, agreementId, cancellationToken) != user) throw new UnauthorizedAccessException();
            Check(tx.Agreement.Revision, revision); var now = clock.GetUtcNow();
            var document = new AgreementDocument { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, OriginalFileName = stored.FileName,
                StorageKey = stored.StorageKey, MediaType = stored.MediaType, Size = stored.Size, Category = category, Description = description?.Trim(), UploadedUtc = now, UploadedByUserId = user };
            tx.Agreement.Revision = Guid.NewGuid(); tx.Agreement.UpdatedUtc = now; tx.Agreement.UpdatedByUserId = user;
            await tx.CommitAsync(document, cancellationToken);
        }
        catch { await data.CleanupUncommittedAsync(accountId, stored.StorageKey); throw; }
    }
    public async Task<AgreementDownload> DownloadAsync(Guid accountId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var actor = await authorization.GetAccountActorScopeAsync(accountId, cancellationToken);
        var doc = await data.ReadDocumentAsync(accountId, documentId, cancellationToken) ?? throw new UnauthorizedAccessException();
        var scope = await authorization.GetAgreementReadScopeAsync(accountId, doc.AgreementId, cancellationToken);
        if (scope.ListScope.UserId != actor.UserId) throw new UnauthorizedAccessException();
        return new(await storage.OpenReadAsync(doc.StorageKey, cancellationToken), doc.OriginalFileName, doc.MediaType);
    }
}
