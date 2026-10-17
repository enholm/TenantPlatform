using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
namespace TenantPlatform.Application.Agreements;
public sealed record AgreementReminderWork(bool AccountActive, Agreement? Agreement, AgreementDeadline? Deadline, AgreementReminderSettings? Settings,
    List<AgreementReminder> Rows, User? Recipient, AgreementResponsibleFacts? RecipientFacts);
public interface IAgreementReminderData
{
    Task<List<Guid>> ReadActiveAccountsAsync(CancellationToken ct);
    Task<bool> AccountActiveAsync(Guid account, CancellationToken ct);
    Task EnsureDeadlinesAsync(Guid account, CancellationToken ct);
    Task<List<Guid>> ReadPlanningCandidatesAsync(Guid account, CancellationToken ct);
    Task<IAgreementReminderTransaction> BeginPlanningAsync(Guid account, Guid deadline, CancellationToken ct);
    Task ExpireReservationsAsync(Guid account, DateTimeOffset now, string result, CancellationToken ct);
    Task<List<Guid>> ReadReadyAsync(Guid account, DateTimeOffset now, int attempts, int limit, CancellationToken ct);
    Task<bool> ClaimAsync(Guid account, Guid id, Guid reservation, DateTimeOffset now, DateTimeOffset expires, int attempts, CancellationToken ct);
    Task<IAgreementReminderTransaction> BeginReservedAsync(Guid account, Guid id, Guid reservation, CancellationToken ct);
    void ReportFailure(Guid account, Guid? id, string errorType, bool transport);
}
public interface IAgreementReminderTransaction : IAsyncDisposable
{
    AgreementReminderWork State { get; }
    Task CommitAsync(CancellationToken ct);
    Task<bool> TryCommitPlanAsync(CancellationToken ct);
}
