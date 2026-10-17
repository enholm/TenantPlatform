using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record LeasingFrameworkWriteState(LeasingFramework? Framework, IReadOnlyList<LeasingAcquisition> Registered,
    IReadOnlyList<LeasingOrder> Orders, IReadOnlyList<Guid> ClosedAcquisitions, bool HasPendingLifecycleEvents,
    bool HasOpenFollowups, bool HasApprovedCreditNote, bool FinanceExists, bool OwnerIsActiveMember);
public interface ILeasingFrameworkWriteData
{
    Task<ILeasingFrameworkWriteTransaction> BeginAsync(Guid account, Guid framework, Guid finance, Guid owner, CancellationToken ct);
}
public interface ILeasingFrameworkWriteTransaction : IAsyncDisposable
{
    LeasingFrameworkWriteState State { get; }
    Task CommitAsync(LeasingFramework framework, LeasingHistory history, CancellationToken ct);
}
public sealed class LeasingFrameworkWriter(IAuthorizationService authorization, ILeasingFrameworkWriteData data, TimeProvider clock)
{
    public async Task<Guid> SaveFrameworkAsync(Guid account, Guid? id, LeasingFramework input, string reason, CancellationToken ct = default)
    {
        input = JsonSerializer.Deserialize<LeasingFramework>(JsonSerializer.Serialize(input))!;
        var actor = await authorization.GetAccountActorScopeAsync(account, ct);
        var scope = LeasingUserAccess.Create(actor); var key = id ?? Guid.NewGuid();
        if (!id.HasValue && !scope.IsAdministrator) throw new UnauthorizedAccessException();
        await using var tx = await data.BeginAsync(account, key, input.FinanceOrganizationId, input.OwnerUserId, ct);
        var current = await authorization.GetAccountActorScopeAsync(account, ct);
        if (current.UserId != actor.UserId) throw new UnauthorizedAccessException();
        scope = LeasingUserAccess.Create(current); var state = tx.State;
        var entity = id.HasValue ? state.Framework ?? throw new UnauthorizedAccessException() : new LeasingFramework { Id = key, AccountId = account };
        if (!scope.IsAdministrator && (!id.HasValue || entity.OwnerUserId != actor.UserId || entity.OwnerUserId != input.OwnerUserId)) throw new UnauthorizedAccessException();
        if (id.HasValue && entity.Revision != input.Revision) throw new LeasingValidationException("LeasingConcurrency");
        if (id.HasValue && string.IsNullOrWhiteSpace(reason) || reason?.Length > 2000) throw new LeasingValidationException("LeasingReasonRequired");
        if (!state.FinanceExists) throw new LeasingValidationException("LeasingInvalidParty");
        if (!state.OwnerIsActiveMember) throw new LeasingValidationException("LeasingInvalidOwner");
        LeasingRules.ValidateCommon(input.Name, input.Number, input.Currency, input.Notes, input.Terms);
        if (!Enum.IsDefined(input.Status) || input.AcquisitionFrom == default || input.AcquisitionTo < input.AcquisitionFrom || input.Limit < 0 || input.Limit > 1000000000000m || decimal.Round(input.Limit, 2) != input.Limit) throw new LeasingValidationException("LeasingInvalidFramework");
        if (state.Registered.Any(x => x.Currency != input.Currency || x.FinanceOrganizationId != input.FinanceOrganizationId || !LeasingCalculator.InPeriod(x.PurchaseDate, input.AcquisitionFrom, input.AcquisitionTo)) || state.Registered.Count > 0 && entity.IncludesVat != input.IncludesVat) throw new LeasingValidationException("LeasingFrameworkConflict");
        if (id.HasValue && entity.Limit != input.Limit) throw new LeasingValidationException("LeasingLimitProposalRequired");
        if (state.Orders.Count > 0 && (entity.Currency != input.Currency || entity.IncludesVat != input.IncludesVat)) throw new LeasingValidationException("LeasingFrameworkConflict");
        var reserved = state.Orders.Where(x => x.Status == LeasingOrderStatus.Approved).Sum(x => LeasingOrderCalculator.Reserved(x, input.IncludesVat));
        if (LeasingCalculator.Used(state.Registered, input.IncludesVat) + reserved > input.Limit) throw new LeasingValidationException("LeasingLimitExceeded");
        if (input.Status == LeasingFrameworkStatus.Finished)
        {
            if (!current.Assignments.Any(x => x.AccountId == account && x.BuildingId is null && x.OrganizationId is null && LegacyRolePermissions.Grants(x, Permissions.Leasing.ApproveLifecycle))) throw new UnauthorizedAccessException();
            if (reserved > 0 || state.Registered.Any(x => !state.ClosedAcquisitions.Contains(x.Id)) || state.HasPendingLifecycleEvents || state.HasOpenFollowups) throw new LeasingValidationException("LifeFrameworkUnresolved");
        }
        if (entity.CreditNotesReleaseLimit != input.CreditNotesReleaseLimit && state.HasApprovedCreditNote) throw new LeasingValidationException("InvoiceCreditRuleLocked");
        var before = id.HasValue ? JsonSerializer.Serialize(entity) : "{}";
        entity.Name = input.Name.Trim(); entity.Number = input.Number.Trim(); entity.FinanceOrganizationId = input.FinanceOrganizationId;
        entity.OwnerUserId = input.OwnerUserId; entity.AcquisitionFrom = input.AcquisitionFrom; entity.AcquisitionTo = input.AcquisitionTo;
        entity.CreditNotesReleaseLimit = input.CreditNotesReleaseLimit; entity.Limit = input.Limit; entity.Currency = input.Currency; entity.IncludesVat = input.IncludesVat;
        entity.Terms = input.Terms.Copy(); entity.Notes = input.Notes?.Trim(); entity.Status = input.Status; entity.Revision = Guid.NewGuid();
        await tx.CommitAsync(entity, new() { Id = Guid.NewGuid(), AccountId = account, FrameworkId = key, ActorUserId = actor.UserId,
            RecordedUtc = clock.GetUtcNow(), Action = id.HasValue ? "Updated" : "Created", Reason = reason?.Trim() ?? "", BeforeJson = before, AfterJson = JsonSerializer.Serialize(entity) }, ct);
        return key;
    }
}
