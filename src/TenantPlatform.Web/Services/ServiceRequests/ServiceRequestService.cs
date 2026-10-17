using TenantPlatform.Application.ServiceRequests;
namespace TenantPlatform.Web.Services.ServiceRequests;
public sealed class ServiceRequestService(TenantPlatform.Application.ServiceRequests.ServiceRequestService service) : IServiceRequestService
{
    public Task<Guid> CreateServiceRequestAsync(Guid accountId, CreateServiceRequestRequest request, CancellationToken cancellationToken = default) => service.CreateServiceRequestAsync(accountId, request, cancellationToken);
    public Task<List<ServiceRequestListItemDto>> GetMyRequestsAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default) => service.GetMyRequestsAsync(accountId, languageCode, cancellationToken);
    public Task<ServiceRequestDetailsDto?> GetRequestAsync(Guid accountId, Guid requestId, string languageCode, CancellationToken cancellationToken = default) => service.GetRequestAsync(accountId, requestId, languageCode, cancellationToken);
    public Task ApproveRequestAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => service.ApproveRequestAsync(accountId, requestId, cancellationToken);
    public Task CompleteRequestAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => service.CompleteRequestAsync(accountId, requestId, cancellationToken);
    public Task<List<ServiceRequestListItemDto>> GetAssignedRequestsAsync(Guid accountId, string languageCode, CancellationToken cancellationToken = default) => service.GetAssignedRequestsAsync(accountId, languageCode, cancellationToken);
    public Task<ServiceRequestAdminListDto> GetAdminRequestsAsync(Guid accountId, string languageCode, ServiceRequestAdminFilter filter, CancellationToken cancellationToken = default) => service.GetAdminRequestsAsync(accountId, languageCode, filter, cancellationToken);
    public Task QueueProviderEmailAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => service.QueueProviderEmailAsync(accountId, requestId, cancellationToken);
    public Task StartWorkAsync(Guid accountId, Guid requestId, CancellationToken cancellationToken = default) => service.StartWorkAsync(accountId, requestId, cancellationToken);
    public Task AddCommentAsync(Guid accountId, Guid requestId, string comment, CancellationToken cancellationToken = default) => service.AddCommentAsync(accountId, requestId, comment, cancellationToken);
    public Task ReplyToEmailAsync(Guid accountId, Guid requestId, Guid messageId, string reply, CancellationToken cancellationToken = default) => service.ReplyToEmailAsync(accountId, requestId, messageId, reply, cancellationToken);
    public Task<IReadOnlyList<ServiceRequestProviderOptionDto>> GetAvailableProvidersAsync(Guid accountId, Guid serviceRequestId, CancellationToken cancellationToken = default) => service.GetAvailableProvidersAsync(accountId, serviceRequestId, cancellationToken);
    public Task AssignProviderAsync(Guid accountId, Guid serviceRequestId, Guid providerOrganizationId, CancellationToken cancellationToken = default) => service.AssignProviderAsync(accountId, serviceRequestId, providerOrganizationId, cancellationToken);
}
