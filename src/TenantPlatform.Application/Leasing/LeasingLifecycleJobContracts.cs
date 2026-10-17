using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Application.Leasing;
public sealed record LeasingLifecycleWork(bool AccountActive, LeasingNotificationSettings? Settings, string? AgreementTimeZone,
    List<LeasingAcquisition> Acquisitions, List<LeasingFramework> Frameworks, List<LeasingLifecycle> Lifecycles,
    List<LeasingEquipment> Equipment, List<LeasingOrder> Orders, List<LeasingFollowup> Followups, Dictionary<Guid, LeasingRecipientFacts> Recipients);
public interface ILeasingLifecycleJobData
{
    Task<List<Guid>> ReadActiveAccountsAsync(CancellationToken ct);
    Task<ILeasingLifecycleTransaction> BeginAsync(Guid account, CancellationToken ct);
    void ReportFailure(Guid account, string errorType);
}
public interface ILeasingLifecycleTransaction : IAsyncDisposable
{
    LeasingLifecycleWork State { get; }
    Task SaveFollowupsAsync(CancellationToken ct);
    Task<List<LeasingNotification>> ReadNotificationsAsync(CancellationToken ct);
    Task CommitAsync(List<LeasingNotification> notifications, CancellationToken ct);
}
