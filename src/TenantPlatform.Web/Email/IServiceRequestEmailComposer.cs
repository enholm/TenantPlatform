namespace TenantPlatform.Web.Email;

public interface IServiceRequestEmailComposer
{
    Task<ComposedEmail> ComposeProviderRequestAsync(
        Guid accountId,
        Guid serviceRequestId,
        CancellationToken cancellationToken = default);
}

