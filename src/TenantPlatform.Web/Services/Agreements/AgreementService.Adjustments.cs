using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    private static T ReadJson<T>(string value) => JsonSerializer.Deserialize<T>(value)!;
    private static string Fingerprint<T>(T value)
    {
        // PostgreSQL numeric preserves column scale (3 becomes 3.000000). Hash mathematical
        // values, not formatting, so a persisted manual percentage remains approvable.
        using var document = JsonDocument.Parse(Json(value));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(document.RootElement, writer);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        static void Write(JsonElement element, Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var p in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal)) { writer.WritePropertyName(p.Name); Write(p.Value, writer); }
                    writer.WriteEndObject(); break;
                case JsonValueKind.Array:
                    writer.WriteStartArray(); foreach (var item in element.EnumerateArray()) Write(item, writer); writer.WriteEndArray(); break;
                case JsonValueKind.Number when element.TryGetDecimal(out var number):
                    writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: element.WriteTo(writer); break;
            }
        }
    }
    // All financial writers take the account lock first, then the agreement lock. Index corrections
    // cannot race approvals. Existing writers are also protected by the parent's revision check.
    private async Task LockFinancialAsync(TenantPlatformDbContext db, Guid accountId, Guid? agreementId, CancellationToken ct)
    {
        await RequireMemberAsync(db, accountId, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {accountId} FOR UPDATE", ct);
        if (agreementId.HasValue)
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM agreements WHERE \"AccountId\" = {accountId} AND \"Id\" = {agreementId.Value} FOR UPDATE", ct);
    }
    private static void ReasonRequired(string? reason)
    { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new AgreementValidationException("ProcessingReasonRequired"); }

    public async Task<AgreementIndexRegister> GetIndicesAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (_, admin) = await RequireMemberAsync(db, accountId, ct);
        return new(admin, await db.AgreementIndexRecords.AsNoTracking().Where(x => x.AccountId == accountId).OrderBy(x => x.Code).ToListAsync(ct),
            await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == accountId).OrderByDescending(x => x.Period).ThenByDescending(x => x.Revision).ToListAsync(ct));
    }
    public async Task<Guid> CreateIndexAsync(Guid accountId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || string.IsNullOrWhiteSpace(name) || name.Length > 200 || description.Length > 4000 ||
            string.IsNullOrWhiteSpace(source) || source.Length > 2000 || !Enum.IsDefined(resolution)) throw new AgreementValidationException("ProcessingInvalidIndex");
        var index = new AgreementIndex { Id = Guid.NewGuid(), AccountId = accountId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description.Trim(),
            Source = source.Trim(), Resolution = resolution, RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId };
        db.AgreementIndexRecords.Add(index); await SaveAsync(db, ct); return index.Id;
    }
    public async Task RecordIndexValueAsync(Guid accountId, Guid indexId, DateOnly period, decimal value, DateOnly? published, string reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockFinancialAsync(db, accountId, null, ct);
        if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
        var index = await db.AgreementIndexRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == indexId, ct) ?? throw new UnauthorizedAccessException();
        ReasonRequired(reason);
        if (value <= 0 || value > 100000000 || decimal.Round(value, 8) != value || period == default || period.Year > 9997 ||
            AgreementAdjustmentCalculator.Period(period, index.Resolution) != period) throw new AgreementValidationException("ProcessingInvalidIndex");
        var revision = await db.AgreementIndexValueRecords.Where(x => x.AccountId == accountId && x.IndexId == indexId && x.Period == period).Select(x => (int?)x.Revision).MaxAsync(ct) ?? 0;
        db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, IndexId = indexId, Period = period, Value = value,
            Revision = revision + 1, PublishedDate = published, Reason = reason.Trim(), RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId });
        await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
    public async Task<AgreementProcessingView> GetProcessingAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, edit, manage) = await RequireAsync(db, accountId, agreementId, false, false, ct);
        return new(edit && !a.IsArchived, manage && !a.IsArchived,
            await db.AgreementAdjustmentRuleRecords.AsNoTracking().Include(x => x.Documents).Where(x => x.AccountId == accountId && x.AgreementId == agreementId).OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct),
            await db.AgreementAdjustmentProposalRecords.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct));
    }
    public async Task<Guid> SaveAdjustmentRuleAsync(Guid accountId, Guid agreementId, Guid revision, AgreementAdjustmentRule input, CancellationToken ct = default)
    {
        // Treat the domain-shaped editor as untrusted input: identity, scope and audit are server-owned.
        var r = ReadJson<AgreementAdjustmentRule>(Json(input));
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockFinancialAsync(db, accountId, agreementId, ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, ct); CheckRevision(a, revision); ReasonRequired(r.Reason);
        if (!Enum.IsDefined(r.Kind) || !Enum.IsDefined(r.Basis) || !Enum.IsDefined(r.Addition) || !Enum.IsDefined(r.Anchor) || !Enum.IsDefined(r.PriceTiming) ||
            r.IntervalMonths is < 1 or > 120 || r.ComparisonOffsetMonths is < -120 or > 120 || r.EffectiveFrom == default || r.EffectiveFrom.Year > 9997 ||
            r.FirstAllowedDate < r.EffectiveFrom || r.FirstAllowedDate.Year > 9997 || r.AnchorDate == default || r.AnchorDate.Year > 9997 ||
            (r.Anchor == AgreementAdjustmentAnchor.AnnualDate && r.IntervalMonths != 12) || r.FloorPercent > r.CeilingPercent ||
            r.FloorPercent < -100 || r.FloorPercent > 10000 || r.CeilingPercent < -100 || r.CeilingPercent > 10000 || (!r.AllowDecrease && r.CeilingPercent < 0) || r.SharePercent is < 0 or > 100 ||
            Math.Abs(r.FixedPercent) > 10000 || Math.Abs(r.AdditionPercent) > 10000 ||
            new decimal?[] { r.SharePercent, r.AdditionPercent, r.FixedPercent, r.FloorPercent, r.CeilingPercent }.Any(x => x.HasValue && decimal.Round(x.Value, 6) != x))
            throw new AgreementValidationException("ProcessingInvalidRule");
        var line = await db.AgreementLines.SingleOrDefaultAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Id == r.LineId && x.ActivatedUtc != null, ct)
            ?? throw new AgreementValidationException("FinanceInvalidReference");
        var price = await db.AgreementPriceVersions.SingleOrDefaultAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.LineId == line.Id && x.Id == r.BasePriceVersionId, ct)
            ?? throw new AgreementValidationException("FinanceInvalidReference");
        if (!price.Independent && !await db.AgreementLineVersions.AnyAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.LineId == line.Id && x.Sequence == price.Sequence && x.Status != AgreementLineStatus.Draft, ct))
            throw new AgreementValidationException("FinanceInvalidReference");
        if (await db.AgreementAdjustmentRuleRecords.AnyAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.LineId == r.LineId && x.EffectiveFrom >= r.EffectiveFrom, ct))
            throw new AgreementValidationException("ProcessingRuleOrder");
        if (r.Kind == AgreementAdjustmentKind.Index)
        {
            var index = await db.AgreementIndexRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == r.IndexId, ct) ?? throw new AgreementValidationException("FinanceInvalidReference");
            if (r.BaseIndexPeriod == default || AgreementAdjustmentCalculator.Period(r.BaseIndexPeriod, index.Resolution) != r.BaseIndexPeriod)
                throw new AgreementValidationException("ProcessingInvalidIndex");
        }
        else
        {
            if (r.IndexId.HasValue || r.Addition != AgreementAdjustmentAddition.None || r.Basis != AgreementAdjustmentBasis.Latest)
                throw new AgreementValidationException("ProcessingInvalidRule");
            if (r.Kind == AgreementAdjustmentKind.Limit && !r.CeilingPercent.HasValue) throw new AgreementValidationException("ProcessingInvalidRule");
        }
        var ids = r.Documents.Select(x => x.DocumentId).Distinct().Order().ToArray();
        if (ids.Length > 100 || await db.AgreementDocuments.CountAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && ids.Contains(x.Id), ct) != ids.Length)
            throw new AgreementValidationException("FinanceInvalidReference");
        r.Id = Guid.NewGuid(); r.AccountId = accountId; r.AgreementId = agreementId; r.RecordedUtc = Clock.GetUtcNow(); r.ActorUserId = userContext.Current.UserId;
        r.Documents = ids.Select(id => new AgreementAdjustmentDocument { AccountId = accountId, AgreementId = agreementId, RuleId = r.Id, DocumentId = id }).ToList();
        db.AgreementAdjustmentRuleRecords.Add(r); Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct); return r.Id;
    }
    private sealed record FinancialSource(Agreement Agreement, List<AgreementLineVersion> Versions, List<AgreementPriceVersion> Prices);
    private static async Task<FinancialSource> SourceAsync(TenantPlatformDbContext db, Agreement a, CancellationToken ct)
    {
        var versions = await db.AgreementLineVersions.AsNoTracking().Include(x => x.Documents).Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.Status != AgreementLineStatus.Draft)
            .OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToListAsync(ct);
        foreach (var v in versions) v.Documents = v.Documents.OrderBy(x => x.DocumentId).ToList();
        var sequences = versions.Select(x => (x.LineId, x.Sequence)).ToHashSet();
        var prices = await db.AgreementPriceVersions.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToListAsync(ct);
        return new(a, versions, prices.Where(x => x.Independent || sequences.Contains((x.LineId, x.Sequence))).ToList());
    }
    private static object HeaderFingerprint(Agreement a) => new { a.Id, a.AccountId, a.Title, a.CounterpartyOrganizationId, a.Currency, a.Direction, a.StartDate, a.EndDate, a.Terms, a.Status, a.IsArchived };
    private async Task<AgreementAdjustmentCalculation> CalculateAdjustmentAsync(TenantPlatformDbContext db, Agreement a, Guid lineId, DateOnly date, decimal? chosen, CancellationToken ct)
    {
        if (date == default || date.Year > 9997 || (chosen.HasValue && decimal.Round(chosen.Value, 6) != chosen)) throw new AgreementValidationException("FinanceInvalidDates");
        var source = await SourceAsync(db, a, ct);
        var r = await db.AgreementAdjustmentRuleRecords.AsNoTracking().Include(x => x.Documents).Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.LineId == lineId && x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct) ?? throw new AgreementValidationException("ProcessingMissingRule");
        r.Documents = r.Documents.OrderBy(x => x.DocumentId).ToList();
        if (!AgreementAdjustmentCalculator.IsScheduled(r, a.StartDate, date)) throw new AgreementValidationException("ProcessingNotDue");
        var applied = await db.AgreementAdjustmentProposalRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.LineId == lineId && x.Status == AgreementProposalStatus.Approved)
            .OrderByDescending(x => x.ScheduledDate).ToListAsync(ct);
        if (applied.Count > 0 && (date <= applied[0].ScheduledDate || date < applied[0].ScheduledDate.AddMonths(r.IntervalMonths)))
            throw new AgreementValidationException("ProcessingAlreadyAdjusted");
        var line = source.Versions.Where(x => x.LineId == lineId && x.EffectiveFrom <= date).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Sequence).FirstOrDefault()
            ?? throw new AgreementValidationException("FinanceInvalidReference");
        if (line.Status != AgreementLineStatus.Active || line.Frequency == AgreementFrequency.Once || date < line.StartDate || date > line.EndDate)
            throw new AgreementValidationException("FinanceLineClosed");
        var effective = r.PriceTiming == AgreementPriceTiming.NextPeriod && !AgreementPeriodCalculator.IsBoundary(line, date) ? AgreementPeriodCalculator.Reference(line, date).Until : date;
        var effectiveLine = source.Versions.Where(x => x.LineId == lineId && x.EffectiveFrom <= effective).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Sequence).First();
        if (effectiveLine.Id != line.Id || effective > line.EndDate) throw new AgreementValidationException("ProcessingNotDue");
        var current = source.Prices.Where(x => x.LineId == lineId && x.EffectiveFrom <= effective).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Sequence).FirstOrDefault()
            ?? throw new AgreementValidationException("FinanceMissingPrice");
        var basis = r.Basis == AgreementAdjustmentBasis.Original ? source.Prices.Single(x => x.Id == r.BasePriceVersionId) : current;
        if (basis.EffectiveFrom > effective) throw new AgreementValidationException("ProcessingInvalidRule");
        AgreementIndexValue? baseValue = null, comparison = null;
        if (r.Kind == AgreementAdjustmentKind.Index)
        {
            var index = await db.AgreementIndexRecords.AsNoTracking().SingleAsync(x => x.AccountId == a.AccountId && x.Id == r.IndexId, ct);
            var basisPeriod = r.BaseIndexPeriod;
            AgreementIndexValue? carriedBase = null;
            if (r.Basis == AgreementAdjustmentBasis.Latest)
            {
                var previous = applied.FirstOrDefault(x => x.RuleId == r.Id);
                if (previous is not null && current.AdjustmentId == previous.Id)
                {
                    carriedBase = ReadJson<AgreementAdjustmentCalculation>(previous.CalculationJson).ComparisonIndex!;
                    basisPeriod = carriedBase.Period;
                }
                else if (current.Id != r.BasePriceVersionId)
                    throw new AgreementValidationException("ProcessingPriceIndexBasisChanged");
            }
            DateOnly comparisonPeriod;
            try { comparisonPeriod = AgreementAdjustmentCalculator.Period(date.AddMonths(r.ComparisonOffsetMonths), index.Resolution); }
            catch (ArgumentOutOfRangeException) { throw new AgreementValidationException("FinanceInvalidDates"); }
            baseValue = carriedBase ?? await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.IndexId == r.IndexId && x.Period == basisPeriod).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
            comparison = await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.IndexId == r.IndexId && x.Period == comparisonPeriod).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
        }
        var calc = AgreementAdjustmentCalculator.Calculate(r, basis.UnitPrice, baseValue?.Value, comparison?.Value, chosen);
        var hash = Fingerprint(new { Header = HeaderFingerprint(a), r, source.Versions, source.Prices, baseValue, comparison, date, effective, chosen });
        return new(r, basis, current, line, baseValue, comparison, date, effective, calc.RawIndexPercent, calc.AppliedPercent, calc.Price, hash);
    }
    public async Task<List<AgreementAdjustmentCandidate>> FindAdjustmentsAsync(Guid accountId, Guid agreementId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (from > to || to.DayNumber - from.DayNumber > 366 || from == default || to.Year > 9997) throw new AgreementValidationException("FinanceInvalidRange");
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, false, false, ct);
        var rules = await db.AgreementAdjustmentRuleRecords.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId).OrderByDescending(x => x.EffectiveFrom).ToListAsync(ct);
        var source = await SourceAsync(db, a, ct); var result = new List<AgreementAdjustmentCandidate>();
        foreach (var line in source.Versions.GroupBy(x => x.LineId))
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var r = rules.FirstOrDefault(x => x.LineId == line.Key && x.EffectiveFrom <= date);
                if (r is null || r.Kind == AgreementAdjustmentKind.Fixed || !AgreementAdjustmentCalculator.IsScheduled(r, a.StartDate, date)) continue;
                string? blocked = null;
                try { await CalculateAdjustmentAsync(db, a, line.Key, date, null, ct); }
                catch (AgreementValidationException e) { blocked = e.Message; }
                result.Add(new(line.Key, line.OrderByDescending(x => x.Sequence).First().Name, date, r.Id, blocked));
            }
        return result;
    }
    public async Task<Guid> ProposeAdjustmentAsync(Guid accountId, Guid agreementId, Guid lineId, DateOnly date, decimal? chosenPercent, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, agreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, ct);
        var c = await CalculateAdjustmentAsync(db, a, lineId, date, chosenPercent, ct);
        var existing = await db.AgreementAdjustmentProposalRecords.FirstOrDefaultAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Fingerprint == c.Fingerprint && x.Status == AgreementProposalStatus.Pending, ct);
        if (existing != null) return existing.Id;
        var p = new AgreementAdjustmentProposal { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId, LineId = lineId, RuleId = c.Rule.Id,
            ScheduledDate = date, EffectiveDate = c.EffectiveDate, ChosenPercent = chosenPercent, OldPrice = c.CurrentPrice.UnitPrice, NewPrice = c.NewPrice,
            Fingerprint = c.Fingerprint, CalculationJson = Json(c), RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId };
        db.AgreementAdjustmentProposalRecords.Add(p); await SaveAsync(db, ct); await tx.CommitAsync(ct); return p.Id;
    }
    public async Task DecideAdjustmentAsync(Guid accountId, Guid agreementId, Guid proposalId, bool approve, string comment, CancellationToken ct = default)
    {
        ReasonRequired(comment);
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, agreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, true, ct);
        var p = await db.AgreementAdjustmentProposalRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Id == proposalId, ct) ?? throw new UnauthorizedAccessException();
        if (p.Status == AgreementProposalStatus.Approved && approve) return;
        if (p.Status != AgreementProposalStatus.Pending) throw new AgreementValidationException("ProcessingStale");
        AgreementAdjustmentCalculation? c = null;
        if (approve)
        {
            try { c = await CalculateAdjustmentAsync(db, a, p.LineId, p.ScheduledDate, p.ChosenPercent, ct); }
            catch (AgreementValidationException) { }
            if (c == null || c.Fingerprint != p.Fingerprint)
            {
                p.Status = AgreementProposalStatus.Stale; p.Comment = "ProcessingStale"; p.DecidedUtc = Clock.GetUtcNow(); p.DecidedByUserId = userContext.Current.UserId;
                await SaveAsync(db, ct); await tx.CommitAsync(ct); throw new AgreementValidationException("ProcessingStale");
            }
            await AddIndependentPriceAsync(db, a, p.LineId, c.EffectiveDate, c.NewPrice, c.CurrentPrice.Quantity, p.Id, comment, ct);
        }
        p.Status = approve ? AgreementProposalStatus.Approved : AgreementProposalStatus.Rejected;
        p.Comment = comment.Trim(); p.DecidedUtc = Clock.GetUtcNow(); p.DecidedByUserId = userContext.Current.UserId;
        Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
    private async Task AddIndependentPriceAsync(TenantPlatformDbContext db, Agreement a, Guid lineId, DateOnly date, decimal price, decimal quantity, Guid? adjustmentId, string reason, CancellationToken ct)
    {
        var ps = await db.AgreementPriceVersions.Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.LineId == lineId).MaxAsync(x => x.Sequence, ct);
        var vs = await db.AgreementLineVersions.Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.LineId == lineId).MaxAsync(x => x.Sequence, ct);
        db.AgreementPriceVersions.Add(new() { Id = Guid.NewGuid(), AccountId = a.AccountId, AgreementId = a.Id, LineId = lineId, Sequence = Math.Max(ps, vs) + 1,
            Independent = true, AdjustmentId = adjustmentId, EffectiveFrom = date, UnitPrice = price, Quantity = quantity, Reason = reason.Trim(), ActorUserId = userContext.Current.UserId, RecordedUtc = Clock.GetUtcNow() });
    }
    public async Task ChangePriceAsync(Guid accountId, Guid agreementId, Guid lineId, Guid revision, DateOnly date, decimal price, AgreementPriceTiming timing, string reason, CancellationToken ct = default)
    {
        ReasonRequired(reason);
        await using var db = await factory.CreateDbContextAsync(ct); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, agreementId, ct); var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, true, ct); CheckRevision(a, revision);
        if (price is < 0 or > 100000000 || decimal.Round(price, 4) != price || !Enum.IsDefined(timing) || date == default || date.Year > 9997) throw new AgreementValidationException("FinanceInvalidLine");
        var source = await SourceAsync(db, a, ct);
        var line = source.Versions.Where(x => x.LineId == lineId && x.EffectiveFrom <= date).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Sequence).FirstOrDefault() ?? throw new UnauthorizedAccessException();
        if (line.Status != AgreementLineStatus.Active || line.Frequency == AgreementFrequency.Once || date > line.EndDate) throw new AgreementValidationException("FinanceLineClosed");
        if (timing == AgreementPriceTiming.NextPeriod && !AgreementPeriodCalculator.IsBoundary(line, date)) date = AgreementPeriodCalculator.Reference(line, date).Until;
        if (date > line.EndDate || source.Versions.Any(x => x.LineId == lineId && x.EffectiveFrom > line.EffectiveFrom && x.EffectiveFrom <= date)) throw new AgreementValidationException("FinanceLineClosed");
        var current = source.Prices.Where(x => x.LineId == lineId && x.EffectiveFrom <= date).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Sequence).First();
        await AddIndependentPriceAsync(db, a, lineId, date, price, current.Quantity, null, reason, ct);
        Touch(a, userContext.Current.UserId); await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
}
