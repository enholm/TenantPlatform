using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public sealed record StoredDeadline(Guid Id, AgreementDeadlineKind Kind, DateOnly DueDate,
    AgreementFollowupStatus Status, AgreementDeadlineState State, DateTimeOffset UpdatedUtc);
public sealed record StoredDeadlinePage(bool Initialized, bool Archived, string TimeZoneId,
    int Page, int Total, StoredDeadline[] Items);
public sealed record FollowupDeadlineSummary(Guid Id, AgreementDeadlineKind Kind, DateOnly DueDate, int DaysLeft,
    AgreementFollowupStatus Status, AgreementDeadlineState State, DateTimeOffset UpdatedUtc);
public sealed record AgreementFollowupPage(bool Initialized, bool Archived, int RequestedPage, int Page,
    int Total, FollowupDeadlineSummary[] Items);

public interface IAgreementFollowupPageData
{
    Task<StoredDeadlinePage> ReadPageAsync(AgreementReadScope scope, int page, CancellationToken ct);
}

// Bounded projection of the existing follow-up read use case. Uses the same concrete agreement
// authorization and account calendar rules; deliberately does not read people, history or queue data.
public sealed class AgreementFollowupPageReader(IAuthorizationService authorization,
    IAgreementFollowupPageData data, TimeProvider clock)
{
    public const int PageSize = 25;
    public async Task<AgreementFollowupPage> ReadAsync(Guid account, Guid agreement, int page = 1, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        if (page is < 1 or > 10000 || account == Guid.Empty || agreement == Guid.Empty) throw new ArgumentException();
        var scope = await authorization.GetAgreementReadScopeAsync(account, agreement, ct);
        var stored = await data.ReadPageAsync(scope, page, ct);
        var today = AgreementReminderSchedule.Today(now, stored.TimeZoneId);
        return new(stored.Initialized, stored.Archived, page, stored.Page, stored.Total,
            stored.Items.Select(d => new FollowupDeadlineSummary(d.Id, d.Kind, d.DueDate,
                d.DueDate.DayNumber - today.DayNumber, d.Status, d.State, d.UpdatedUtc)).ToArray());
    }
}
