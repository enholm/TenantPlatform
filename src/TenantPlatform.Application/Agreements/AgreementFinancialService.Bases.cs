using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed partial class AgreementFinancialService
{
    private static void CheckBasisDirection(Agreement a, AgreementDirection expected)
    {
        if (!Enum.IsDefined(expected) || a.Direction != expected) throw new AgreementValidationException("ProcessingWrongDirection");
        if (a.Currency is null) throw new AgreementValidationException("FinanceSetupRequired");
    }
    private static List<AgreementCalculatedPeriod> CalculateSource(FinancialSource source, DateOnly from, DateOnly to) =>
        AgreementPeriodCalculator.Calculate(source.Agreement.Id, source.Agreement.Direction ?? throw new AgreementValidationException("FinanceSetupRequired"),
            source.Agreement.Currency ?? throw new AgreementValidationException("FinanceSetupRequired"), source.Versions, source.Prices, from, to, source.AutomaticPrices);
    private static List<AgreementCalculatedPeriod> InvoicePeriods(FinancialSource source, DateOnly from, DateOnly to)
    {
        if (from > to || from.Year < 3 || to.Year > 9996 || to.DayNumber - from.DayNumber > 366) throw new AgreementValidationException("FinanceInvalidRange");
        // Annual arrears may invoice the day after the entire prior year. Calculate whole periods,
        // not invoice-window slices, and retain all segments belonging to the selected event.
        var rows = CalculateSource(source, from.AddMonths(-13), to.AddMonths(13));
        var keys = rows.Where(x => !x.Included && x.InvoiceDate >= from && x.InvoiceDate <= to).Select(x => x.EventKey).ToHashSet();
        return rows.Where(x => !x.Included && keys.Contains(x.EventKey)).ToList();
    }
    public async Task<AgreementForecastDto> PreviewBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var session = await Open(accountId, agreementId, false, false, ct);
        var state = session.State; var a = state.Agreement; CheckBasisDirection(a, direction);
        var periods = InvoicePeriods(Source(state), from, to);
        return new(periods, direction == AgreementDirection.Income ? periods.Sum(x => x.Amount) : 0, direction == AgreementDirection.Cost ? periods.Sum(x => x.Amount) : 0, a.Currency!);
    }
    private AgreementBasisData BasisData(AgreementFinancialState state, FinancialSource source,
        List<AgreementCalculatedPeriod> periods, CancellationToken ct)
    {
        var a = source.Agreement;
        var accountName = state.AccountName; var party = state.CounterpartyName;
        var adjustmentIds = source.Prices.Where(x => periods.Any(p => p.PriceVersionId == x.Id) && x.AdjustmentId.HasValue).Select(x => x.AdjustmentId!.Value).ToArray();
        var adjustments = state.Proposals.Where(x => adjustmentIds.Contains(x.Id)).OrderBy(x => x.Id).ToList();
        var events = periods.GroupBy(x => x.EventKey).OrderBy(x => x.Key).Select(g => new AgreementBasisEventData(g.Key, g.First().LineId,
            g.OrderBy(x => x.From).ThenBy(x => x.PriceVersionId).ToList(), g.Sum(x => x.Amount), 0, g.Sum(x => x.Amount),
            adjustments.Where(x => source.Prices.Any(p => p.AdjustmentId == x.Id && g.Any(row => row.PriceVersionId == p.Id))).ToList())).ToList();
        return new(a.AccountId, accountName, a.Id, a.Title, a.CounterpartyOrganizationId, party, a.Direction!.Value, a.Currency!, "ProcessingTaxExclusive", events, []);
    }
    private static string BasisFingerprint(FinancialSource source, AgreementBasisData data) =>
        Fingerprint(new { Header = HeaderFingerprint(source.Agreement), source.Versions, source.Prices, data });
    private void AddBasisSnapshot(AgreementFinancialState state, AgreementBasis basis, FinancialSource source, AgreementBasisData data, string reason, Guid user)
    {
        basis.Revision++;
        state.Snapshots.Add(new() { Id = Guid.NewGuid(), AccountId = basis.AccountId, AgreementId = basis.AgreementId, BasisId = basis.Id,
            Revision = basis.Revision, Fingerprint = BasisFingerprint(source, data), DataJson = Json(data), RecordedUtc = clock.GetUtcNow(), ActorUserId = user, Reason = reason });
    }
    public async Task<AgreementBasisRun> GenerateBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var session = await Open(accountId, agreementId, true, false, ct);
        var state = session.State; var a = state.Agreement; var user = session.Scope.ListScope.UserId; CheckBasisDirection(a, direction);
        var source = Source(state); var periods = InvoicePeriods(source, from, to);
        var result = GenerateBasisCore(state, source, periods, user, ct);
        if (result.Created.Count > 0) await session.CommitAsync(ct); return result;
    }
    private AgreementBasisRun GenerateBasisCore(AgreementFinancialState state, FinancialSource source,
        List<AgreementCalculatedPeriod> periods, Guid user, CancellationToken ct)
    {
        var a = source.Agreement; var accountId = a.AccountId; var agreementId = a.Id; var direction = a.Direction!.Value;
        var claims = state.Events.Where(x => x.Active).ToList();
        var existing = claims.Where(x => periods.Any(p => p.EventKey == x.EventKey)).Select(x => x.BasisId).Distinct().ToList();
        var claimed = claims.Select(x => x.EventKey).ToHashSet(); var created = new List<Guid>();
        foreach (var group in periods.Where(x => !claimed.Contains(x.EventKey)).GroupBy(x => x.InvoiceDate).OrderBy(x => x.Key))
        {
            // A single logical event must never be split across separate invoice-date headers.
            if (group.Any(x => periods.Any(p => p.EventKey == x.EventKey && p.InvoiceDate != group.Key))) throw new AgreementValidationException("ProcessingPeriodLocked");
            var b = new AgreementBasis { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, Direction = direction, InvoiceDate = group.Key,
                GeneratedUtc = clock.GetUtcNow(), GeneratedByUserId = user };
            state.Bases.Add(b);
            var data = BasisData(state, source, group.ToList(), ct); AddBasisSnapshot(state, b, source, data, "ProcessingGenerated", user);
            foreach (var e in data.Events) state.Events.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, BasisId = b.Id, LineId = e.LineId, EventKey = e.EventKey });
            created.Add(b.Id);
        }
        if (created.Count > 0) Touch(a, user);
        return new(created, existing);
    }
    private static AgreementBasisSnapshot LatestBasisSnapshot(AgreementFinancialState state, AgreementBasis b, CancellationToken ct) =>
        state.Snapshots.Single(x => x.BasisId == b.Id && x.Revision == b.Revision);
    private AgreementBasisData CurrentBasisData(AgreementFinancialState state, FinancialSource source, AgreementBasis b, CancellationToken ct)
    {
        var original = b.OriginalBasisId.HasValue
            ? state.Bases.Single(x => x.Id == b.OriginalBasisId) : b;
        var originalSnapshot = LatestBasisSnapshot(state, original, ct);
        var old = ReadJson<AgreementBasisData>(originalSnapshot.DataJson);
        var rows = new List<AgreementCalculatedPeriod>();
        foreach (var e in old.Events)
        {
            // A regenerated zero event retains its first historical snapshot as a date anchor.
            var anchor = e.Segments;
            if (anchor.Count == 0)
            {
                var first = state.Snapshots.Where(x => x.BasisId == original.Id).OrderBy(x => x.Revision).First();
                anchor = ReadJson<AgreementBasisData>(first.DataJson).Events.Single(x => x.EventKey == e.EventKey).Segments;
            }
            rows.AddRange(CalculateSource(source, anchor.Min(x => x.ReferenceFrom), anchor.Max(x => x.ReferenceTo)).Where(x => x.EventKey == e.EventKey && !x.Included));
        }
        var target = BasisData(state, source, rows, ct);
        var events = old.Events.Select(e => target.Events.FirstOrDefault(x => x.EventKey == e.EventKey) ?? new(e.EventKey, e.LineId, [], 0, 0, 0, [])).ToList();
        if (b.OriginalBasisId.HasValue)
        {
            if (original.Status != AgreementBasisStatus.Approved) throw new AgreementValidationException("ProcessingOriginalRequired");
            var corrections = state.Bases.Where(x => x.OriginalBasisId == original.Id && x.Status == AgreementBasisStatus.Approved).OrderBy(x => x.Id).ToList();
            var net = old.Events.ToDictionary(x => x.EventKey, x => x.Amount); var snapshotIds = new List<Guid> { originalSnapshot.Id };
            foreach (var correction in corrections)
            {
                var snap = LatestBasisSnapshot(state, correction, ct); snapshotIds.Add(snap.Id);
                foreach (var e in ReadJson<AgreementBasisData>(snap.DataJson).Events) net[e.EventKey] += e.Amount;
            }
            events = events.Select(e => e with { PreviousAmount = net[e.EventKey], TargetAmount = e.Amount, Amount = e.Amount - net[e.EventKey] }).ToList();
            return target with { Events = events, PreviousSnapshotIds = snapshotIds };
        }
        return target with { Events = events };
    }
    public async Task<List<AgreementBasisSummary>> ListBasisAsync(Guid accountId, AgreementDirection direction, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(direction)) throw new AgreementValidationException("ProcessingWrongDirection");
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        var rows = await data.ReadBasisListAsync(scope, direction, ct);
        var result = rows.Select(row => { var payload = ReadJson<AgreementBasisData>(row.Snapshot.DataJson); return new AgreementBasisSummary(row.Basis, payload.AgreementTitle, payload.CounterpartyName, payload.Currency, payload.Events.Sum(x => x.Amount)); }).ToList();
        return result;
    }
    public async Task<List<AgreementBasisDetails>> GetAffectedBasesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var session = await Open(accountId, agreementId, false, false, ct);
        var ids = session.State.Bases.Where(x => x.Status != AgreementBasisStatus.Cancelled).OrderBy(x => x.InvoiceDate).Select(x => x.Id).ToList();
        var affected = new List<AgreementBasisDetails>();
        foreach (var id in ids)
        {
            var details = await GetBasisAsync(accountId, id, ct);
            if (details.Stale || details.NeedsCorrection) affected.Add(details);
        }
        return affected;
    }
    public async Task<AgreementBasisDetails> GetBasisAsync(Guid accountId, Guid basisId, CancellationToken ct = default)
    {
        await using var session = await OpenBasis(accountId, basisId, false, false, ct);
        var state = session.State; var a = state.Agreement; var b = state.Bases.Single(x => x.Id == basisId);
        var edit = session.Scope.CanEdit; var manage = session.Scope.CanManageAccess;
        var snaps = state.Snapshots.Where(x => x.BasisId == b.Id).OrderByDescending(x => x.Revision).ToList();
        var stale = false; var needs = false;
        if (b.Status != AgreementBasisStatus.Cancelled)
        {
            var source = Source(state);
            try
            {
                var current = CurrentBasisData(state, source, b, ct);
                stale = b.Status == AgreementBasisStatus.Draft && BasisFingerprint(source, current) != snaps[0].Fingerprint;
                if (b.Status == AgreementBasisStatus.Approved && !b.OriginalBasisId.HasValue)
                {
                    var hypothetical = new AgreementBasis { AccountId = accountId, AgreementId = a.Id, OriginalBasisId = b.Id };
                    needs = (CurrentBasisData(state, source, hypothetical, ct)).Events.Any(x => x.Amount != 0);
                }
            }
            catch (AgreementValidationException) { stale = true; }
        }
        return new(b, snaps, stale, needs, edit && !a.IsArchived, manage && !a.IsArchived);
    }
    public async Task RegenerateBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var session = await OpenBasis(accountId, basisId, true, false, ct);
        var state = session.State; var a = state.Agreement; var b = state.Bases.Single(x => x.Id == basisId); var user = session.Scope.ListScope.UserId;
        if (b.Status != AgreementBasisStatus.Draft || b.Revision != revision) throw new AgreementValidationException("ProcessingStale");
        var source = Source(state); var current = CurrentBasisData(state, source, b, ct);
        if (!b.OriginalBasisId.HasValue && current.Events.SelectMany(x => x.Segments).Any(x => x.InvoiceDate != b.InvoiceDate)) throw new AgreementValidationException("ProcessingInvoiceDateChanged");
        AddBasisSnapshot(state, b, source, current, reason.Trim(), user); Touch(a, user); await session.CommitAsync(ct);
    }
    public async Task ApproveBasisAsync(Guid accountId, Guid basisId, int revision, CancellationToken ct = default)
    {
        var result = await ApproveBasisCoreAsync(accountId, basisId, revision, ct);
        if (!result.ApprovedNow && result.Revision != revision) throw new AgreementValidationException("ProcessingStale");
    }
    private async Task<(bool ApprovedNow, int Revision)> ApproveBasisCoreAsync(Guid accountId, Guid basisId, int revision, CancellationToken ct)
    {
        await using var session = await OpenBasis(accountId, basisId, true, true, ct);
        var state = session.State; var a = state.Agreement; var b = state.Bases.Single(x => x.Id == basisId); var user = session.Scope.ListScope.UserId;
        if (b.Status == AgreementBasisStatus.Approved) return (false, b.Revision);
        if (b.Status != AgreementBasisStatus.Draft || b.Revision != revision) throw new AgreementValidationException("ProcessingStale");
        var source = Source(state); var current = CurrentBasisData(state, source, b, ct);
        if (BasisFingerprint(source, current) != (LatestBasisSnapshot(state, b, ct)).Fingerprint) throw new AgreementValidationException("ProcessingStale");
        b.Status = AgreementBasisStatus.Approved; b.ApprovedUtc = clock.GetUtcNow(); b.ApprovedByUserId = user;
        Touch(a, user); await session.CommitAsync(ct);
        return (true, b.Revision);
    }
    public async Task CancelBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var session = await OpenBasis(accountId, basisId, true, true, ct);
        var state = session.State; var a = state.Agreement; var b = state.Bases.Single(x => x.Id == basisId); var user = session.Scope.ListScope.UserId;
        if (b.Status == AgreementBasisStatus.Cancelled) return;
        if (b.Revision != revision) throw new AgreementValidationException("ProcessingStale");
        if (state.Bases.Any(x => x.OriginalBasisId == b.Id && x.Status != AgreementBasisStatus.Cancelled))
            throw new AgreementValidationException("ProcessingCancelCorrectionsFirst");
        b.Status = AgreementBasisStatus.Cancelled; b.CancelledUtc = clock.GetUtcNow(); b.CancelledByUserId = user; b.Reason = reason.Trim();
        foreach (var claim in state.Events.Where(x => x.BasisId == b.Id && x.Active).ToList()) claim.Active = false;
        Touch(a, user); await session.CommitAsync(ct);
    }
    public async Task<Guid> CreateCorrectionAsync(Guid accountId, Guid originalId, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var session = await OpenBasis(accountId, originalId, true, false, ct);
        var state = session.State; var a = state.Agreement; var original = state.Bases.Single(x => x.Id == originalId); var user = session.Scope.ListScope.UserId;
        if (original.Status != AgreementBasisStatus.Approved || original.OriginalBasisId.HasValue) throw new AgreementValidationException("ProcessingOriginalRequired");
        var existing = state.Bases.SingleOrDefault(x => x.OriginalBasisId == originalId && x.Status == AgreementBasisStatus.Draft);
        if (existing != null) return existing.Id;
        var correction = new AgreementBasis { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = a.Id, Direction = original.Direction, InvoiceDate = original.InvoiceDate,
            OriginalBasisId = originalId, GeneratedUtc = clock.GetUtcNow(), GeneratedByUserId = user, Reason = reason.Trim() };
        var source = Source(state); var data = CurrentBasisData(state, source, correction, ct);
        if (data.Events.All(x => x.Amount == 0)) throw new AgreementValidationException("ProcessingNoDifference");
        state.Bases.Add(correction); AddBasisSnapshot(state, correction, source, data, reason.Trim(), user);
        Touch(a, user); await session.CommitAsync(ct); return correction.Id;
    }
}
