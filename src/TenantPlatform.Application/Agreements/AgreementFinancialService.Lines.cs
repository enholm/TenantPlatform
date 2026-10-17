using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed partial class AgreementFinancialService(IAuthorizationService authorization, IAgreementFinancialData data, TimeProvider clock)
{
    private async Task<AgreementReadScope> Require(Guid account, Guid agreement, bool edit, CancellationToken ct)
    {
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        if (edit && scope.IsArchived && scope.CanEditBeforeArchive) throw new AgreementValidationException("FollowupArchived");
        if (edit && !scope.CanEdit) throw new UnauthorizedAccessException(); return scope;
    }
    private static void CheckRevision(Agreement a, Guid revision) { if (a.Revision != revision) throw new AgreementValidationException("AgreementConcurrencyConflict"); }
    private static void ReasonRequired(string? reason) { if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new AgreementValidationException("ProcessingReasonRequired"); }
    private void Touch(Agreement a, Guid user) { a.Revision = Guid.NewGuid(); a.UpdatedUtc = clock.GetUtcNow(); a.UpdatedByUserId = user; }
    public async Task<AgreementLinesDto> GetLinesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var scope = await Require(accountId, agreementId, false, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, false, ct);
        var current = await Require(accountId, agreementId, false, ct); if (scope.ListScope.UserId != current.ListScope.UserId) throw new UnauthorizedAccessException();
        var state = tx.State; var a = state.Agreement;
        return new(a.Revision, current.CanEdit && !a.IsArchived, a.Direction, a.Currency,
            state.Lines.Select(x => new AgreementLineDetails(x, state.Versions.Where(v => v.LineId == x.Id).OrderByDescending(v => v.Sequence).ToList(), state.Prices.Where(v => v.LineId == x.Id).OrderByDescending(v => v.Sequence).ToList())).ToList(), state.Documents, state.ActorNames);
    }
    private static List<AgreementCalculatedPeriod> Calculate(AgreementFinancialState state, DateOnly from, DateOnly to)
    {
        var a = state.Agreement;
        var versions = state.Versions.Where(x => x.Status != AgreementLineStatus.Draft).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToList();
        foreach (var v in versions) v.Documents = v.Documents.OrderBy(x => x.DocumentId).ToList();
        var sequences = versions.Select(x => (x.LineId, x.Sequence)).ToHashSet();
        var prices = state.Prices.Where(x => x.Independent || sequences.Contains((x.LineId, x.Sequence))).OrderBy(x => x.LineId).ThenBy(x => x.Sequence).ToList();
        var automatic = AgreementAutomaticIndexCalculator.Project(a.IndexSetupNeedsReview, versions, prices, state.Selections.OrderBy(x => x.Sequence).ToList(), state.Indices, state.IndexValues.Where(x => !x.Superseded).ToList());
        return AgreementPeriodCalculator.Calculate(a.Id, a.Direction ?? throw new AgreementValidationException("FinanceSetupRequired"), a.Currency ?? throw new AgreementValidationException("FinanceSetupRequired"), versions, prices, from, to, automatic);
    }
    public async Task<AgreementForecastDto> ForecastAsync(Guid accountId, Guid agreementId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var scope = await Require(accountId, agreementId, false, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, false, ct);
        var current = await Require(accountId, agreementId, false, ct); if (scope.ListScope.UserId != current.ListScope.UserId) throw new UnauthorizedAccessException();
        var periods = Calculate(tx.State, from, to);
        return new(periods, periods.Where(x => x.Direction == AgreementDirection.Income).Sum(x => x.Amount), periods.Where(x => x.Direction == AgreementDirection.Cost).Sum(x => x.Amount), tx.State.Agreement.Currency!);
    }
    public async Task<Guid> SaveLineAsync(Guid accountId, Guid agreementId, Guid? lineId, SaveAgreementLineRequest r, CancellationToken ct = default)
    {
        r = JsonSerializer.Deserialize<SaveAgreementLineRequest>(JsonSerializer.Serialize(r))!;
        var initial = await Require(accountId, agreementId, true, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, true, ct);
        var scope = await Require(accountId, agreementId, true, ct); if (scope.ListScope.UserId != initial.ListScope.UserId) throw new UnauthorizedAccessException();
        var user = scope.ListScope.UserId; var state = tx.State; var a = state.Agreement;
        CheckRevision(a, r.Revision);
        if (a.Direction is null || a.Currency is null) throw new AgreementValidationException("FinanceSetupRequired");
        ValidateLine(r);
        var line = lineId.HasValue
            ? state.Lines.SingleOrDefault(x => x.Id == lineId)
                ?? throw new UnauthorizedAccessException()
            : new AgreementLine { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId };
        var allVersions = state.Versions.OrderByDescending(x => x.Sequence).ToList();
        var old = allVersions.FirstOrDefault(x => x.LineId == line.Id);
        if (r.IndexRegulated && !a.IndexId.HasValue && !(a.IndexSetupNeedsReview && old?.IndexRegulated == true)) throw new AgreementValidationException("SimpleIndexRequired");
        if (old?.Status is AgreementLineStatus.Ended or AgreementLineStatus.Deactivated) throw new AgreementValidationException("FinanceLineClosed");
        var active = line.ActivatedUtc.HasValue;
        var lastPriceSequence = state.Prices.Where(x => x.LineId == line.Id).Select(x => (int?)x.Sequence).DefaultIfEmpty().Max() ?? 0;
        var v = new AgreementLineVersion { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
            LineId = line.Id, Sequence = Math.Max(old?.Sequence ?? 0, lastPriceSequence) + 1, EffectiveFrom = active ? r.EffectiveFrom : r.StartDate,
            Name = r.Name.Trim(), Description = r.Description?.Trim(), StartDate = r.StartDate, EndDate = r.EndDate,
            FirstPayableDate = r.FirstPayableDate, PayableSourceLineId = r.PayableSourceLineId,
            PayableOffsetMonths = r.PayableSourceLineId.HasValue ? r.PayableOffsetMonths : null,
            Frequency = r.Frequency, Anchor = r.Anchor, AnchorDate = r.Anchor == AgreementAnchor.Calendar ? new(1, 1, 1) : r.AnchorDate,
            BillingTiming = r.BillingTiming, Status = active || r.Activate ? AgreementLineStatus.Active : AgreementLineStatus.Draft,
            IndexRegulated = r.IndexRegulated, RecordedUtc = clock.GetUtcNow(), ActorUserId = user, Reason = r.Reason?.Trim() };
        if (r.PayableSourceLineId.HasValue)
        {
            var seen = new HashSet<Guid> { line.Id };
            var sourceId = r.PayableSourceLineId;
            while (sourceId.HasValue)
            {
                if (!seen.Add(sourceId.Value)) throw new AgreementValidationException("FinanceSourceCycle");
                var source = allVersions.FirstOrDefault(x => x.LineId == sourceId) ?? throw new AgreementValidationException("FinanceInvalidReference");
                sourceId = source.PayableSourceLineId;
            }
            var sourceVersion = allVersions.First(x => x.LineId == r.PayableSourceLineId);
            if (!active || r.PayableSourceLineId != old!.PayableSourceLineId || r.PayableOffsetMonths != old.PayableOffsetMonths)
            {
                v.PayableSourceStartDate = sourceVersion.StartDate;
                try { v.FirstPayableDate = sourceVersion.StartDate.AddMonths(r.PayableOffsetMonths!.Value); }
                catch (ArgumentOutOfRangeException) { throw new AgreementValidationException("FinanceInvalidDates"); }
            }
        }
        if (active && (v.EffectiveFrom == default || v.EffectiveFrom > v.EndDate))
            throw new AgreementValidationException("FinanceInvalidDates");
        if (active && r.PayableSourceLineId == old!.PayableSourceLineId && r.PayableOffsetMonths == old.PayableOffsetMonths)
            v.PayableSourceStartDate = old.PayableSourceStartDate;
        if (v.FirstPayableDate < v.StartDate || v.FirstPayableDate.Year > 9998) throw new AgreementValidationException("FinanceInvalidDates");
        var documents = r.DocumentIds.Distinct().ToArray();
        if (documents.Length > 100 || state.Documents.Count(x => documents.Contains(x.Id)) != documents.Length)
            throw new AgreementValidationException("FinanceInvalidReference");
        v.Documents = documents.Select(id => new AgreementLineDocument { AccountId = accountId, AgreementId = agreementId, LineVersionId = v.Id, DocumentId = id }).ToList();
        if (!lineId.HasValue) state.Lines.Add(line);
        if (r.Activate && !active) line.ActivatedUtc = clock.GetUtcNow();
        var currentPrice = state.Prices.Where(x => x.LineId == line.Id).OrderByDescending(x => x.Sequence).FirstOrDefault();
        var economicChange = old is null || currentPrice is null || r.Quantity != currentPrice.Quantity || r.UnitPrice != currentPrice.UnitPrice ||
            v.StartDate != old.StartDate || v.EndDate != old.EndDate || v.FirstPayableDate != old.FirstPayableDate ||
            v.Frequency != old.Frequency || v.Anchor != old.Anchor || v.AnchorDate != old.AnchorDate || v.BillingTiming != old.BillingTiming;
        if (active && economicChange) ReasonRequired(r.Reason);
        if (active && !economicChange && v.IndexRegulated == old!.IndexRegulated) v.EffectiveFrom = old.EffectiveFrom;
        state.Versions.Add(v);
        if (!active || currentPrice is null || r.Quantity != currentPrice.Quantity || r.UnitPrice != currentPrice.UnitPrice || v.EffectiveFrom < currentPrice.EffectiveFrom) state.Prices.Add(new AgreementPriceVersion { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
            LineId = line.Id, Sequence = v.Sequence, EffectiveFrom = v.EffectiveFrom, Quantity = r.Quantity, UnitPrice = r.UnitPrice,
            IndexBaseDate = active && currentPrice is not null && r.UnitPrice == currentPrice.UnitPrice && v.EffectiveFrom >= (currentPrice.IndexBaseDate ?? currentPrice.EffectiveFrom) ? currentPrice.IndexBaseDate ?? currentPrice.EffectiveFrom : v.EffectiveFrom,
            RecordedUtc = v.RecordedUtc, ActorUserId = v.ActorUserId, Reason = v.Reason });
        Touch(a, user); await tx.CommitAsync(ct);
        return line.Id;
    }

    public async Task CloseLineAsync(Guid accountId, Guid agreementId, Guid lineId, Guid revision, DateOnly date, bool deactivate, string reason, CancellationToken ct = default)
    {
        var initial = await Require(accountId, agreementId, true, ct);
        await using var tx = await data.BeginAsync(accountId, agreementId, true, ct);
        var scope = await Require(accountId, agreementId, true, ct); if (scope.ListScope.UserId != initial.ListScope.UserId) throw new UnauthorizedAccessException();
        var user = scope.ListScope.UserId; var state = tx.State; var a = state.Agreement; CheckRevision(a, revision);
        var original = state.Versions.Where(x => x.LineId == lineId).OrderByDescending(x => x.Sequence).FirstOrDefault() ?? throw new UnauthorizedAccessException();
        var old = JsonSerializer.Deserialize<AgreementLineVersion>(JsonSerializer.Serialize(original))!;
        if (old.Status != AgreementLineStatus.Active) throw new AgreementValidationException("FinanceLineClosed");
        if (date == default || date.Year > 9998 || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000)
            throw new AgreementValidationException("FinanceInvalidDates");
        var boundary = deactivate ? date : date.AddDays(1);
        if (boundary < old.EffectiveFrom || boundary < old.StartDate || (old.EndDate.HasValue && date > old.EndDate.Value))
            throw new AgreementValidationException("FinanceInvalidDates");
        // Detached snapshot becomes a new row. The original and its document links are never updated.
        old.Id = Guid.NewGuid(); old.Sequence++; old.EffectiveFrom = boundary;
        old.Status = deactivate ? AgreementLineStatus.Deactivated : AgreementLineStatus.Ended;
        if (!deactivate) old.EndDate = date;
        old.RecordedUtc = clock.GetUtcNow(); old.ActorUserId = user; old.Reason = reason.Trim();
        old.Documents = old.Documents.Select(d => new AgreementLineDocument { AccountId = accountId, AgreementId = agreementId,
            LineVersionId = old.Id, DocumentId = d.DocumentId }).ToList();
        state.Versions.Add(old); Touch(a, user); await tx.CommitAsync(ct);
    }

    private static void ValidateLine(SaveAgreementLineRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 200 || r.Description?.Length > 4000 || r.Reason?.Length > 2000 ||
            !Enum.IsDefined(r.Frequency) || !Enum.IsDefined(r.Anchor) || !Enum.IsDefined(r.BillingTiming) ||
            r.Quantity <= 0 || r.Quantity > 100000000 || r.UnitPrice < 0 || r.UnitPrice > 100000000 ||
            decimal.Round(r.Quantity, 4) != r.Quantity || decimal.Round(r.UnitPrice, 4) != r.UnitPrice)
            throw new AgreementValidationException("FinanceInvalidLine");
        if (r.EffectiveFrom.Year > 9998 || r.StartDate == default || r.StartDate.Year > 9998 || r.EndDate < r.StartDate || r.EndDate?.Year > 9998 ||
            (r.Anchor == AgreementAnchor.Date && (r.AnchorDate == default || r.AnchorDate > r.StartDate)) ||
            (r.PayableSourceLineId.HasValue && (r.PayableOffsetMonths is null or < 0 or > 1200)))
            throw new AgreementValidationException("FinanceInvalidDates");
    }
}
