using TenantPlatform.Application.Security;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceRequests;
public sealed record InboundRequestTarget(Guid AccountId, Guid RequestId);
public interface IInboundServiceRequestData
{
    Task<InboundRequestTarget?> ResolveTokenAsync(string token, CancellationToken ct);
    Task<IInboundServiceRequestTransaction> BeginAsync(InboundRequestTarget target, string token, CancellationToken ct);
}
public interface IInboundServiceRequestTransaction : IAsyncDisposable
{
    Task<InboundRequestTarget?> ReadMessageOwnerAsync(string externalId, CancellationToken ct);
    Task<bool> InsertAsync(ServiceRequestMessage message, CancellationToken ct);
}
public sealed class InboundServiceRequestEmailJob(IInboundServiceRequestData data, IServiceRequestReplyAddressParser parser,
    ISystemWorkContext system) : IInboundServiceRequestEmailService
{
    public async Task<bool> ProcessAsync(InboundEmailMessage message, CancellationToken cancellationToken = default)
    {
        SystemWorkAccess.Require(system, SystemWorkKind.ServiceRequestInbound);
        var recipients = message.ToAddresses.ToArray(); var external = message.ExternalMessageId; var from = message.FromAddress;
        var subject = message.Subject; var body = message.Body; var reply = message.InReplyToMessageId; var references = message.References; var received = message.ReceivedAt;
        if (string.IsNullOrWhiteSpace(external) || external.Length > 500 || string.IsNullOrWhiteSpace(from) || from.Length > 320 || subject.Length > 500 || recipients.Any(x => x.Length > 320)) return false;
        var token = recipients.Select(x => parser.TryGetReplyToken(x, out var candidate) ? candidate : null).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (token is null) return false;
        var target = await data.ResolveTokenAsync(token, cancellationToken); if (target is null) return false;
        await using var tx = await data.BeginAsync(target, token, cancellationToken);
        var existing = await tx.ReadMessageOwnerAsync(external, cancellationToken);
        if (existing is not null) return existing == target;
        // Token possession binds a mailbox reply to a request; From is unverified content, never a user identity.
        return await tx.InsertAsync(new() { Id = Guid.NewGuid(), ServiceRequestId = target.RequestId, Direction = ServiceRequestMessageDirection.Inbound,
            Type = ServiceRequestMessageType.Email, ExternalMessageId = external, InReplyToMessageId = reply, References = references,
            FromAddress = from, ToAddress = recipients.FirstOrDefault(), Subject = subject, Body = body, CreatedAt = received.ToUniversalTime() }, cancellationToken);
    }
}
