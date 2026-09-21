using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
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
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, false, false, ct); CheckBasisDirection(a, direction);
        var periods = InvoicePeriods(await SourceAsync(db, a, ct), from, to);
        return new(periods, direction == AgreementDirection.Income ? periods.Sum(x => x.Amount) : 0, direction == AgreementDirection.Cost ? periods.Sum(x => x.Amount) : 0, a.Currency!);
    }
    private async Task<AgreementBasisData> BasisDataAsync(TenantPlatformDbContext db, FinancialSource source,
        List<AgreementCalculatedPeriod> periods, CancellationToken ct)
    {
        var a = source.Agreement;
        var accountName = await db.Accounts.Where(x => x.Id == a.AccountId).Select(x => x.Name).SingleAsync(ct);
        var party = await db.Organizations.Where(x => x.AccountId == a.AccountId && x.Id == a.CounterpartyOrganizationId).Select(x => x.Name).SingleAsync(ct);
        var adjustmentIds = source.Prices.Where(x => periods.Any(p => p.PriceVersionId == x.Id) && x.AdjustmentId.HasValue).Select(x => x.AdjustmentId!.Value).ToArray();
        var adjustments = await db.AgreementAdjustmentProposalRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && adjustmentIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(ct);
        var events = periods.GroupBy(x => x.EventKey).OrderBy(x => x.Key).Select(g => new AgreementBasisEventData(g.Key, g.First().LineId,
            g.OrderBy(x => x.From).ThenBy(x => x.PriceVersionId).ToList(), g.Sum(x => x.Amount), 0, g.Sum(x => x.Amount),
            adjustments.Where(x => source.Prices.Any(p => p.AdjustmentId == x.Id && g.Any(row => row.PriceVersionId == p.Id))).ToList())).ToList();
        return new(a.AccountId, accountName, a.Id, a.Title, a.CounterpartyOrganizationId, party, a.Direction!.Value, a.Currency!, "ProcessingTaxExclusive", events, []);
    }
    private static string BasisFingerprint(FinancialSource source, AgreementBasisData data) =>
        Fingerprint(new { Header = HeaderFingerprint(source.Agreement), source.Versions, source.Prices, data });
    private void AddBasisSnapshot(TenantPlatformDbContext db, AgreementBasis basis, FinancialSource source, AgreementBasisData data, string reason)
    {
        basis.Revision++;
        db.AgreementBasisSnapshotRecords.Add(new() { Id = Guid.NewGuid(), AccountId = basis.AccountId, AgreementId = basis.AgreementId, BasisId = basis.Id,
            Revision = basis.Revision, Fingerprint = BasisFingerprint(source, data), DataJson = Json(data), RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId, Reason = reason });
    }
    public async Task<AgreementBasisRun> GenerateBasisAsync(Guid accountId, Guid agreementId, AgreementDirection direction, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, agreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, ct); CheckBasisDirection(a, direction);
        var source = await SourceAsync(db, a, ct); var periods = InvoicePeriods(source, from, to);
        var result = await GenerateBasisCoreAsync(db, source, periods, ct);
        await tx.CommitAsync(ct); return result;
    }
    private async Task<AgreementBasisRun> GenerateBasisCoreAsync(TenantPlatformDbContext db, FinancialSource source,
        List<AgreementCalculatedPeriod> periods, CancellationToken ct)
    {
        var a = source.Agreement; var accountId = a.AccountId; var agreementId = a.Id; var direction = a.Direction!.Value;
        var claims = await db.AgreementBasisEventRecords.Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Active).ToListAsync(ct);
        var existing = claims.Where(x => periods.Any(p => p.EventKey == x.EventKey)).Select(x => x.BasisId).Distinct().ToList();
        var claimed = claims.Select(x => x.EventKey).ToHashSet(); var created = new List<Guid>();
        foreach (var group in periods.Where(x => !claimed.Contains(x.EventKey)).GroupBy(x => x.InvoiceDate).OrderBy(x => x.Key))
        {
            // A single logical event must never be split across separate invoice-date headers.
            if (group.Any(x => periods.Any(p => p.EventKey == x.EventKey && p.InvoiceDate != group.Key))) throw new AgreementValidationException("ProcessingPeriodLocked");
            var b = new AgreementBasis { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, Direction = direction, InvoiceDate = group.Key,
                GeneratedUtc = Clock.GetUtcNow(), GeneratedByUserId = userContext.Current.UserId };
            db.AgreementBasisRecords.Add(b);
            var data = await BasisDataAsync(db, source, group.ToList(), ct); AddBasisSnapshot(db, b, source, data, "ProcessingGenerated");
            foreach (var e in data.Events) db.AgreementBasisEventRecords.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, BasisId = b.Id, LineId = e.LineId, EventKey = e.EventKey });
            created.Add(b.Id);
        }
        if (created.Count > 0) { Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); }
        return new(created, existing);
    }
    private static async Task<AgreementBasisSnapshot> LatestBasisSnapshotAsync(TenantPlatformDbContext db, AgreementBasis b, CancellationToken ct) =>
        await db.AgreementBasisSnapshotRecords.AsNoTracking().SingleAsync(x => x.AccountId == b.AccountId && x.AgreementId == b.AgreementId && x.BasisId == b.Id && x.Revision == b.Revision, ct);
    private async Task<AgreementBasisData> CurrentBasisDataAsync(TenantPlatformDbContext db, FinancialSource source, AgreementBasis b, CancellationToken ct)
    {
        var original = b.OriginalBasisId.HasValue
            ? await db.AgreementBasisRecords.SingleAsync(x => x.AccountId == b.AccountId && x.AgreementId == b.AgreementId && x.Id == b.OriginalBasisId, ct) : b;
        var originalSnapshot = await LatestBasisSnapshotAsync(db, original, ct);
        var old = ReadJson<AgreementBasisData>(originalSnapshot.DataJson);
        var rows = new List<AgreementCalculatedPeriod>();
        foreach (var e in old.Events)
        {
            // A regenerated zero event retains its first historical snapshot as a date anchor.
            var anchor = e.Segments;
            if (anchor.Count == 0)
            {
                var first = await db.AgreementBasisSnapshotRecords.AsNoTracking().Where(x => x.AccountId == b.AccountId && x.AgreementId == b.AgreementId && x.BasisId == original.Id)
                    .OrderBy(x => x.Revision).FirstAsync(ct);
                anchor = ReadJson<AgreementBasisData>(first.DataJson).Events.Single(x => x.EventKey == e.EventKey).Segments;
            }
            rows.AddRange(CalculateSource(source, anchor.Min(x => x.ReferenceFrom), anchor.Max(x => x.ReferenceTo)).Where(x => x.EventKey == e.EventKey && !x.Included));
        }
        var target = await BasisDataAsync(db, source, rows, ct);
        var events = old.Events.Select(e => target.Events.FirstOrDefault(x => x.EventKey == e.EventKey) ?? new(e.EventKey, e.LineId, [], 0, 0, 0, [])).ToList();
        if (b.OriginalBasisId.HasValue)
        {
            if (original.Status != AgreementBasisStatus.Approved) throw new AgreementValidationException("ProcessingOriginalRequired");
            var corrections = await db.AgreementBasisRecords.AsNoTracking().Where(x => x.AccountId == b.AccountId && x.AgreementId == b.AgreementId && x.OriginalBasisId == original.Id && x.Status == AgreementBasisStatus.Approved).OrderBy(x => x.Id).ToListAsync(ct);
            var net = old.Events.ToDictionary(x => x.EventKey, x => x.Amount); var snapshotIds = new List<Guid> { originalSnapshot.Id };
            foreach (var correction in corrections)
            {
                var snap = await LatestBasisSnapshotAsync(db, correction, ct); snapshotIds.Add(snap.Id);
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
        await using var db = await factory.CreateDbContextAsync(ct); var (user, admin) = await RequireMemberAsync(db, accountId, ct);
        var visible = Accessible(db, accountId, user, admin).Select(x => x.Id);
        var bases = await db.AgreementBasisRecords.AsNoTracking().Where(x => x.AccountId == accountId && x.Direction == direction && visible.Contains(x.AgreementId)).OrderByDescending(x => x.GeneratedUtc).Take(500).ToListAsync(ct);
        var result = new List<AgreementBasisSummary>();
        foreach (var b in bases)
        {
            var data = ReadJson<AgreementBasisData>((await LatestBasisSnapshotAsync(db, b, ct)).DataJson);
            result.Add(new(b, data.AgreementTitle, data.CounterpartyName, data.Currency, data.Events.Sum(x => x.Amount)));
        }
        return result;
    }
    public async Task<List<AgreementBasisDetails>> GetAffectedBasesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await RequireAsync(db, accountId, agreementId, false, false, ct);
        var ids = await db.AgreementBasisRecords.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Status != AgreementBasisStatus.Cancelled)
            .OrderBy(x => x.InvoiceDate).Select(x => x.Id).ToListAsync(ct);
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
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        await RequireMemberAsync(db, accountId, ct);
        var b = await db.AgreementBasisRecords.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == basisId, ct) ?? throw new UnauthorizedAccessException();
        var (a, edit, manage) = await RequireAsync(db, accountId, b.AgreementId, false, false, ct);
        var snaps = await db.AgreementBasisSnapshotRecords.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == a.Id && x.BasisId == b.Id).OrderByDescending(x => x.Revision).ToListAsync(ct);
        var stale = false; var needs = false;
        if (b.Status != AgreementBasisStatus.Cancelled)
        {
            var source = await SourceAsync(db, a, ct);
            try
            {
                var current = await CurrentBasisDataAsync(db, source, b, ct);
                stale = b.Status == AgreementBasisStatus.Draft && BasisFingerprint(source, current) != snaps[0].Fingerprint;
                if (b.Status == AgreementBasisStatus.Approved && !b.OriginalBasisId.HasValue)
                {
                    var hypothetical = new AgreementBasis { AccountId = accountId, AgreementId = a.Id, OriginalBasisId = b.Id };
                    needs = (await CurrentBasisDataAsync(db, source, hypothetical, ct)).Events.Any(x => x.Amount != 0);
                }
            }
            catch (AgreementValidationException) { stale = true; }
        }
        return new(b, snaps, stale, needs, edit && !a.IsArchived, manage && !a.IsArchived);
    }
    public async Task RegenerateBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, null, ct);
        var b = await db.AgreementBasisRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == basisId, ct) ?? throw new UnauthorizedAccessException();
        await LockFinancialAsync(db, accountId, b.AgreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, b.AgreementId, true, false, ct);
        if (b.Status != AgreementBasisStatus.Draft || b.Revision != revision) throw new AgreementValidationException("ProcessingStale");
        var source = await SourceAsync(db, a, ct); var current = await CurrentBasisDataAsync(db, source, b, ct);
        if (!b.OriginalBasisId.HasValue && current.Events.SelectMany(x => x.Segments).Any(x => x.InvoiceDate != b.InvoiceDate)) throw new AgreementValidationException("ProcessingInvoiceDateChanged");
        AddBasisSnapshot(db, b, source, current, reason.Trim()); Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
    public async Task ApproveBasisAsync(Guid accountId, Guid basisId, int revision, CancellationToken ct = default)
    {
        var result = await ApproveBasisCoreAsync(accountId, basisId, revision, ct);
        if (!result.ApprovedNow && result.Revision != revision) throw new AgreementValidationException("ProcessingStale");
    }
    private async Task<(bool ApprovedNow, int Revision)> ApproveBasisCoreAsync(Guid accountId, Guid basisId, int revision, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, null, ct);
        var b = await db.AgreementBasisRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == basisId, ct) ?? throw new UnauthorizedAccessException();
        await LockFinancialAsync(db, accountId, b.AgreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, b.AgreementId, true, true, ct);
        if (b.Status == AgreementBasisStatus.Approved) return (false, b.Revision);
        if (b.Status != AgreementBasisStatus.Draft || b.Revision != revision) throw new AgreementValidationException("ProcessingStale");
        var source = await SourceAsync(db, a, ct); var current = await CurrentBasisDataAsync(db, source, b, ct);
        if (BasisFingerprint(source, current) != (await LatestBasisSnapshotAsync(db, b, ct)).Fingerprint) throw new AgreementValidationException("ProcessingStale");
        b.Status = AgreementBasisStatus.Approved; b.ApprovedUtc = Clock.GetUtcNow(); b.ApprovedByUserId = userContext.Current.UserId;
        Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct);
        return (true, b.Revision);
    }
    public async Task CancelBasisAsync(Guid accountId, Guid basisId, int revision, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, null, ct);
        var b = await db.AgreementBasisRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == basisId, ct) ?? throw new UnauthorizedAccessException();
        await LockFinancialAsync(db, accountId, b.AgreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, b.AgreementId, true, true, ct);
        if (b.Status == AgreementBasisStatus.Cancelled) return;
        if (b.Revision != revision) throw new AgreementValidationException("ProcessingStale");
        if (await db.AgreementBasisRecords.AnyAsync(x => x.AccountId == accountId && x.AgreementId == a.Id && x.OriginalBasisId == b.Id && x.Status != AgreementBasisStatus.Cancelled, ct))
            throw new AgreementValidationException("ProcessingCancelCorrectionsFirst");
        b.Status = AgreementBasisStatus.Cancelled; b.CancelledUtc = Clock.GetUtcNow(); b.CancelledByUserId = userContext.Current.UserId; b.Reason = reason.Trim();
        foreach (var claim in await db.AgreementBasisEventRecords.Where(x => x.AccountId == accountId && x.AgreementId == a.Id && x.BasisId == b.Id && x.Active).ToListAsync(ct)) claim.Active = false;
        Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
    public async Task<Guid> CreateCorrectionAsync(Guid accountId, Guid originalId, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, null, ct);
        var original = await db.AgreementBasisRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == originalId, ct) ?? throw new UnauthorizedAccessException();
        await LockFinancialAsync(db, accountId, original.AgreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, original.AgreementId, true, false, ct);
        if (original.Status != AgreementBasisStatus.Approved || original.OriginalBasisId.HasValue) throw new AgreementValidationException("ProcessingOriginalRequired");
        var existing = await db.AgreementBasisRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.AgreementId == a.Id && x.OriginalBasisId == originalId && x.Status == AgreementBasisStatus.Draft, ct);
        if (existing != null) return existing.Id;
        var correction = new AgreementBasis { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = a.Id, Direction = original.Direction, InvoiceDate = original.InvoiceDate,
            OriginalBasisId = originalId, GeneratedUtc = Clock.GetUtcNow(), GeneratedByUserId = userContext.Current.UserId, Reason = reason.Trim() };
        var source = await SourceAsync(db, a, ct); var data = await CurrentBasisDataAsync(db, source, correction, ct);
        if (data.Events.All(x => x.Amount == 0)) throw new AgreementValidationException("ProcessingNoDifference");
        db.AgreementBasisRecords.Add(correction); AddBasisSnapshot(db, correction, source, data, reason.Trim());
        Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct); return correction.Id;
    }
}
