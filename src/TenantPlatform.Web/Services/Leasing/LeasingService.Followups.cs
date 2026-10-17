using TenantPlatform.Application.Leasing;
using TenantPlatform.Core.Leasing;
namespace TenantPlatform.Web.Services.Leasing;
public sealed partial class LeasingService
{
    public Task<List<LeasingNotification>> LeasingNotificationHistoryAsync(Guid account, CancellationToken ct = default) => followups.LeasingNotificationHistoryAsync(account, ct);
    public Task<LeasingNotificationSettings> GetLeasingNotificationSettingsAsync(Guid account, CancellationToken ct = default) => followups.GetLeasingNotificationSettingsAsync(account, ct);
    public Task SaveLeasingNotificationSettingsAsync(Guid account, LeasingNotificationSettings input, string reason, CancellationToken ct = default) => followups.SaveLeasingNotificationSettingsAsync(account, input, reason, ct);
    public Task<LeasingFollowupPage> ListLeasingFollowupsAsync(Guid account, LeasingFollowupKind? kind = null, LeasingFollowupStatus? status = null, DateOnly? to = null, Guid? owner = null, int page = 1, CancellationToken ct = default) => followups.ListLeasingFollowupsAsync(account, kind, status, to, owner, page, ct);
    public Task UpdateLeasingFollowupAsync(Guid account, Guid id, Guid revision, LeasingFollowupStatus status, Guid? owner, string comment, Guid request, CancellationToken ct = default) => followups.UpdateLeasingFollowupAsync(account, id, revision, status, owner, comment, request, ct);
    public Task ReadLeasingNotificationAsync(Guid account, Guid id, CancellationToken ct = default) => followups.ReadLeasingNotificationAsync(account, id, ct);
}
