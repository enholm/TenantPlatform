using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;

public sealed record BasisReadFilter(AgreementDirection Direction, Guid? AgreementId = null, AgreementBasisStatus? Status = null, DateOnly? From = null, DateOnly? To = null);
public enum BasisFreshness { NotEvaluated }
public sealed record StoredBasisSummary(Guid Id, Guid AgreementId, Guid? OriginalBasisId, AgreementDirection Direction,
    DateOnly InvoiceDate, AgreementBasisStatus Status, int CurrentRevision, int SnapshotRevision, DateTimeOffset GeneratedUtc,
    string AgreementTitle, Guid CounterpartyId, string CounterpartyName, string Currency, string TaxTreatment,
    decimal Amount, DateOnly? PeriodFrom, DateOnly? PeriodTo, int EventCount, int SegmentCount, BasisFreshness Freshness);
public sealed record StoredBasisSegment(Guid LineId, string LineName, Guid LineVersionId, Guid PriceVersionId,
    DateOnly ReferenceFrom, DateOnly ReferenceTo, DateOnly From, DateOnly To, DateOnly InvoiceDate,
    decimal Quantity, decimal UnitPrice, int ActiveDays, int ReferenceDays, decimal Amount, bool Included);
public sealed record StoredBasisEvent(Guid LineId, decimal Amount, decimal PreviousAmount, decimal TargetAmount, StoredBasisSegment[] Segments);
public sealed record StoredBasisDetail(StoredBasisSummary Summary, StoredBasisEvent[] Events);
// Private storage payloads are deliberately separate from returned contracts.
public sealed record BasisStorageRow(Guid Id, Guid AgreementId, Guid? OriginalBasisId, AgreementDirection Direction,
    DateOnly InvoiceDate, AgreementBasisStatus Status, int CurrentRevision, DateTimeOffset GeneratedUtc,
    int? SnapshotRevision, string? DataJson);
public sealed record BasisResourceReference(Guid Id, Guid AgreementId, Guid? LineId = null);
public sealed record BasisReferenceRequest(Guid[] Parties, Guid[] Lines, Guid[] Versions, Guid[] Prices, Guid[] Bases, Guid[] Snapshots);
public sealed record BasisReferenceFacts(Guid[] Parties, BasisResourceReference[] Lines, BasisResourceReference[] Versions,
    BasisResourceReference[] Prices, BasisResourceReference[] Bases, BasisResourceReference[] Snapshots);
