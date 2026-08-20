namespace TenantPlatform.Web.Services.ServiceRequests;

public interface IServiceRequestService
{
    Task<Guid> CreateServiceRequestAsync(
        Guid accountId,
        Guid requesterUserId,
        CreateServiceRequestRequest request,
        CancellationToken cancellationToken = default);
}

