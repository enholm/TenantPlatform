using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record LeasingOrderTarget(LeasingOrder Order, LeasingFramework Framework);
public interface ILeasingOrderReadData
{
    Task<LeasingOrderTarget?> ReadTargetAsync(Guid account, Guid id, CancellationToken ct);
    Task<List<LeasingOrder>> QueryAsync(LeasingUserScope scope, Guid? framework, Guid? acquisition, CancellationToken ct);
    Task<OrderDetails> ReadDetailsAsync(Guid account, Guid id, CancellationToken ct);
    Task<List<LeasingAcquisition>> ReadCandidatesAsync(LeasingUserScope scope, LeasingOrder order, CancellationToken ct);
    Task<List<LeasingLimitChange>> ReadLimitChangesAsync(Guid account, Guid framework, CancellationToken ct);
}
public sealed class LeasingOrderReader(IAuthorizationService authorization, ILeasingOrderReadData data, ILeasingReadData parents, TimeProvider clock)
{
    public static bool Owns(LeasingUserScope scope, LeasingOrderTarget target) => scope.IsAdministrator || target.Order.OwnerUserId == scope.UserId || target.Framework.OwnerUserId == scope.UserId;
    private async Task<LeasingOrderTarget> Target(LeasingUserScope scope, Guid id, bool edit, CancellationToken ct)
    {
        var target = await data.ReadTargetAsync(scope.AccountId, id, ct) ?? throw new UnauthorizedAccessException();
        if (!Owns(scope, target) && (edit || !scope.CanReadOrders)) throw new UnauthorizedAccessException(); return target;
    }
    public async Task<bool> CanChangeLimitAsync(Guid account, CancellationToken ct = default) => LeasingUserAccess.Has(await authorization.GetAccountActorScopeAsync(account, ct), Permissions.Leasing.ApproveLimit);
    public async Task<List<LeasingOrder>> ListOrdersAsync(Guid account, Guid? framework = null, CancellationToken ct = default)
    {
        var scope = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
        return await data.QueryAsync(scope, framework, null, ct);
    }
    public async Task<OrderDetails> GetOrderAsync(Guid account, Guid id, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow(); var actor = await authorization.GetAccountActorScopeAsync(account, ct); var scope = LeasingUserAccess.Create(actor);
        var target = await Target(scope, id, false, ct); var value = await data.ReadDetailsAsync(account, id, ct);
        target = new(value.Order, value.Framework);
        if (!Owns(scope, target) && !scope.CanReadOrders) throw new UnauthorizedAccessException();
        // Internal storage/claim data is never part of order presentation metadata.
        foreach (var doc in value.Documents) doc.StorageKey = "";
        foreach (var invoice in value.Invoices) { invoice.StorageKey = ""; invoice.FileHash = ""; invoice.ProcessingToken = null; invoice.ProcessingStartedUtc = null; invoice.ProcessingError = null; }
        var today = AgreementReminderSchedule.Today(now, await parents.ReadTimeZoneAsync(account, ct));
        return value with { CanApprove = LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveOrders), CanEdit = Owns(scope, target), NeedsReview = value.Order.Status == LeasingOrderStatus.Approved && LeasingOrderCalculator.Reserved(value.Order, value.Framework.IncludesVat) > 0 && (value.Framework.Status != LeasingFrameworkStatus.Open || today > value.Framework.AcquisitionTo) };
    }
    public async Task<LeasingAcquisition> NewOrderAcquisitionAsync(Guid account, Guid orderId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow(); var scope = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
        var target = await Target(scope, orderId, true, ct); var o = target.Order; var f = target.Framework;
        if (o.Status != LeasingOrderStatus.Approved) throw new LeasingValidationException("LeasingInvalidTransition");
        return new() { FrameworkId = f.Id, FinanceOrganizationId = f.FinanceOrganizationId, SupplierOrganizationId = o.SupplierOrganizationId, OwnerUserId = scope.UserId, Currency = f.Currency, Terms = f.Terms.Copy(), PurchaseDate = AgreementReminderSchedule.Today(now, await parents.ReadTimeZoneAsync(account, ct)), Name = o.Number, Reference = o.Number };
    }
    public async Task<List<LeasingOrder>> AcquisitionOrdersAsync(Guid account, Guid acquisition, CancellationToken ct = default)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct); var read = LeasingReader.Scope(actor);
        if (!LeasingReader.CanRead(read, await parents.ReadParentAsync(account, acquisition, false, actor.UserId, ct), false)) throw new UnauthorizedAccessException();
        return await data.QueryAsync(LeasingUserAccess.Create(actor), null, acquisition, ct);
    }
    public async Task<List<LeasingAcquisition>> OrderLinkCandidatesAsync(Guid account, Guid order, CancellationToken ct = default)
    {
        var scope = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
        var target = await Target(scope, order, false, ct);
        return await data.ReadCandidatesAsync(scope, target.Order, ct);
    }
    public async Task<List<LeasingLimitChange>> LimitChangesAsync(Guid account, Guid framework, CancellationToken ct = default)
    {
        var actor = await authorization.GetAccountActorScopeAsync(account, ct); var scope = LeasingUserAccess.Create(actor);
        var facts = await parents.ReadParentAsync(account, framework, true, actor.UserId, ct);
        if (facts.Framework is null || !scope.IsAdministrator && facts.Framework.OwnerUserId != actor.UserId && !LeasingUserAccess.Has(actor, Permissions.Leasing.ApproveLimit)) throw new UnauthorizedAccessException();
        return await data.ReadLimitChangesAsync(account, framework, ct);
    }
}