public interface IAgreementBasisReadSession : IAsyncDisposable
{
    Task<FinancePage<BasisStorageRow>> ListAsync(BasisReadFilter filter, int page, CancellationToken ct);
    Task<BasisStorageRow> DetailAsync(Guid basis, int? revision, CancellationToken ct);
    Task<BasisReferenceFacts> ReferencesAsync(BasisReferenceRequest request, CancellationToken ct);
}
public interface IAgreementBasisReadData
{
    Task<Guid?> FindAgreementAsync(Guid account, Guid basis, CancellationToken ct);
    Task<IAgreementBasisReadSession> BeginAsync(AgreementListScope scope, CancellationToken ct);
    Task<Guid[]> VisibleAsync(AgreementListScope scope, Guid[] agreements, CancellationToken ct);
}
public sealed class AgreementBasisSnapshotException : Exception;
public sealed class AgreementBasisReader(IAuthorizationService authorization, IAgreementBasisReadData data)
{
    public const int MaxSnapshotCharacters = 65536, MaxEvents = 100, MaxSegments = 250, MaxPreviousSnapshots = 100;
    private static void Page(int page) { if (page is < 1 or > 10000) throw new ArgumentException(); }
    public async Task<FinancePage<StoredBasisSummary>> ListAsync(Guid account, BasisReadFilter filter, int page = 1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); Page(page);
        if (!Enum.IsDefined(filter.Direction) || filter.Status.HasValue && !Enum.IsDefined(filter.Status.Value) || filter.AgreementId == Guid.Empty ||
            filter.From == default(DateOnly) || filter.To == default(DateOnly) || filter.From > filter.To) throw new ArgumentException();
        var scope = await authorization.GetAgreementListScopeAsync(account, ct);
        if (filter.AgreementId is Guid agreement && (await authorization.GetAgreementReadScopeAsync(account, agreement, ct)).ListScope.UserId != scope.UserId) throw new UnauthorizedAccessException();
        await using var session = await data.BeginAsync(scope, ct);
        var rows = await session.ListAsync(filter, page, ct);
        var parsed = rows.Items.Select(r => BasisSnapshotProjection.Parse(account, r, ct)).ToArray();
        await Validate(session, rows.Items, parsed, ct);
        var fresh = await authorization.GetAgreementListScopeAsync(account, ct);
        if (fresh.UserId != scope.UserId) throw new UnauthorizedAccessException();
        var ids = rows.Items.Select(r => r.AgreementId).Distinct().ToArray();
        if (ids.Except(await data.VisibleAsync(fresh, ids, ct)).Any()) throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
        return new(rows.RequestedPage, rows.Page, rows.TotalCount, parsed.Select(p => p.Detail.Summary).ToArray());
    }
    public async Task<StoredBasisDetail> DetailAsync(Guid account, Guid basis, int? revision = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (basis == Guid.Empty || revision is < 1) throw new ArgumentException();
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        var agreement = await data.FindAgreementAsync(account, basis, ct) ?? throw new UnauthorizedAccessException();
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        if (scope.ListScope.UserId != actor.UserId) throw new UnauthorizedAccessException();
        await using var session = await data.BeginAsync(scope.ListScope, ct);
        var row = await session.DetailAsync(basis, revision, ct);
        if (row.AgreementId != agreement) throw new UnauthorizedAccessException();
        var parsed = BasisSnapshotProjection.Parse(account, row, ct);
        await Validate(session, [row], [parsed], ct);
        if ((await authorization.GetAgreementReadScopeAsync(account, agreement, ct)).ListScope.UserId != actor.UserId) throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
        return parsed.Detail;
    }
    private static async Task Validate(IAgreementBasisReadSession session, BasisStorageRow[] rows, BasisParsedSnapshot[] snapshots, CancellationToken ct)
    {
        var events = snapshots.SelectMany(p => p.Detail.Events).ToArray(); var segments = events.SelectMany(e => e.Segments).ToArray();
        var facts = await session.ReferencesAsync(new(snapshots.Select(p => p.Detail.Summary.CounterpartyId).Distinct().ToArray(),
            events.Select(e => e.LineId).Distinct().ToArray(), segments.Select(s => s.LineVersionId).Distinct().ToArray(), segments.Select(s => s.PriceVersionId).Distinct().ToArray(),
            rows.Where(r => r.OriginalBasisId.HasValue).Select(r => r.OriginalBasisId!.Value).Distinct().ToArray(), snapshots.SelectMany(p => p.PreviousSnapshots).Distinct().ToArray()), ct);
        foreach (var p in snapshots)
        {
            ct.ThrowIfCancellationRequested(); var s = p.Detail.Summary; var agreement = s.AgreementId;
            if (!facts.Parties.Contains(s.CounterpartyId) || s.OriginalBasisId is Guid original && !facts.Bases.Contains(new(original, agreement)) ||
                p.PreviousSnapshots.Any(id => !facts.Snapshots.Contains(new(id, agreement))) || p.Detail.Events.Any(e => !facts.Lines.Contains(new(e.LineId, agreement)) ||
                    e.Segments.Any(v => !facts.Versions.Contains(new(v.LineVersionId, agreement, e.LineId)) || !facts.Prices.Contains(new(v.PriceVersionId, agreement, e.LineId)))))
                throw new AgreementBasisSnapshotException();
        }
    }
}
