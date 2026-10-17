using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Leasing;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Leasing;
// Raw membership/ownership facts only. Application is the sole owner of recipient eligibility.
public static class LeasingLifecycleRecipientData
{
    public static async Task<LeasingRecipientScope> ReadAsync(TenantPlatformDbContext db, Guid account, Guid user, Guid? acquisition, Guid? framework, CancellationToken ct)
    {
        var member = await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account && x.UserId == user).Select(x => new { x.Id, x.User.IsActive }).SingleOrDefaultAsync(ct);
        var roles = member is null ? [] : await db.UserAccountRoles.AsNoTracking().Where(x => x.UserAccountId == member.Id).Select(x => x.Role).ToListAsync(ct);
        var a = acquisition.HasValue ? await db.LeasingAcquisitions.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == acquisition, ct) : null;
        var frameworkId = framework ?? a?.FrameworkId;
        var f = frameworkId.HasValue ? await db.LeasingFrameworks.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.Id == frameworkId, ct) : null;
        return new(member is null ? null : new(user, member.IsActive, true, roles), a, f);
    }
}
