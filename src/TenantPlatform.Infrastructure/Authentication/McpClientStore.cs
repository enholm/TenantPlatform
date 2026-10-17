using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence.OAuth;

namespace TenantPlatform.Infrastructure.Authentication;

public sealed class McpClientStore(DbContextOptions<OAuthDbContext> options) : IMcpClientStore
{
    public async Task<T> WithUserLockAsync<T>(Guid user, Func<IMcpClientSlot, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("TenantPlatform.InternalMcp:" + user.ToString("D"))));
        // This dedicated transaction holds ONLY a coordination lock. Individual credential saves below
        // use separate contexts/transactions so "consumed" is durable before a refresh is sent.
        await using var coordination = new OAuthDbContext(options);
        await using var transaction = await coordination.Database.BeginTransactionAsync(ct);
        await coordination.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        return await action(new Slot(options, user), ct); // disposal releases lock on success, error and cancellation
    }
    private sealed class Slot(DbContextOptions<OAuthDbContext> options, Guid user) : IMcpClientSlot
    {
        public async Task<McpClientCredential?> ReadAsync(CancellationToken ct)
        {
            await using var db = new OAuthDbContext(options);
            return await db.Set<McpClientCredential>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user, ct);
        }
        public async Task SaveAsync(McpClientCredential? value, CancellationToken ct)
        {
            if (value is not null && value.UserId != user) throw new InvalidOperationException("Credential slot mismatch.");
            await using var db = new OAuthDbContext(options);
            var existing = await db.Set<McpClientCredential>().SingleOrDefaultAsync(x => x.UserId == user, ct);
            if (value is null) { if (existing is not null) db.Remove(existing); }
            else if (existing is null) db.Add(value);
            else db.Entry(existing).CurrentValues.SetValues(value);
            await db.SaveChangesAsync(ct);
        }
    }
}
