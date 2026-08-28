namespace TenantPlatform.Web.Services.ServiceRequests;

public interface IServiceRequestService
{
    Task<Guid> CreateServiceRequestAsync(
        Guid accountId,
        Guid requesterUserId,
        CreateServiceRequestRequest request,
        CancellationToken cancellationToken = default);

    Task<List<ServiceRequestListItemDto>> GetMyRequestsAsync(
        Guid accountId,
        Guid userId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<ServiceRequestDetailsDto?> GetRequestAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task ApproveRequestAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task CompleteRequestAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<List<ServiceRequestListItemDto>> GetAssignedRequestsAsync(
        Guid accountId,
        Guid userId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task QueueProviderEmailAsync(
        Guid accountId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task StartWorkAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task AddCommentAsync(
        Guid accountId,
        Guid requestId,
        Guid userId,
        string comment,
        CancellationToken cancellationToken = default);
}

