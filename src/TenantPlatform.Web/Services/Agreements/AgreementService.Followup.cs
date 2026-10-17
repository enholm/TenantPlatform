using TenantPlatform.Application.Agreements;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Infrastructure.Agreements;

namespace TenantPlatform.Web.Services.Agreements;

public partial class AgreementService
{
    public async Task<AgreementDeadlinePage> ListDeadlinesAsync(Guid accountId, AgreementDeadlineFilter filter, CancellationToken ct = default)
    {
        // Explicit compatibility write; the Application reader never initializes data.
        await agreementInitialization.EnsureVisibleAccountAsync(accountId, ct);
        return await agreementDeadlines.ReadAsync(accountId, filter, ct);
    }

    // GUI compatibility can initialize only the already-authorized target. Reader itself never writes.
    public async Task<AgreementFollowupDetails> GetFollowupAsync(Guid accountId, Guid agreementId, CancellationToken ct = default)
    {
        var details = await agreementFollowup.ReadAsync(accountId, agreementId, ct);
        if (!details.DeadlinesInitialized)
        {
            await agreementInitialization.EnsureAgreementAsync(accountId, agreementId, ct);
            details = await agreementFollowup.ReadAsync(accountId, agreementId, ct);
        }
        return details;
    }

    public Task FollowupAsync(Guid accountId, Guid deadlineId, Guid revision, AgreementFollowupAction action, Guid? assignedUserId, string? comment, CancellationToken ct = default) =>
        followupWriter.FollowupAsync(accountId, deadlineId, revision, action, assignedUserId, comment, ct);
    public Task ArchiveAsync(Guid accountId, Guid agreementId, Guid revision, CancellationToken ct = default) => followupWriter.ArchiveAsync(accountId, agreementId, revision, ct);
    public Task SetReminderPreferencesAsync(Guid accountId, Guid agreementId, Guid revision, AgreementReminderMode mode, int[] days, CancellationToken ct = default) =>
        followupWriter.SetReminderPreferencesAsync(accountId, agreementId, revision, mode, days, ct);
    public Task<SaveAgreementReminderSettings> GetReminderSettingsAsync(Guid accountId, CancellationToken ct = default) => followupWriter.GetReminderSettingsAsync(accountId, ct);
    public Task SaveReminderSettingsAsync(Guid accountId, SaveAgreementReminderSettings request, CancellationToken ct = default) => followupWriter.SaveReminderSettingsAsync(accountId, request, ct);
}
