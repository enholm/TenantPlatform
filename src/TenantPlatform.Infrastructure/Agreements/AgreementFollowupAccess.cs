using TenantPlatform.Application.Agreements;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Agreements;

public static class AgreementFollowupAccess
{
    public static IQueryable<AgreementResponsibleFacts> Facts(TenantPlatformDbContext db, Guid accountId, Agreement agreement) =>
        db.Users.Select(u => new AgreementResponsibleFacts { UserId = u.Id, AgreementId = agreement.Id, IsActive = u.IsActive,
            IsMember = db.UserAccounts.Any(m => m.AccountId == accountId && m.UserId == u.Id), IsOwner = u.Id == agreement.OwnerUserId,
            IsAccountAdmin = db.UserAccountRoles.Any(r => r.UserAccount.AccountId == accountId && r.UserAccount.UserId == u.Id && r.Role == UserRole.AccountAdmin),
            HasEditGrant = db.AgreementAccess.Any(g => g.AccountId == accountId && g.AgreementId == agreement.Id && g.UserId == u.Id && g.Level == AgreementAccessLevel.Edit) });
    public static IQueryable<User> Editors(TenantPlatformDbContext db, Guid accountId, Agreement agreement)
    {
        var eligible = Facts(db, accountId, agreement).Where(AgreementResponsibleRules.Eligible).Select(x => x.UserId);
        return db.Users.Where(x => eligible.Contains(x.Id));
    }
}
