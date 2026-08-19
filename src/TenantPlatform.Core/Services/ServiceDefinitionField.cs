namespace TenantPlatform.Core.Services;

public class ServiceDefinitionField
{
    public Guid Id { get; set; }

    public Guid ServiceDefinitionId { get; set; }

    public string Key { get; set; } = string.Empty;

    public ServiceFieldType FieldType { get; set; }

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }

    public string? OptionsJson { get; set; }
}
