namespace TenantPlatform.Web.Services.AccountSettings;

public sealed class AccountRegisterInput
{
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
