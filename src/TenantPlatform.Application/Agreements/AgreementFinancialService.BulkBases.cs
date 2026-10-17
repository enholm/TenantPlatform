using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed partial class AgreementFinancialService
{
    public async Task<List<AgreementBulkBasisOption>> GetBulkBasisOptionsAsync(Guid accountId, CancellationToken ct = default)
    {
        return await data.ReadBulkOptionsAsync(await authorization.GetAgreementListScopeAsync(accountId, ct), ct);
    }

    private static void CheckBulkRange(DateOnly from, DateOnly to)
    {
        if (from > to || from.Year < 3 || to.Year > 9996 || to.DayNumber - from.DayNumber > 366)
            throw new AgreementValidationException("FinanceInvalidRange");
    }

    public async Task<List<AgreementBulkBasisPreview>> PreviewBulkBasisAsync(Guid accountId, AgreementBulkBasisFilter filter, CancellationToken ct = default)
    {
        filter = filter with { };
        CheckBulkRange(filter.From, filter.To);
        var actor = await authorization.GetAccountActorScopeAsync(accountId, ct);
        var options = await GetBulkBasisOptionsAsync(accountId, ct);
        var result = new List<AgreementBulkBasisPreview>();
        foreach (var option in options.Where(x => (!filter.AgreementId.HasValue || x.Id == filter.AgreementId) &&
                     (!filter.CounterpartyId.HasValue || x.CounterpartyId == filter.CounterpartyId)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var session = await Open(accountId, option.Id, false, false, ct);
                if (session.Scope.ListScope.UserId != actor.UserId) throw new UnauthorizedAccessException();
                var state = session.State; var a = state.Agreement;
                CheckBasisDirection(a, AgreementDirection.Income);
                result.Add(BulkPreview(state, Source(state), filter.From, filter.To, session.Scope.CanEdit, ct));
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
        data.ReportFailure(ex, agreementId);
        return "AgreementOperationFailed";
    }

    private List<AgreementBulkBasisLink> BulkLinks(AgreementFinancialState state, IEnumerable<Guid> ids)
    {
        var selected = ids.ToHashSet();
        return state.Bases.Where(x => selected.Contains(x.Id)).OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id).Select(x => new AgreementBulkBasisLink(x.Id, x.InvoiceDate,
            ReadJson<AgreementBasisData>(LatestBasisSnapshot(state, x, default).DataJson).Events.Sum(e => e.Amount))).ToList();
    }

    private AgreementBulkBasisPreview BulkPreview(AgreementFinancialState state, FinancialSource source,
        DateOnly from, DateOnly to, bool edit, CancellationToken ct)
    {
        var a = source.Agreement;
        var periods = InvoicePeriods(source, from, to);
        var data = BasisData(state, source, periods, ct);
        // New claims can be safely reported as existing on a concurrent/repeated run. Cancellations
        // can increase the amount to create, so they invalidate the preview along with financial inputs.
        var cancelled = state.Bases.Where(x => x.Status == AgreementBasisStatus.Cancelled).OrderBy(x => x.Id).Select(x => x.Id).ToList();
        var fingerprint = Fingerprint(new { from, to, cancelled, Financial = BasisFingerprint(source, data) });
        var keys = periods.Select(x => x.EventKey).Distinct().ToArray();
        var claims = state.Events.Where(x => x.Active && keys.Contains(x.EventKey)).ToList();
        var claimed = claims.Select(x => x.EventKey).ToHashSet();
        var claimedIds = claims.Select(x => x.BasisId).Distinct().ToArray();
        // Include old dates as well: changed schedules or removed lines must not hide stale drafts.
        var bases = state.Bases.Where(x => x.Status != AgreementBasisStatus.Cancelled && (claimedIds.Contains(x.Id) || (x.InvoiceDate >= from && x.InvoiceDate <= to))).OrderBy(x => x.InvoiceDate).ThenBy(x => x.Id).ToList();
        string? reason = a.IsArchived ? "FollowupArchived" : !edit ? "BulkReadOnly" : a.IndexSetupNeedsReview ? "SimpleIndexReview" : null;
        foreach (var basis in bases)
        {
            var current = CurrentBasisData(state, source, basis, ct);
            if (basis.Status == AgreementBasisStatus.Draft &&
                BasisFingerprint(source, current) != (LatestBasisSnapshot(state, basis, ct)).Fingerprint)
                reason ??= "BulkStaleDraft";
            if (basis.Status == AgreementBasisStatus.Approved && !basis.OriginalBasisId.HasValue)
            {
                var correction = new AgreementBasis { AccountId = a.AccountId, AgreementId = a.Id, OriginalBasisId = basis.Id };
                if ((CurrentBasisData(state, source, correction, ct)).Events.Any(x => x.Amount != 0))
                    reason ??= "ProcessingCorrectionNeeded";
            }
        }
        var fresh = periods.Where(x => !claimed.Contains(x.EventKey)).ToList();
        var status = reason is not null ? AgreementBulkBasisStatus.Followup : fresh.Count > 0 ? AgreementBulkBasisStatus.Ready
            : bases.Count > 0 ? AgreementBulkBasisStatus.Existing : AgreementBulkBasisStatus.Empty;
        return new(a.Id, a.Title, data.CounterpartyName, a.Currency!, status, reason, status == AgreementBulkBasisStatus.Ready,
            fingerprint, periods, fresh, BulkLinks(state, bases.Select(x => x.Id)));
    }

    public async Task<List<AgreementBulkBasisResult>> GenerateBulkBasisAsync(Guid accountId, DateOnly from, DateOnly to,
        IReadOnlyList<AgreementBulkBasisSelection> selection, CancellationToken ct = default)
    {
        selection = selection.Select(x => x with { }).ToArray();
        CheckBulkRange(from, to);
        var actor = await authorization.GetAccountActorScopeAsync(accountId, ct);
        var results = new List<AgreementBulkBasisResult>();
        foreach (var item in selection.DistinctBy(x => x.AgreementId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var session = await Open(accountId, item.AgreementId, true, false, ct);
                if (session.Scope.ListScope.UserId != actor.UserId) throw new UnauthorizedAccessException();
                var state = session.State; var a = state.Agreement; var user = session.Scope.ListScope.UserId;
                CheckBasisDirection(a, AgreementDirection.Income); var source = Source(state);
                var current = BulkPreview(state, source, from, to, true, ct);
                if (string.IsNullOrEmpty(item.Fingerprint) || item.Fingerprint != current.Fingerprint)
                    results.Add(new(a.Id, AgreementBulkBasisStatus.Followup, "BulkChanged", a.Currency!, [], current.Existing));
                else if (current.Status == AgreementBulkBasisStatus.Followup)
                    results.Add(new(a.Id, current.Status, current.Reason, a.Currency!, [], current.Existing));
                else
                {
                    var run = GenerateBasisCore(state, source, current.Periods, user, ct);
                    var created = BulkLinks(state, run.Created);
                    var existing = BulkLinks(state, run.Existing.Concat(current.Existing.Select(x => x.Id)).Distinct());
                    if (run.Created.Count > 0) await session.CommitAsync(ct);
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
