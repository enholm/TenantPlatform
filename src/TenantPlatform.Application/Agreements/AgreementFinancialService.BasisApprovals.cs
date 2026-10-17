using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public sealed partial class AgreementFinancialService
{
    public async Task<AgreementBasisApprovalList> ListPendingBasisApprovalsAsync(Guid accountId, AgreementBasisApprovalFilter filter, CancellationToken ct = default)
    {
        if (filter.From > filter.To || (filter.Direction.HasValue && !Enum.IsDefined(filter.Direction.Value)))
            throw new AgreementValidationException("FinanceInvalidRange");
        var rows = await data.ReadPendingApprovalsAsync(await authorization.GetAgreementListScopeAsync(accountId, ct), filter, ct);
        var options = rows.Options; var candidates = rows.Candidates;
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
        selection = selection.Select(x => x with { }).ToArray();
        var actor = await authorization.GetAccountActorScopeAsync(accountId, ct);
        var results = new List<AgreementBasisApprovalResult>();
        foreach (var item in selection.DistinctBy(x => x.BasisId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // The exact same locked transaction and validation path as individual approval.
                var current = await authorization.GetAccountActorScopeAsync(accountId, ct);
                if (current.UserId != actor.UserId) throw new UnauthorizedAccessException();
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
