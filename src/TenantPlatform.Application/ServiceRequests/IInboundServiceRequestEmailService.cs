namespace TenantPlatform.Application.ServiceRequests;

public interface IInboundServiceRequestEmailService
{
    Task<bool> ProcessAsync(
        InboundEmailMessage message,
        CancellationToken cancellationToken = default);
}

