using TenantPlatform.Core.Accounts;

namespace TenantPlatform.Core.Identity;

public class UserAccount
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    public User User { get; set; } = null!;

    public Account Account { get; set; } = null!;

    public ICollection<UserAccountRole> UserAccountRoles { get; set; }
        = new List<UserAccountRole>();
}
