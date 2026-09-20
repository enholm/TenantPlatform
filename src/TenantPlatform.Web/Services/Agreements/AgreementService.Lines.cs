using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public async Task<AgreementLinesDto> GetLinesAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var readTransaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var (a, edit, _) = await RequireAsync(db, accountId, agreementId, false, false, ct);
        var lines = await db.AgreementLines.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId).ToListAsync(ct);
        var versions = await db.AgreementLineVersions.AsNoTracking().Include(x => x.Documents)
            .Where(x => x.AccountId == accountId && x.AgreementId == agreementId).OrderByDescending(x => x.Sequence).ToListAsync(ct);
        var prices = await db.AgreementPriceVersions.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId)
            .OrderByDescending(x => x.Sequence).ToListAsync(ct);
        var documents = await (from d in db.AgreementDocuments.AsNoTracking() join u in db.Users on d.UploadedByUserId equals u.Id
            where d.AccountId == accountId && d.AgreementId == agreementId orderby d.UploadedUtc descending
            select new AgreementDocumentDto(d.Id, d.OriginalFileName, d.Size, d.Category, d.Description, d.UploadedUtc, u.FirstName + " " + u.LastName)).ToListAsync(ct);
        var actors = versions.Select(x => x.ActorUserId).Concat(prices.Select(x => x.ActorUserId)).Distinct().ToArray();
        var names = await db.Users.Where(x => actors.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FirstName + " " + x.LastName, ct);
        return new(a.Revision, edit && !a.IsArchived, a.Direction, a.Currency,
            lines.Select(x => new AgreementLineDetails(x, versions.Where(v => v.LineId == x.Id).ToList(), prices.Where(v => v.LineId == x.Id).ToList())).ToList(),
            documents, names);
    }

    public async Task<Guid> SaveLineAsync(Guid accountId, Guid agreementId, Guid? lineId, SaveAgreementLineRequest r, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockFinancialAsync(db, accountId, agreementId, ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, ct);
        CheckRevision(a, r.Revision);
        if (a.Direction is null || a.Currency is null) throw new AgreementValidationException("FinanceSetupRequired");
        ValidateLine(r);
        var line = lineId.HasValue
            ? await db.AgreementLines.SingleOrDefaultAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && x.Id == lineId, ct)
                ?? throw new UnauthorizedAccessException()
            : new AgreementLine { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId };
        var allVersions = await db.AgreementLineVersions.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId)
            .OrderByDescending(x => x.Sequence).ToListAsync(ct);
        var old = allVersions.FirstOrDefault(x => x.LineId == line.Id);
        if (r.IndexRegulated && !a.IndexId.HasValue && !(a.IndexSetupNeedsReview && old?.IndexRegulated == true)) throw new AgreementValidationException("SimpleIndexRequired");
        if (old?.Status is AgreementLineStatus.Ended or AgreementLineStatus.Deactivated) throw new AgreementValidationException("FinanceLineClosed");
        var active = line.ActivatedUtc.HasValue;
        var lastPriceSequence = await db.AgreementPriceVersions.Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.LineId == line.Id)
            .Select(x => (int?)x.Sequence).MaxAsync(ct) ?? 0;
        var v = new AgreementLineVersion { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
            LineId = line.Id, Sequence = Math.Max(old?.Sequence ?? 0, lastPriceSequence) + 1, EffectiveFrom = active ? r.EffectiveFrom : r.StartDate,
            Name = r.Name.Trim(), Description = r.Description?.Trim(), StartDate = r.StartDate, EndDate = r.EndDate,
            FirstPayableDate = r.FirstPayableDate, PayableSourceLineId = r.PayableSourceLineId,
            PayableOffsetMonths = r.PayableSourceLineId.HasValue ? r.PayableOffsetMonths : null,
            Frequency = r.Frequency, Anchor = r.Anchor, AnchorDate = r.Anchor == AgreementAnchor.Calendar ? new(1, 1, 1) : r.AnchorDate,
            BillingTiming = r.BillingTiming, Status = active || r.Activate ? AgreementLineStatus.Active : AgreementLineStatus.Draft,
            IndexRegulated = r.IndexRegulated, RecordedUtc = Clock.GetUtcNow(), ActorUserId = userContext.Current.UserId, Reason = r.Reason?.Trim() };
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
        if (documents.Length > 100 || await db.AgreementDocuments.CountAsync(x => x.AccountId == accountId && x.AgreementId == agreementId && documents.Contains(x.Id), ct) != documents.Length)
            throw new AgreementValidationException("FinanceInvalidReference");
        v.Documents = documents.Select(id => new AgreementLineDocument { AccountId = accountId, AgreementId = agreementId, LineVersionId = v.Id, DocumentId = id }).ToList();
        if (!lineId.HasValue) db.AgreementLines.Add(line);
        if (r.Activate && !active) line.ActivatedUtc = Clock.GetUtcNow();
        var currentPrice = await db.AgreementPriceVersions.AsNoTracking().Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.LineId == line.Id)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct);
        var economicChange = old is null || currentPrice is null || r.Quantity != currentPrice.Quantity || r.UnitPrice != currentPrice.UnitPrice ||
            v.StartDate != old.StartDate || v.EndDate != old.EndDate || v.FirstPayableDate != old.FirstPayableDate ||
            v.Frequency != old.Frequency || v.Anchor != old.Anchor || v.AnchorDate != old.AnchorDate || v.BillingTiming != old.BillingTiming;
        if (active && economicChange) ReasonRequired(r.Reason);
        if (active && !economicChange && v.IndexRegulated == old!.IndexRegulated) v.EffectiveFrom = old.EffectiveFrom;
        db.AgreementLineVersions.Add(v);
        if (!active || currentPrice is null || r.Quantity != currentPrice.Quantity || r.UnitPrice != currentPrice.UnitPrice || v.EffectiveFrom < currentPrice.EffectiveFrom) db.AgreementPriceVersions.Add(new AgreementPriceVersion { Id = Guid.NewGuid(), AccountId = accountId, AgreementId = agreementId,
            LineId = line.Id, Sequence = v.Sequence, EffectiveFrom = v.EffectiveFrom, Quantity = r.Quantity, UnitPrice = r.UnitPrice,
            IndexBaseDate = active && currentPrice is not null && r.UnitPrice == currentPrice.UnitPrice && v.EffectiveFrom >= (currentPrice.IndexBaseDate ?? currentPrice.EffectiveFrom) ? currentPrice.IndexBaseDate ?? currentPrice.EffectiveFrom : v.EffectiveFrom,
            RecordedUtc = v.RecordedUtc, ActorUserId = v.ActorUserId, Reason = v.Reason });
        Touch(a, userContext.Current.UserId); await SaveAsync(db, ct);
        await tx.CommitAsync(ct);
        return line.Id;
    }

    public async Task CloseLineAsync(Guid accountId, Guid agreementId, Guid lineId, Guid revision, DateOnly date, bool deactivate, string reason, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, true, false, ct); CheckRevision(a, revision);
        var old = await db.AgreementLineVersions.AsNoTracking().Include(x => x.Documents)
            .Where(x => x.AccountId == accountId && x.AgreementId == agreementId && x.LineId == lineId)
            .OrderByDescending(x => x.Sequence).FirstOrDefaultAsync(ct) ?? throw new UnauthorizedAccessException();
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
        old.RecordedUtc = Clock.GetUtcNow(); old.ActorUserId = userContext.Current.UserId; old.Reason = reason.Trim();
        old.Documents = old.Documents.Select(d => new AgreementLineDocument { AccountId = accountId, AgreementId = agreementId,
            LineVersionId = old.Id, DocumentId = d.DocumentId }).ToList();
        db.AgreementLineVersions.Add(old); Touch(a, userContext.Current.UserId); await SaveAsync(db, ct);
    }

    public async Task<AgreementForecastDto> ForecastAsync(Guid accountId, Guid agreementId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var (a, _, _) = await RequireAsync(db, accountId, agreementId, false, false, ct);
        if (a.Direction is null || a.Currency is null) throw new AgreementValidationException("FinanceSetupRequired");
        var periods = CalculateSource(await SourceAsync(db, a, ct), from, to);
        return new(periods, periods.Where(x => x.Direction == AgreementDirection.Income).Sum(x => x.Amount),
            periods.Where(x => x.Direction == AgreementDirection.Cost).Sum(x => x.Amount), a.Currency);
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
