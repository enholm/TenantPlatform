using TenantPlatform.Application.ServiceRequests;
namespace TenantPlatform.Web.Email;
public sealed class ServiceRequestEmailComposer(TenantPlatform.Application.ServiceRequests.ServiceRequestService service) : IServiceRequestEmailComposer
{
    public Task<ComposedEmail> ComposeProviderRequestAsync(Guid accountId, Guid serviceRequestId, CancellationToken cancellationToken = default) =>
        service.ComposeProviderRequestAsync(accountId, serviceRequestId, cancellationToken);
}
