using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Infrastructure.Authentication;

public sealed class OAuthUserData(IDbContextFactory<TenantPlatformDbContext> factory) : IOAuthUserData
{
    public async Task<OAuthUserStatus?> ReadAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.LoginAccounts.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new OAuthUserStatus(x.User.IsActive, x.IsEnabled)).SingleOrDefaultAsync(ct);
    }
}
