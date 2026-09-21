using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public async Task<AgreementBasisApprovalList> ListPendingBasisApprovalsAsync(Guid accountId, AgreementBasisApprovalFilter filter, CancellationToken ct = default)
    {
        if (filter.From > filter.To || (filter.Direction.HasValue && !Enum.IsDefined(filter.Direction.Value)))
            throw new AgreementValidationException("FinanceInvalidRange");
        await using var db = await factory.CreateDbContextAsync(ct);
        var (user, admin) = await RequireMemberAsync(db, accountId, ct);
        var accessible = Accessible(db, accountId, user, admin).AsNoTracking();
        var query = from b in db.AgreementBasisRecords.AsNoTracking()
                    join a in accessible on b.AgreementId equals a.Id
                    join p in db.Organizations.AsNoTracking() on a.CounterpartyOrganizationId equals p.Id
                    where b.AccountId == accountId && p.AccountId == accountId && b.Status == AgreementBasisStatus.Draft
                    select new { Basis = b, Agreement = a, Party = p.Name };
        var options = await query.Select(x => new { x.Agreement.Id, x.Agreement.Title,
            x.Agreement.CounterpartyOrganizationId, x.Party }).Distinct().OrderBy(x => x.Title).ThenBy(x => x.Id)
            .Select(x => new AgreementBulkBasisOption(x.Id, x.Title, x.CounterpartyOrganizationId, x.Party)).ToListAsync(ct);
        if (filter.CounterpartyId.HasValue) query = query.Where(x => x.Agreement.CounterpartyOrganizationId == filter.CounterpartyId);
        if (filter.AgreementId.HasValue) query = query.Where(x => x.Agreement.Id == filter.AgreementId);
        if (filter.Direction.HasValue) query = query.Where(x => x.Basis.Direction == filter.Direction);
        if (filter.From.HasValue) query = query.Where(x => x.Basis.InvoiceDate >= filter.From);
        if (filter.To.HasValue) query = query.Where(x => x.Basis.InvoiceDate <= filter.To);
        // No Take limit: the explicit selection can span the complete filtered result.
        var candidates = await query.OrderBy(x => x.Basis.InvoiceDate).ThenBy(x => x.Basis.Id)
            .Select(x => new { x.Basis.Id, x.Basis.AgreementId, x.Basis.Direction, x.Basis.InvoiceDate,
                x.Agreement.Title, x.Party, x.Agreement.IsArchived, x.Agreement.CounterpartyOrganizationId }).ToListAsync(ct);
        var result = new List<AgreementBasisApprovalItem>();
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // Reuses the individual detail read and its consistent snapshot/staleness checks.
                var details = await GetBasisAsync(accountId, candidate.Id, ct);
                var basis = details.Basis;
                if (basis.Status != AgreementBasisStatus.Draft) continue;
                var data = ReadJson<AgreementBasisData>(details.Snapshots[0].DataJson);
                var periods = data.Events.SelectMany(x => x.Segments).ToList();
                if (periods.Count == 0)
                {
                    periods = details.Snapshots.Skip(1).SelectMany(x => ReadJson<AgreementBasisData>(x.DataJson).Events)
                        .SelectMany(x => x.Segments).ToList();
                    if (periods.Count == 0 && basis.OriginalBasisId is Guid originalId)
                    {
                        var original = await GetBasisAsync(accountId, originalId, ct);
                        periods = ReadJson<AgreementBasisData>(original.Snapshots[0].DataJson).Events.SelectMany(x => x.Segments).ToList();
                    }
                }
                var reason = candidate.IsArchived ? "FollowupArchived" : !details.CanApprove ? "ApprovalPermissionRequired"
                    : details.Stale ? "ProcessingStale" : null;
                result.Add(new(basis.Id, basis.Revision, basis.AgreementId, data.AgreementTitle, data.CounterpartyId,
                    data.CounterpartyName, basis.Direction, basis.InvoiceDate,
                    periods.Count == 0 ? null : periods.Min(x => x.From), periods.Count == 0 ? null : periods.Max(x => x.To),
                    data.Events.Sum(x => x.Amount), data.Currency, basis.OriginalBasisId.HasValue, reason is null, reason));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { /* Access was revoked after the query; omit identifying data. */ }
            catch (Exception ex)
            {
                // Malformed or invalid drafts remain visible for follow-up instead of blocking the list.
                result.Add(new(candidate.Id, 0, candidate.AgreementId, candidate.Title, candidate.CounterpartyOrganizationId, candidate.Party, candidate.Direction, candidate.InvoiceDate,
                    null, null, 0, "", false, false, BulkError(ex, candidate.Id)));
            }
        }
        return new(result, options);
    }

    public async Task<List<AgreementBasisApprovalResult>> ApproveBasesAsync(Guid accountId,
        IReadOnlyList<AgreementBasisApprovalSelection> selection, CancellationToken ct = default)
    {
        await using (var db = await factory.CreateDbContextAsync(ct)) await RequireMemberAsync(db, accountId, ct);
        var results = new List<AgreementBasisApprovalResult>();
        foreach (var item in selection.DistinctBy(x => x.BasisId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // The exact same locked transaction and validation path as individual approval.
                var result = await ApproveBasisCoreAsync(accountId, item.BasisId, item.Revision, ct);
                results.Add(new(item.BasisId, result.ApprovedNow ? AgreementBasisApprovalOutcome.Approved
                    : AgreementBasisApprovalOutcome.AlreadyProcessed, null));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                results.Add(new(item.BasisId, AgreementBasisApprovalOutcome.Failed,
                    ex is UnauthorizedAccessException ? "ApprovalAccessDenied" : BulkError(ex, item.BasisId)));
            }
        }
        return results;
    }
}
