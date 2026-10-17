using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Authentication;
using TenantPlatform.Application.Security;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.Authentication;

public sealed class AuthenticationSessionData(IDbContextFactory<TenantPlatformDbContext> factory) : IAuthenticationSessionData
{
    public async Task<AuthenticationSessionSnapshot?> ReadAsync(Guid userId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
        if (user is null) return null;
        var last = await db.LoginAccounts.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.LastAccountId).SingleOrDefaultAsync(ct);
        var accounts = await db.UserAccounts.AsNoTracking().Where(x => x.UserId == userId).Select(x => x.AccountId).ToListAsync(ct);
        var assignments = await db.UserAccountRoles.AsNoTracking().Where(x => x.UserAccount.UserId == userId)
            .Select(x => new RoleAssignment(x.Role, x.UserAccount.AccountId, x.OrganizationId, x.BuildingId)).ToListAsync(ct);
        return new(user, last, accounts, assignments);
    }
    public async Task RememberAccountAsync(Guid userId, Guid accountId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var login = await db.LoginAccounts.SingleOrDefaultAsync(x => x.UserId == userId && x.User.IsActive &&
            db.UserAccounts.Any(m => m.UserId == userId && m.AccountId == accountId), ct) ?? throw new UnauthorizedAccessException();
        login.LastAccountId = accountId;
        await db.SaveChangesAsync(ct);
    }
    public async Task SaveLanguageAsync(Guid userId, string language, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct) ?? throw new UnauthorizedAccessException();
        user.PreferredLanguage = language;
        await db.SaveChangesAsync(ct);
    }
}
