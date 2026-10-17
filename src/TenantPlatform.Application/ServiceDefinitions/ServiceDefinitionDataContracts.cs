using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Services;

namespace TenantPlatform.Application.ServiceDefinitions;

public sealed record ServiceDefinitionUsage(Guid DefinitionId, Guid? ProviderOrganizationId, ServiceRequestStatus Status);
public sealed record ServiceDefinitionState(string DefaultLanguage, List<ServiceDefinition> Definitions,
    List<ServiceDefinitionTranslation> Translations, List<ServiceDefinitionField> Fields,
    List<ServiceDefinitionFieldTranslation> FieldTranslations, List<ServiceDefinitionProvider> Providers,
    List<Organization> Organizations, IReadOnlyList<ServiceDefinitionUsage> Usage, IReadOnlyList<Guid> UsedFieldIds);
public interface IServiceDefinitionData
{
    Task<ServiceDefinitionState> ReadBookableAsync(Guid account, Guid? definition, CancellationToken ct);
    Task<ServiceDefinitionState> ReadAsync(Guid account, Guid? definition, CancellationToken ct);
    Task<IReadOnlyList<ServiceDefinitionUsage>> ReadUsageAsync(Guid account, Guid definition, CancellationToken ct);
    Task<List<Organization>> ReadOrganizationsAsync(Guid account, CancellationToken ct);
    Task<IServiceDefinitionTransaction> BeginAsync(Guid account, CancellationToken ct);
}
public interface IServiceDefinitionTransaction : IAsyncDisposable
{
    ServiceDefinitionState State { get; }
    Task CommitAsync(CancellationToken ct);
}
