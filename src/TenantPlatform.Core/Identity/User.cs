using TenantPlatform.Core.Localization;

namespace TenantPlatform.Core.Identity;

public class User
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string PreferredLanguage { get; set; } = SupportedLanguages.NbNo;

    public bool IsActive { get; set; } = true;
    public bool IsPlatformAdmin { get; set; }
    public LoginAccount? LoginAccount { get; set; }

    public ICollection<UserAccount> Accounts { get; set; }
    = new List<UserAccount>();
}
