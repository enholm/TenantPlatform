using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record LeasingOrderWriteState(LeasingOrder? Order, LeasingFramework? Framework, LeasingLimitChange? Change, LeasingOrderEvent? PreviousEvent, int OrderCount, string TimeZone, LeasingClassificationCatalog Catalog, LeasingFramework? OriginalFramework = null);
public interface ILeasingOrderWriteData { Task<ILeasingOrderWork> BeginAsync(Guid account, Guid? order, Guid? framework, Guid? change, Guid? request, CancellationToken ct); }
public interface ILeasingOrderWork : IAsyncDisposable
{
    LeasingOrderWriteState State { get; }
    Task<(bool Supplier, bool Owner)> ReferencesAsync(Guid supplier, Guid owner, CancellationToken ct);
    Task SaveOrderAsync(LeasingOrder order, LeasingOrderEvent history, CancellationToken ct);
    Task SaveLimitAsync(LeasingLimitChange change, LeasingHistory? history, CancellationToken ct);
    Task<LeasingCapacity> ReadCapacityAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
}
