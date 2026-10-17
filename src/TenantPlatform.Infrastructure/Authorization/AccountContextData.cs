using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Authorization;

public sealed class AccountContextData(IDbContextFactory<TenantPlatformDbContext> factory) : IAccountContextData
{
    public async Task<IReadOnlyList<MemberAccount>> ReadMemberAccountsAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.UserAccounts.AsNoTracking().Where(x => x.UserId == userId && x.User.IsActive)
            .OrderBy(x => x.Account.Name).ThenBy(x => x.AccountId)
            .Select(x => new MemberAccount(x.AccountId, x.Account.Name)).ToListAsync(ct);
    }
}
