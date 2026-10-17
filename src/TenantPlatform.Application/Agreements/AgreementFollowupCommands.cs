using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public class SaveAgreementReminderSettings
{
    public bool Enabled { get; set; }
    public int[] Days { get; set; } = [90, 30, 7];
    public string TimeZoneId { get; set; } = "Europe/Oslo";
    public TimeOnly SendAt { get; set; } = new(8, 0);
    public Guid Revision { get; set; }
}

public interface IAgreementFollowupService
{
    Task<AgreementDeadlinePage> ListDeadlinesAsync(Guid accountId, AgreementDeadlineFilter filter, CancellationToken ct = default);
    Task<AgreementFollowupDetails> GetFollowupAsync(Guid accountId, Guid agreementId, CancellationToken ct = default);
    Task FollowupAsync(Guid accountId, Guid deadlineId, Guid revision, AgreementFollowupAction action, Guid? assignedUserId, string? comment, CancellationToken ct = default);
    Task SetReminderPreferencesAsync(Guid accountId, Guid agreementId, Guid revision, AgreementReminderMode mode, int[] days, CancellationToken ct = default);
    Task<SaveAgreementReminderSettings> GetReminderSettingsAsync(Guid accountId, CancellationToken ct = default);
    Task SaveReminderSettingsAsync(Guid accountId, SaveAgreementReminderSettings request, CancellationToken ct = default);
    Task ArchiveAsync(Guid accountId, Guid agreementId, Guid revision, CancellationToken ct = default);
}

