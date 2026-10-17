using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;

public sealed record LeasingReadScope(Guid AccountId, Guid UserId, bool Administrator, bool FrameworkReader, bool AcquisitionReader, bool OrderApprover, bool OptionsReader, bool DocumentReader, bool FrameworkOptionsReader, bool LimitApprover, bool PaymentReader);
public sealed record LeasingListFilter(string? Search, string Kind, string? Status, Guid? Owner, int Page);
public sealed record LeasingParentFacts(LeasingFramework? Framework, LeasingAcquisition? Acquisition, LeasingFramework? AcquisitionFramework, bool HasOwnedOrder, bool HasOrderRealization);
public sealed record LeasingOptionData(List<LeasingOption> Parties, List<LeasingOption> Owners, List<LeasingOption> Frameworks, bool HasOwnedFramework);
public interface ILeasingReadData
{
    Task<bool> HasOwnedRootAsync(Guid account, Guid user, CancellationToken ct);
    Task<LeasingOptionData> ReadOptionsAsync(LeasingReadScope scope, CancellationToken ct);
    Task<LeasingPage> ReadListAsync(LeasingReadScope scope, LeasingListFilter filter, CancellationToken ct);
    Task<LeasingParentFacts> ReadParentAsync(Guid account, Guid id, bool framework, Guid actor, CancellationToken ct);
    Task<LeasingDetails> ReadDetailsAsync(LeasingReadScope scope, Guid id, bool framework, CancellationToken ct);
    Task<string> ReadTimeZoneAsync(Guid account, CancellationToken ct);
}
public sealed class LeasingReader(IAuthorizationService authorization, ILeasingReadData data, TimeProvider clock)
{
    public static LeasingReadScope Scope(AccountActorScope actor)
    {
        bool Has(string permission) => actor.Assignments.Any(x => x.AccountId == actor.AccountId && x.BuildingId is null && x.OrganizationId is null && LegacyRolePermissions.Grants(x, permission));
        var admin = Has(Permissions.Agreements.Create); var orders = Has(Permissions.Leasing.ApproveOrders); var limits = Has(Permissions.Leasing.ApproveLimit);
        var reports = Has(Permissions.Leasing.ReadReports) || Has(Permissions.Leasing.ExportReports);
        var payments = admin || reports || Has(Permissions.Leasing.ActivatePlans) || Has(Permissions.Leasing.ApproveInvoices) || Has(Permissions.Leasing.AcceptVariances);
        var lifecycle = LeasingUserAccess.Create(actor).CanReadLifecycle;
        return new(actor.AccountId, actor.UserId, admin, admin || reports || orders || limits, admin || reports, orders,
            admin || orders || limits || payments || lifecycle, payments || lifecycle, admin || orders || limits, limits, payments);
    }
    public static bool Owns(LeasingReadScope scope, LeasingParentFacts facts) => scope.Administrator || facts.Framework?.OwnerUserId == scope.UserId || facts.Acquisition?.OwnerUserId == scope.UserId || facts.AcquisitionFramework?.OwnerUserId == scope.UserId;
    public static bool CanRead(LeasingReadScope scope, LeasingParentFacts facts, bool framework) => framework
        ? facts.Framework is not null && (scope.FrameworkReader || facts.Framework.OwnerUserId == scope.UserId || facts.HasOwnedOrder)
        : facts.Acquisition is not null && (Owns(scope, facts) || scope.AcquisitionReader || scope.OrderApprover && facts.HasOrderRealization);
    private async Task<LeasingReadScope> Actor(Guid account, CancellationToken ct) => Scope(await authorization.GetAccountActorScopeAsync(account, ct));
    public async Task<LeasingOptions> OptionsAsync(Guid account, CancellationToken ct = default)
    {
        var scope = await Actor(account, ct);
        if (!scope.OptionsReader && !await data.HasOwnedRootAsync(account, scope.UserId, ct)) throw new UnauthorizedAccessException();
        var value = await data.ReadOptionsAsync(scope, ct);
        return new(value.Parties, value.Owners, value.Frameworks, scope.Administrator, scope.Administrator || value.HasOwnedFramework);
    }
    public async Task<LeasingPage> ListAsync(Guid account, string? search, string kind, string? status, Guid? owner, int page, CancellationToken ct = default)
    {
        var scope = await Actor(account, ct);
        // The legacy list includes only admin/owners, plus limit approvers for frameworks.
        // Broader report/order readers apply to details, not this list.
        return await data.ReadListAsync(scope with { FrameworkReader = scope.Administrator || scope.LimitApprover, AcquisitionReader = scope.Administrator }, new(search, kind, status, owner, page), ct);
    }
    public async Task<LeasingDetails> GetAsync(Guid account, Guid id, bool framework, CancellationToken ct = default)
    {
        var scope = await Actor(account, ct);
        var facts = await data.ReadParentAsync(account, id, framework, scope.UserId, ct);
        if (!CanRead(scope, facts, framework)) throw new UnauthorizedAccessException();
        var details = await data.ReadDetailsAsync(scope, id, framework, ct);
        return details with { CanEdit = Owns(scope, facts) && (framework || facts.Acquisition?.Status != LeasingAcquisitionStatus.Cancelled) };
    }
    public async Task<LeasingAcquisition> NewAcquisitionAsync(Guid account, Guid? frameworkId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow(); var scope = await Actor(account, ct);
        var result = new LeasingAcquisition { OwnerUserId = scope.UserId, PurchaseDate = AgreementReminderSchedule.Today(now, await data.ReadTimeZoneAsync(account, ct)), Items = [new LeasingItem()] };
        if (frameworkId.HasValue)
        {
            var facts = await data.ReadParentAsync(account, frameworkId.Value, true, scope.UserId, ct);
            var f = facts.Framework ?? throw new UnauthorizedAccessException();
            if (!Owns(scope, facts)) throw new UnauthorizedAccessException();
            if (f.Status != LeasingFrameworkStatus.Open) throw new LeasingValidationException("LeasingClosed");
            result.FrameworkId = f.Id; result.FinanceOrganizationId = f.FinanceOrganizationId; result.Currency = f.Currency; result.Terms = f.Terms.Copy();
        }
        else if (!scope.Administrator) throw new UnauthorizedAccessException();
        return result;
    }
}
