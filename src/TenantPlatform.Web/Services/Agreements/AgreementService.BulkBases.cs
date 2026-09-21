using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public async Task<List<AgreementBulkBasisOption>> GetBulkBasisOptionsAsync(Guid accountId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await RequireMemberAsync(db, accountId, ct);
        return await (from a in Accessible(db, accountId, user, admin).AsNoTracking()
                      join p in db.Organizations on a.CounterpartyOrganizationId equals p.Id
                      where a.Direction == AgreementDirection.Income && p.AccountId == accountId
                      orderby a.Title, a.Id
                      select new AgreementBulkBasisOption(a.Id, a.Title, p.Id, p.Name)).ToListAsync(ct);
    }

    private static void CheckBulkRange(DateOnly from, DateOnly to)
    {
        if (from > to || from.Year < 3 || to.Year > 9996 || to.DayNumber - from.DayNumber > 366)
            throw new AgreementValidationException("FinanceInvalidRange");
    }

    public async Task<List<AgreementBulkBasisPreview>> PreviewBulkBasisAsync(Guid accountId, AgreementBulkBasisFilter filter, CancellationToken ct = default)
    {
        CheckBulkRange(filter.From, filter.To);
        var options = await GetBulkBasisOptionsAsync(accountId, ct);
        var result = new List<AgreementBulkBasisPreview>();
        foreach (var option in options.Where(x => (!filter.AgreementId.HasValue || x.Id == filter.AgreementId) &&
                     (!filter.CounterpartyId.HasValue || x.CounterpartyId == filter.CounterpartyId)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
                var (a, edit, _) = await RequireAsync(db, accountId, option.Id, false, false, ct);
                CheckBasisDirection(a, AgreementDirection.Income);
                var source = await SourceAsync(db, a, ct);
                result.Add(await BulkPreviewAsync(db, source, filter.From, filter.To, edit, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException)
            {
                // Access may have been revoked after the options query. Do not return cached identifying data.
                result.Add(new(option.Id, "", "", "", AgreementBulkBasisStatus.Followup, "BulkAccessDenied", false, "", [], [], []));
            }
            catch (Exception ex)
            {
                result.Add(new(option.Id, option.Title, option.Counterparty, "", AgreementBulkBasisStatus.Followup,
                    BulkError(ex, option.Id), false, "", [], [], []));
            }
        }
        return result;
    }

    private string BulkError(Exception ex, Guid agreementId)
    {
        if (ex is UnauthorizedAccessException) return "BulkAccessDenied";
        if (ex is AgreementValidationException) return ex.Message;
        logger.LogError(ex, "Bulk basis processing failed for agreement {AgreementId}.", agreementId);
        return "AgreementOperationFailed";
    }

    private async Task<List<AgreementBulkBasisLink>> BulkLinksAsync(TenantPlatformDbContext db, Guid accountId, Guid agreementId,
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        var selected = ids.ToArray();
        var rows = await (from b in db.AgreementBasisRecords.AsNoTracking()
                          join s in db.AgreementBasisSnapshotRecords.AsNoTracking()
                            on new { b.AccountId, b.AgreementId, BasisId = b.Id, b.Revision }
                            equals new { s.AccountId, s.AgreementId, s.BasisId, s.Revision }
                          where b.AccountId == accountId && b.AgreementId == agreementId && selected.Contains(b.Id)
                          orderby b.InvoiceDate, b.Id
                          select new { b.Id, b.InvoiceDate, s.DataJson }).ToListAsync(ct);
        return rows.Select(x => new AgreementBulkBasisLink(x.Id, x.InvoiceDate,
            ReadJson<AgreementBasisData>(x.DataJson).Events.Sum(e => e.Amount))).ToList();
    }

    private async Task<AgreementBulkBasisPreview> BulkPreviewAsync(TenantPlatformDbContext db, FinancialSource source,
        DateOnly from, DateOnly to, bool edit, CancellationToken ct)
    {
        var a = source.Agreement;
        var periods = InvoicePeriods(source, from, to);
        var data = await BasisDataAsync(db, source, periods, ct);
        // New claims can be safely reported as existing on a concurrent/repeated run. Cancellations
        // can increase the amount to create, so they invalidate the preview along with financial inputs.
        var cancelled = await db.AgreementBasisRecords.AsNoTracking()
            .Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.Status == AgreementBasisStatus.Cancelled)
            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        var fingerprint = Fingerprint(new { from, to, cancelled, Financial = BasisFingerprint(source, data) });
        var keys = periods.Select(x => x.EventKey).Distinct().ToArray();
        var claims = await db.AgreementBasisEventRecords.AsNoTracking()
            .Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id && x.Active && keys.Contains(x.EventKey)).ToListAsync(ct);
        var claimed = claims.Select(x => x.EventKey).ToHashSet();
        var claimedIds = claims.Select(x => x.BasisId).Distinct().ToArray();
        // Include old dates as well: changed schedules or removed lines must not hide stale drafts.
        var bases = await db.AgreementBasisRecords.AsNoTracking().Where(x => x.AccountId == a.AccountId && x.AgreementId == a.Id &&
            x.Status != AgreementBasisStatus.Cancelled && (claimedIds.Contains(x.Id) || (x.InvoiceDate >= from && x.InvoiceDate <= to)))
            .OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id).ToListAsync(ct);
        string? reason = a.IsArchived ? "FollowupArchived" : !edit ? "BulkReadOnly" : a.IndexSetupNeedsReview ? "SimpleIndexReview" : null;
        foreach (var basis in bases)
        {
            var current = await CurrentBasisDataAsync(db, source, basis, ct);
            if (basis.Status == AgreementBasisStatus.Draft &&
                BasisFingerprint(source, current) != (await LatestBasisSnapshotAsync(db, basis, ct)).Fingerprint)
                reason ??= "BulkStaleDraft";
            if (basis.Status == AgreementBasisStatus.Approved && !basis.OriginalBasisId.HasValue)
            {
                var correction = new AgreementBasis { AccountId = a.AccountId, AgreementId = a.Id, OriginalBasisId = basis.Id };
                if ((await CurrentBasisDataAsync(db, source, correction, ct)).Events.Any(x => x.Amount != 0))
                    reason ??= "ProcessingCorrectionNeeded";
            }
        }
        var fresh = periods.Where(x => !claimed.Contains(x.EventKey)).ToList();
        var status = reason is not null ? AgreementBulkBasisStatus.Followup : fresh.Count > 0 ? AgreementBulkBasisStatus.Ready
            : bases.Count > 0 ? AgreementBulkBasisStatus.Existing : AgreementBulkBasisStatus.Empty;
        return new(a.Id, a.Title, data.CounterpartyName, a.Currency!, status, reason, status == AgreementBulkBasisStatus.Ready,
            fingerprint, periods, fresh, await BulkLinksAsync(db, a.AccountId, a.Id, bases.Select(x => x.Id), ct));
    }

    public async Task<List<AgreementBulkBasisResult>> GenerateBulkBasisAsync(Guid accountId, DateOnly from, DateOnly to,
        IReadOnlyList<AgreementBulkBasisSelection> selection, CancellationToken ct = default)
    {
        CheckBulkRange(from, to);
        await using (var check = await factory.CreateDbContextAsync(ct)) await RequireMemberAsync(check, accountId, ct);
        var results = new List<AgreementBulkBasisResult>();
        foreach (var item in selection.DistinctBy(x => x.AgreementId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var db = await factory.CreateDbContextAsync(ct);
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await LockFinancialAsync(db, accountId, item.AgreementId, ct);
                var (a, _, _) = await RequireAsync(db, accountId, item.AgreementId, true, false, ct);
                CheckBasisDirection(a, AgreementDirection.Income);
                var source = await SourceAsync(db, a, ct);
                var current = await BulkPreviewAsync(db, source, from, to, true, ct);
                if (string.IsNullOrEmpty(item.Fingerprint) || item.Fingerprint != current.Fingerprint)
                    results.Add(new(a.Id, AgreementBulkBasisStatus.Followup, "BulkChanged", a.Currency!, [], current.Existing));
                else if (current.Status == AgreementBulkBasisStatus.Followup)
                    results.Add(new(a.Id, current.Status, current.Reason, a.Currency!, [], current.Existing));
                else
                {
                    var run = await GenerateBasisCoreAsync(db, source, current.Periods, ct);
                    var created = await BulkLinksAsync(db, accountId, a.Id, run.Created, ct);
                    var existing = await BulkLinksAsync(db, accountId, a.Id, run.Existing.Concat(current.Existing.Select(x => x.Id)).Distinct(), ct);
                    await tx.CommitAsync(ct);
                    results.Add(new(a.Id, created.Count > 0 ? AgreementBulkBasisStatus.Created : existing.Count > 0 ? AgreementBulkBasisStatus.Existing : AgreementBulkBasisStatus.Empty,
                        null, a.Currency!, created, existing));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                results.Add(new(item.AgreementId, AgreementBulkBasisStatus.Followup, BulkError(ex, item.AgreementId), "", [], []));
            }
        }
        return results;
    }
}
