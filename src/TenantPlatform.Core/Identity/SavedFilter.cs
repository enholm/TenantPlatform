namespace TenantPlatform.Core.Identity;

public enum SavedFilterPage
{
    Agreements = 1,
    AgreementFollowUp = 2,
    Leasing = 3,
    LeasingFrameworks = 4,
    LeasingOrders = 5
}

public sealed class SavedFilter
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid UserId { get; set; }
    public SavedFilterPage Page { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public int CriteriaVersion { get; set; } = 1;
    public string CriteriaJson { get; set; } = "{}";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
