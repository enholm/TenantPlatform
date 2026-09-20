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
        // values, not formatting, so persisted basis snapshots keep stable fingerprints.
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
        if (await db.AgreementIndexRecords.AnyAsync(x => x.AccountId == accountId && x.Code == code.Trim().ToUpperInvariant(), ct)) throw new AgreementValidationException("SimpleDuplicateIndex");
        var index = new AgreementIndex { Id = Guid.NewGuid(), AccountId = accountId, Code = code.Trim().ToUpperInvariant(), Name = name.Trim(), Description = description.Trim(),
            Source = source.Trim(), Resolution = resolution, RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId };
        db.AgreementIndexRecords.Add(index); await SaveAsync(db, ct); return index.Id;
    }
    public async Task UpdateIndexAsync(Guid accountId, Guid indexId, string code, string name, string description, string source, AgreementIndexResolution resolution, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockFinancialAsync(db, accountId, null, ct);
        if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
        var index = await db.AgreementIndexRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == indexId, ct) ?? throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || string.IsNullOrWhiteSpace(name) || name.Length > 200 || description.Length > 4000 ||
            string.IsNullOrWhiteSpace(source) || source.Length > 2000 || !Enum.IsDefined(resolution)) throw new AgreementValidationException("ProcessingInvalidIndex");
        if (await db.AgreementIndexRecords.AnyAsync(x => x.AccountId == accountId && x.Id != indexId && x.Code == code.Trim().ToUpperInvariant(), ct)) throw new AgreementValidationException("SimpleDuplicateIndex");
        var periods = await db.AgreementIndexValueRecords.Where(x => x.AccountId == accountId && x.IndexId == indexId && !x.Superseded).Select(x => x.Period).ToListAsync(ct);
        if (periods.Any(x => AgreementAdjustmentCalculator.Period(x, resolution) != x)) throw new AgreementValidationException("ProcessingInvalidIndex");
        index.Code = code.Trim().ToUpperInvariant(); index.Name = name.Trim(); index.Description = description.Trim(); index.Source = source.Trim(); index.Resolution = resolution;
        index.RecordedUtc = Clock.GetUtcNow(); index.ActorUserId = userContext.Current.UserId;
        await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
    public async Task RecordIndexValueAsync(Guid accountId, Guid indexId, DateOnly period, decimal value, DateOnly? published, string reason, Guid? originalValueId = null, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockFinancialAsync(db, accountId, null, ct);
        if (!(await RequireMemberAsync(db, accountId, ct)).Admin) throw new UnauthorizedAccessException();
        var index = await db.AgreementIndexRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == indexId, ct) ?? throw new UnauthorizedAccessException();
        ReasonRequired(reason);
        if (value <= 0 || value > 100000000 || decimal.Round(value, 8) != value || period == default || period.Year > 9997 ||
            AgreementAdjustmentCalculator.Period(period, index.Resolution) != period) throw new AgreementValidationException("ProcessingInvalidIndex");
        var revision = await db.AgreementIndexValueRecords.Where(x => x.AccountId == accountId && x.IndexId == indexId && x.Period == period).Select(x => (int?)x.Revision).MaxAsync(ct) ?? 0;
        var periodKey = Guid.NewGuid();
        if (!originalValueId.HasValue && revision > 0) throw new AgreementValidationException("SimpleDuplicatePeriod");
        if (originalValueId.HasValue)
        {
            var original = await db.AgreementIndexValueRecords.SingleOrDefaultAsync(x => x.AccountId == accountId && x.IndexId == indexId && x.Id == originalValueId, ct)
                ?? throw new UnauthorizedAccessException();
            periodKey = original.PeriodKey;
            revision = Math.Max(revision, original.Revision);
            if (original.Superseded || await db.AgreementIndexValueRecords.AnyAsync(x => x.AccountId == accountId && x.IndexId == indexId && x.Period == original.Period && x.Revision > original.Revision, ct))
                throw new AgreementValidationException("ProcessingStale");
            if (original.Period != period)
            {
                if (await db.AgreementIndexValueRecords.AnyAsync(x => x.AccountId == accountId && x.IndexId == indexId && x.Period == period && x.PeriodKey != periodKey, ct)) throw new AgreementValidationException("SimpleDuplicatePeriod");
                // A moved period retains all original revisions for historical references.
                foreach (var old in await db.AgreementIndexValueRecords.Where(x => x.AccountId == accountId && x.IndexId == indexId && x.Period == original.Period).ToListAsync(ct)) old.Superseded = true;
            }
        }
        foreach (var proposal in await db.AgreementAdjustmentProposalRecords.Where(x => x.AccountId == accountId && x.Status == AgreementProposalStatus.Pending).ToListAsync(ct))
        {
            var calculation = ReadJson<AgreementAdjustmentCalculation>(proposal.CalculationJson);
            if (calculation.BaseIndex?.IndexId == indexId || calculation.ComparisonIndex?.IndexId == indexId) proposal.Status = AgreementProposalStatus.Stale;
        }
        db.AgreementIndexValueRecords.Add(new() { Id = Guid.NewGuid(), AccountId = accountId, IndexId = indexId, Period = period, Value = value, PeriodKey = periodKey,
            Revision = revision + 1, PublishedDate = published, Reason = reason.Trim(), RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId });
        await SaveAsync(db, ct); await tx.CommitAsync(ct);
    }
    public async Task<AgreementProcessingView> GetProcessingAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, edit, manage) = await RequireAsync(db, accountId, agreementId, false, false, ct);
        return new(edit && !a.IsArchived, manage && !a.IsArchived,
            await db.AgreementAdjustmentProposalRecords.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct));
    }
    private sealed record FinancialSource(Agreement Agreement, List<AgreementLineVersion> Versions, List<AgreementPriceVersion> Prices,
        Dictionary<Guid, List<AgreementAutomaticPrice>> AutomaticPrices);
    private static async Task<FinancialSource> SourceAsync(TenantPlatformDbContext db, Agreement a, CancellationToken ct)
    {
        var versions = await db.AgreementLineVersions.AsNoTracking().Include(x => x.Documents).Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.Status != AgreementLineStatus.Draft)
            .OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToListAsync(ct);
        foreach (var v in versions) v.Documents = v.Documents.OrderBy(x => x.DocumentId).ToList();
        var sequences = versions.Select(x => (x.LineId, x.Sequence)).ToHashSet();
        var prices = await db.AgreementPriceVersions.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToListAsync(ct);
        prices = prices.Where(x => x.Independent || sequences.Contains((x.LineId, x.Sequence))).ToList();
        var selections = await db.AgreementIndexSelections.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id).OrderBy(x => x.Sequence).ToListAsync(ct);
        var indexIds = selections.Where(x => x.IndexId.HasValue).Select(x => x.IndexId!.Value).Distinct().ToArray();
        var indices = await db.AgreementIndexRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && indexIds.Contains(x.Id)).ToListAsync(ct);
        var values = await db.AgreementIndexValueRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && indexIds.Contains(x.IndexId) && !x.Superseded).ToListAsync(ct);
        return new(a, versions, prices, AgreementAutomaticIndexCalculator.Project(a.IndexSetupNeedsReview, versions, prices, selections, indices, values));
    }
    private static object HeaderFingerprint(Agreement a) => new { a.Id, a.AccountId, a.Title, a.CounterpartyOrganizationId, a.Currency, a.Direction, a.StartDate, a.EndDate, a.Terms, a.Status, a.IsArchived };
}
