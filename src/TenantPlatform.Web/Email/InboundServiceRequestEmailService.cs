using TenantPlatform.Application.ServiceRequests;
namespace TenantPlatform.Web.Email;
public sealed class InboundServiceRequestEmailService(InboundServiceRequestEmailJob job) : IInboundServiceRequestEmailService
{
    public Task<bool> ProcessAsync(InboundEmailMessage message, CancellationToken cancellationToken = default) => job.ProcessAsync(message, cancellationToken);
}
