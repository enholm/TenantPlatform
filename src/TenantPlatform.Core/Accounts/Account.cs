using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Localization;

namespace TenantPlatform.Core.Accounts;

public class Account
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DefaultLanguage { get; set; } = SupportedLanguages.NbNo;

    public bool IsActive { get; set; } = true;
    public ICollection<UserAccount> Users { get; set; }
    = new List<UserAccount>();
}
