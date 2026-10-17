using TenantPlatform.Application.Security;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Application.Agreements;

public class AgreementDeadlineFilter
{
    public bool Mine { get; set; }
    public Guid? AssignedUserId { get; set; }
    public AgreementType? AgreementType { get; set; }
    public AgreementDeadlineKind? Kind { get; set; }
    public AgreementFollowupStatus? Status { get; set; }
    public AgreementDeadlineState? State { get; set; } = AgreementDeadlineState.Current;
    public bool IncludeCompleted { get; set; }
    public int HorizonDays { get; set; } = 90;
    public bool CustomDates { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
}
public record AgreementDeadlinePage(List<AgreementDeadlineRow> Items, int Total, int Page, int PageSize,
    List<AgreementOptionDto> Owners, int Overdue, int Next30, int MissingOwner);

public sealed record AgreementDeadlineQuery(bool Mine, Guid? AssignedUserId, AgreementType? AgreementType,
    AgreementDeadlineKind? Kind, AgreementFollowupStatus? Status, AgreementDeadlineState? State,
    bool IncludeCompleted, int HorizonDays, bool CustomDates, DateOnly? From, DateOnly? To, string? Search, int Page);

public interface IAgreementDeadlineData
{
    Task<string> ReadTimeZoneAsync(Guid accountId, CancellationToken ct);
    Task<AgreementDeadlinePage> QueryAsync(AgreementListScope scope, AgreementDeadlineQuery filter, DateOnly today, CancellationToken ct);
}
public interface IAgreementDeadlineReader
{
    Task<AgreementDeadlinePage> ReadAsync(Guid accountId, AgreementDeadlineFilter filter, CancellationToken ct = default);
}
public sealed class AgreementDeadlineReader(IAuthorizationService authorization, IAgreementDeadlineData data, TimeProvider clock) : IAgreementDeadlineReader
{
    public async Task<AgreementDeadlinePage> ReadAsync(Guid accountId, AgreementDeadlineFilter filter, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var query = new AgreementDeadlineQuery(filter.Mine, filter.AssignedUserId, filter.AgreementType, filter.Kind,
            filter.Status, filter.State, filter.IncludeCompleted, filter.HorizonDays, filter.CustomDates,
            filter.From, filter.To, filter.Search, filter.Page);
        var scope = await authorization.GetAgreementListScopeAsync(accountId, ct);
        if (query.CustomDates ? query.From > query.To : query.HorizonDays is not (30 or 60 or 90))
            throw new AgreementValidationException("FollowupInvalidRange");
        if (query.AgreementType.HasValue && !Enum.IsDefined(query.AgreementType.Value) ||
            query.Kind.HasValue && !Enum.IsDefined(query.Kind.Value) ||
            query.Status.HasValue && !Enum.IsDefined(query.Status.Value) ||
            query.State.HasValue && !Enum.IsDefined(query.State.Value))
            throw new AgreementValidationException("FollowupInvalidRange");
        var zone = await data.ReadTimeZoneAsync(accountId, ct);
        return await data.QueryAsync(scope, query, AgreementReminderSchedule.Today(now, zone), ct);
    }
}
