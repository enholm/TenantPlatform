using TenantPlatform.Application.Security;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceRequests;
public sealed record ServiceRequestOutboxWork(EmailOutboxMessage? Message, bool AccountActive, ServiceRequest? Request,
    List<ServiceDefinitionProvider> Providers, List<ServiceRequestMessage> Inbound);
public interface IServiceRequestOutboxData
{
    Task<IServiceRequestOutboxTransaction> BeginNextAsync(DateTimeOffset now, CancellationToken ct);
    void ReportFailure(Guid id, int attempt, string errorType);
}
public interface IServiceRequestOutboxTransaction : IAsyncDisposable
{
    ServiceRequestOutboxWork State { get; }
    Task CommitAsync(ServiceRequestMessage? history, CancellationToken ct);
}
public sealed class EmailOutboxJob(IServiceRequestOutboxData data, IEmailSender sender, IServiceRequestEmailAddressService addresses,
    TimeProvider clock, ISystemWorkContext system)
{
    public async Task<bool> ProcessOneAsync(CancellationToken ct = default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.EmailOutbox);
        await using var tx = await data.BeginNextAsync(clock.GetUtcNow(), ct);
        var state = tx.State; var message = state.Message; if (message is null) return false;
        var request = state.Request;
        var validRecipient = request is not null && System.Net.Mail.MailAddress.TryCreate(message.ToAddress, out _) &&
            (state.Providers.Any(x => x.IsActive && x.IntegrationType == ServiceProviderIntegrationType.Email && x.ServiceProviderOrganizationId == request.AssignedServiceProviderOrganizationId && string.Equals(x.RequestEmailAddress?.Trim(), message.ToAddress.Trim(), StringComparison.OrdinalIgnoreCase)) ||
            state.Inbound.Any(x => string.Equals(x.FromAddress?.Trim(), message.ToAddress.Trim(), StringComparison.OrdinalIgnoreCase) && x.ExternalMessageId == message.InReplyToMessageId));
        if (!state.AccountActive || request is null || !validRecipient || message.AttemptCount >= 5)
        {
            message.Status = EmailOutboxStatus.Failed; message.NextAttemptAt = null; message.LastError = !state.AccountActive || request is null ? "EmailRequestUnavailable" : "EmailRecipientUnavailable";
            await tx.CommitAsync(null, ct); return true;
        }
        message.AttemptCount++; message.LastAttemptAt = clock.GetUtcNow(); ServiceRequestMessage? history = null;
        try
        {
            // Use the current request token, never a stale or cross-request persisted Reply-To.
            await sender.SendAsync(message.ToAddress, addresses.GetReplyAddress(request.ReplyToken), message.Subject, message.Body, message.InReplyToMessageId, message.References, ct);
            message.Status = EmailOutboxStatus.Sent; message.SentAt = clock.GetUtcNow(); message.LastError = null; message.NextAttemptAt = null;
            history = new() { Id = Guid.NewGuid(), ServiceRequestId = request.Id, Direction = ServiceRequestMessageDirection.Outbound, Type = ServiceRequestMessageType.Email,
                ToAddress = message.ToAddress, Subject = message.Subject, Body = message.Body, InReplyToMessageId = message.InReplyToMessageId, References = message.References, CreatedAt = clock.GetUtcNow() };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            message.LastError = "EmailDeliveryFailed";
            if (message.AttemptCount >= 5) { message.Status = EmailOutboxStatus.Failed; message.NextAttemptAt = null; }
            else message.NextAttemptAt = clock.GetUtcNow().AddMinutes(Math.Pow(2, message.AttemptCount));
            data.ReportFailure(message.Id, message.AttemptCount, ex.GetType().Name);
        }
        await tx.CommitAsync(history, ct); return true;
    }
}
