using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed record AnalysisCleanupTarget(Guid AccountId, Guid DraftId);
public interface IAgreementAnalysisCleanupData
{
    Task<List<AnalysisCleanupTarget>> ReadCandidatesAsync(DateTimeOffset now, CancellationToken ct);
    Task<IAnalysisCleanupTransaction> BeginAsync(Guid account, Guid draft, CancellationToken ct);
    Task<List<string>> ReadOrphanCandidatesAsync(DateTimeOffset olderThan, CancellationToken ct);
    Task<bool> IsReferencedAsync(string key, CancellationToken ct);
    Task DeleteOrphanAsync(string key, CancellationToken ct);
}
public interface IAnalysisCleanupTransaction : IAsyncDisposable
{
    AgreementAnalysis? Draft { get; }
    Task CommitAsync(bool removeDraft, CancellationToken ct);
}
public sealed class AgreementAnalysisCleanupService(IAgreementAnalysisCleanupData data, IAgreementDocumentStorage storage,
    TimeProvider clock, ISystemWorkContext system)
{
    public async Task CleanDraftAsync(Guid accountId, Guid draftId, CancellationToken ct)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.AgreementAnalysisCleanup);
        await using var tx = await data.BeginAsync(accountId, draftId, ct);
        var draft = tx.Draft; var now = clock.GetUtcNow();
        if (draft is null || draft.State == AgreementAnalysisState.Approved || draft.LeaseUntilUtc > now) return;
        var discard = draft.State == AgreementAnalysisState.Discarded || draft.ExpiresUtc <= now;
        foreach (var file in draft.Files.Where(x => discard || x.PendingDelete).ToList())
        {
            if (file.AgreementDocumentId.HasValue) throw new InvalidOperationException("Cannot discard an archived document.");
            // Retain the draft lock through storage deletion. Failure leaves metadata for retry.
            await storage.DiscardUncommittedAsync(file.StorageKey); draft.Files.Remove(file);
        }
        await tx.CommitAsync(discard, ct);
    }
    public async Task RunAsync(CancellationToken ct)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.AgreementAnalysisCleanup);
        var candidates = await data.ReadCandidatesAsync(clock.GetUtcNow(), ct);
        foreach (var target in candidates) await CleanDraftAsync(target.AccountId, target.DraftId, ct);
    }
    public async Task CleanOrphansAsync(CancellationToken ct)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.AgreementAnalysisCleanup);
        var keys = await data.ReadOrphanCandidatesAsync(clock.GetUtcNow().AddDays(-2), ct);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            // Global reference protection is deliberate storage reconciliation, not tenant read access.
            if (!await data.IsReferencedAsync(key, ct)) await data.DeleteOrphanAsync(key, ct);
        }
    }
}
