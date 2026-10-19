using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Core.PersonalPages;
using TenantPlatform.Infrastructure.Persistence;
namespace TenantPlatform.Infrastructure.PersonalPages;

public sealed class PersonalPageData(IDbContextFactory<TenantPlatformDbContext> factory) : IPersonalPageData
{
    public async Task<PersonalPageCollection> ReadAsync(Guid account, Guid user, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Set<PersonalPageSet>().AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == account && x.OwnerUserId == user, ct);
        return row is null ? new(Guid.Empty, []) : new(row.Revision, JsonSerializer.Deserialize<PersonalPage[]>(row.DefinitionsJson)!);
    }
    public async Task SaveAsync(Guid account, Guid user, Guid expectedRevision, PersonalPageCollection value, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("personal-pages:" + account + ":" + user));
        var key = BinaryPrimitives.ReadInt64LittleEndian(hash);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        var row = await db.Set<PersonalPageSet>().SingleOrDefaultAsync(x => x.AccountId == account && x.OwnerUserId == user, ct);
        if ((row?.Revision ?? Guid.Empty) != expectedRevision) throw new PersonalPageException("Conflict");
        var json = JsonSerializer.Serialize(value.Pages);
        if (Encoding.UTF8.GetByteCount(json) > 524288) throw new PersonalPageException("Limit");
        if (row is null) { row = new() { AccountId = account, OwnerUserId = user }; db.Add(row); }
        row.Revision = value.Revision; row.DefinitionsJson = json;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
    }
}
