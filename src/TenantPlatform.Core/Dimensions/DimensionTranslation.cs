namespace TenantPlatform.Core.Dimensions;

public sealed class DimensionTranslation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid DimensionId { get; set; }
    public string LanguageCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}
