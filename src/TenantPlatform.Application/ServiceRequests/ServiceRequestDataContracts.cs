using TenantPlatform.Core.Occupancies;
using TenantPlatform.Core.Properties;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceRequests;

public sealed record ServiceRequestState(ServiceRequest? Request, ServiceDefinition? Definition, Occupancy? Occupancy, Unit? Unit,
    List<ServiceDefinitionField> Fields, List<ServiceDefinitionProvider> Providers, List<ServiceRequestMessage> Messages,
    List<ServiceRequestFieldValue> Values, List<EmailOutboxMessage> Outbox);
public interface IServiceRequestData
{
    Task<ServiceRequest?> ReadRequestAsync(Guid account, Guid request, CancellationToken ct);
    Task<IReadOnlyList<ServiceRequestProviderOptionDto>> ReadProviderOptionsAsync(Guid account, Guid definition, CancellationToken ct);
    Task<List<ServiceRequestListItemDto>> ReadListAsync(ServiceRequestReadScope scope, ServiceRequestListKind kind, string language, CancellationToken ct);
    Task<List<ServiceRequestAdminListItemDto>> ReadAdminAsync(ServiceRequestReadScope scope, string language, CancellationToken ct);
    Task<ServiceRequestDetailsDto?> ReadDetailsAsync(ServiceRequestReadScope scope, Guid id, string language, CancellationToken ct);
    Task<ServiceRequestDetailsDto?> ReadEmailDetailsAsync(ServiceRequestReadScope scope, Guid id, CancellationToken ct);
    Task<IServiceRequestTransaction> BeginCreateAsync(Guid account, Guid definition, Guid occupancy, CancellationToken ct);
    Task<IServiceRequestTransaction> BeginRequestAsync(Guid account, Guid request, CancellationToken ct);
}
public interface IServiceRequestTransaction : IAsyncDisposable
{
    ServiceRequestState State { get; }
    Task CommitAsync(ServiceRequest request, bool create, CancellationToken ct);
}
