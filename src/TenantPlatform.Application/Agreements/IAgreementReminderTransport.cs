using TenantPlatform.Core.Agreements;
namespace TenantPlatform.Application.Agreements;
public record AgreementReminderMessage(Guid QueueId, Guid AccountId, Guid AgreementId, string Recipient,
    string Language, string AgreementTitle, AgreementDeadlineKind Kind, DateOnly DueDate);
public interface IAgreementReminderTransport
{
    Task<string?> SendAsync(AgreementReminderMessage message, CancellationToken ct);
}

