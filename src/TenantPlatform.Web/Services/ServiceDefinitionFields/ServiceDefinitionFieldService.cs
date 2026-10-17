using TenantPlatform.Application.ServiceDefinitionFields;
namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public sealed class ServiceDefinitionFieldService(TenantPlatform.Application.ServiceDefinitionFields.ServiceDefinitionFieldService service) : IServiceDefinitionFieldService
{
    public Task<ServiceDefinitionFieldDetailsDto?> GetFieldAsync(Guid accountId, Guid serviceDefinitionId, Guid fieldId, CancellationToken cancellationToken = default) => service.GetFieldAsync(accountId, serviceDefinitionId, fieldId, cancellationToken);
    public Task<Guid> CreateFieldAsync(Guid accountId, Guid serviceDefinitionId, CreateServiceDefinitionFieldRequest request, CancellationToken cancellationToken = default) => service.CreateFieldAsync(accountId, serviceDefinitionId, request, cancellationToken);
    public Task UpdateFieldAsync(Guid accountId, Guid serviceDefinitionId, Guid fieldId, UpdateServiceDefinitionFieldRequest request, CancellationToken cancellationToken = default) => service.UpdateFieldAsync(accountId, serviceDefinitionId, fieldId, request, cancellationToken);
    public Task DeleteFieldAsync(Guid accountId, Guid serviceDefinitionId, Guid fieldId, CancellationToken cancellationToken = default) => service.DeleteFieldAsync(accountId, serviceDefinitionId, fieldId, cancellationToken);
}
