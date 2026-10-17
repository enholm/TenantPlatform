namespace TenantPlatform.Application.ServiceRequests;

public interface IServiceRequestEmailComposer
{
    Task<ComposedEmail> ComposeProviderRequestAsync(
        Guid accountId,
        Guid serviceRequestId,
        CancellationToken cancellationToken = default);
}

