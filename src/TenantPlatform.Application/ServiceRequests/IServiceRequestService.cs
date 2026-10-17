namespace TenantPlatform.Application.ServiceRequests;

public interface IServiceRequestService
{
    Task<Guid> CreateServiceRequestAsync(
        Guid accountId,
        CreateServiceRequestRequest request,
        CancellationToken cancellationToken = default);

    Task<List<ServiceRequestListItemDto>> GetMyRequestsAsync(
        Guid accountId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<ServiceRequestDetailsDto?> GetRequestAsync(
        Guid accountId,
        Guid requestId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task ApproveRequestAsync(
        Guid accountId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task CompleteRequestAsync(
        Guid accountId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task<List<ServiceRequestListItemDto>> GetAssignedRequestsAsync(
        Guid accountId,
        string languageCode,
        CancellationToken cancellationToken = default);

    Task<ServiceRequestAdminListDto> GetAdminRequestsAsync(
        Guid accountId,
        string languageCode,
        ServiceRequestAdminFilter filter,
        CancellationToken cancellationToken = default);

    Task QueueProviderEmailAsync(
        Guid accountId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task StartWorkAsync(
        Guid accountId,
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task AddCommentAsync(
        Guid accountId,
        Guid requestId,
        string comment,
        CancellationToken cancellationToken = default);

    Task ReplyToEmailAsync(
        Guid accountId,
        Guid requestId,
        Guid messageId,
        string reply,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceRequestProviderOptionDto>>
        GetAvailableProvidersAsync(
            Guid accountId,
            Guid serviceRequestId,
            CancellationToken cancellationToken = default);

    Task AssignProviderAsync(
        Guid accountId,
        Guid serviceRequestId,
        Guid providerOrganizationId,
        CancellationToken cancellationToken = default);
}
