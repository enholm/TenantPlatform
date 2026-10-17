using System.Text.Json;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;

public interface ILeasingClassificationData
{
    Task<bool> HasOwnedRootAsync(Guid account, Guid user, CancellationToken ct);
    Task<LeasingClassificationCatalog> ReadCatalogAsync(Guid account, bool canManage, CancellationToken ct);
    Task<ILeasingDimensionRuleTransaction> BeginRuleAsync(Guid account, Guid dimension, CancellationToken ct);
}
public interface ILeasingDimensionRuleTransaction : IAsyncDisposable
{
    Dimension? Dimension { get; }
    LeasingDimensionRule? Rule { get; }
    Task CommitAsync(LeasingDimensionRule rule, DimensionHistory history, CancellationToken ct);
}
public sealed class LeasingClassificationService(IAuthorizationService authorization, ILeasingClassificationData data, TimeProvider clock)
{
    public async Task<LeasingClassificationCatalog> ClassificationCatalogAsync(Guid account, CancellationToken ct = default)
    {
        var actor = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
        if (!actor.CanReadLifecycle && !actor.CanReadOrders && !await data.HasOwnedRootAsync(account, actor.UserId, ct)) throw new UnauthorizedAccessException();
        return await data.ReadCatalogAsync(account, actor.IsAdministrator, ct);
    }
    public async Task SaveDimensionRuleAsync(Guid account, LeasingDimensionRule input, CancellationToken ct = default)
    {
        // Snapshot client-owned mutable data before the first await.
        var dimension = input.DimensionId; var revision = input.Revision;
        var enabled = input.IsEnabled; var required = input.IsRequired; var allocation = input.AllowAllocation;
        var actor = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
        if (!actor.IsAdministrator) throw new UnauthorizedAccessException();
        await using var tx = await data.BeginRuleAsync(account, dimension, ct);
        var current = LeasingUserAccess.Create(await authorization.GetAccountActorScopeAsync(account, ct));
        if (current.UserId != actor.UserId || !current.IsAdministrator || tx.Dimension is not { } dim) throw new UnauthorizedAccessException();
        if (allocation && dim.AllowMultiple || !enabled && (required || allocation)) throw new LeasingValidationException("LeasingInvalidDimensionRule");
        var rule = tx.Rule;
        var before = rule is null ? "{}" : JsonSerializer.Serialize(rule);
        if (rule is null) rule = new() { AccountId = account, DimensionId = dimension };
        else if (rule.Revision != revision) throw new LeasingValidationException("LeasingConcurrency");
        rule.IsEnabled = enabled; rule.IsRequired = required; rule.AllowAllocation = allocation; rule.Revision = Guid.NewGuid();
        await tx.CommitAsync(rule, new() { Id = Guid.NewGuid(), AccountId = account, DimensionId = dimension,
            ActorUserId = actor.UserId, RecordedUtc = clock.GetUtcNow(), BeforeJson = before, AfterJson = JsonSerializer.Serialize(rule) }, ct);
    }
    public static List<string> MissingDimensions(LeasingItem item, LeasingClassificationCatalog catalog) => catalog.Dimensions
        .Where(d => d.Rule.IsEnabled && d.Rule.IsRequired && (item.AllocationDimensions.Any(x => x.DimensionId == d.Dimension.Id)
            ? item.AllocationRows.Count == 0 || item.AllocationRows.Any(r => !item.DimensionSelections.Any(s => s.DimensionId == d.Dimension.Id && s.AllocationRowId == r.Id))
            : !item.DimensionSelections.Any(s => s.DimensionId == d.Dimension.Id && s.AllocationRowId == null)))
        .Select(d => d.Dimension.Name).ToList();
}
