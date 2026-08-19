namespace TenantPlatform.Web.Services.ServiceDefinitionFields;

public interface IServiceDefinitionFieldService
{
    Task<ServiceDefinitionFieldDetailsDto?> GetFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid fieldId,
        CancellationToken cancellationToken = default);

    Task<Guid> CreateFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        CreateServiceDefinitionFieldRequest request,
        CancellationToken cancellationToken = default);

    Task UpdateFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid fieldId,
        UpdateServiceDefinitionFieldRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteFieldAsync(
        Guid accountId,
        Guid serviceDefinitionId,
        Guid fieldId,
        CancellationToken cancellationToken = default);
}

