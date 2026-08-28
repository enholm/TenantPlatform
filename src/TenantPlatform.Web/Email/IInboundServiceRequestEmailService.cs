namespace TenantPlatform.Web.Email;

public interface IInboundServiceRequestEmailService
{
    Task<bool> ProcessAsync(
        InboundEmailMessage message,
        CancellationToken cancellationToken = default);
}

