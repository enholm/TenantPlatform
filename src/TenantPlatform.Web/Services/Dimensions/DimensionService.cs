using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.Dimensions;

public sealed record DimensionRegister(List<Dimension> Dimensions, List<DimensionValue> Values, List<DimensionHistory> History, Dictionary<Guid, string> Actors);
public sealed class DimensionService(IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService context, ITenantAuthorizationService authorization, TimeProvider clock)
{
    private async Task<Guid> Require(TenantPlatformDbContext db, CancellationToken ct)
    {
        var user = context.Current;
        if (!user.IsAuthenticated || user.CurrentAccountId is not Guid account ||
            !await authorization.CanManageAccountStructureAsync(ct) ||
            !await db.UserAccounts.AnyAsync(x => x.AccountId == account && x.UserId == user.UserId && x.User.IsActive, ct)) throw new UnauthorizedAccessException();
        return account;
    }
    public async Task<DimensionRegister> GetAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var account = await Require(db, ct);
        return new(await db.Dimensions.AsNoTracking().Where(x => x.AccountId == account).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct),
            await db.DimensionValues.AsNoTracking().Where(x => x.AccountId == account).ToListAsync(ct),
            await db.DimensionHistory.AsNoTracking().Where(x => x.AccountId == account).OrderByDescending(x => x.RecordedUtc).ToListAsync(ct),
            await db.UserAccounts.AsNoTracking().Where(x => x.AccountId == account).Select(x => new { x.UserId, Name = x.User.FirstName + " " + x.User.LastName }).ToDictionaryAsync(x => x.UserId, x => x.Name, ct));
    }
    public async Task<Guid> SaveDimensionAsync(Guid? id, Dimension input, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var account = await Require(db, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var code = Code(input.Code); ValidateName(input.Name);
        if (input.Description?.Length > 2000 || input.SortOrder < 0) throw new DimensionValidationException("DimensionInvalidInput");
        if (await db.Dimensions.AnyAsync(x => x.AccountId == account && x.Code == code && x.Id != id, ct)) throw new DimensionValidationException("DimensionDuplicateCode");
        var entity = id.HasValue ? await db.Dimensions.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == id, ct) ?? throw new UnauthorizedAccessException()
            : new Dimension { Id = Guid.NewGuid(), AccountId = account };
        Revision(id.HasValue, entity.Revision, input.Revision);
        var before = id.HasValue ? JsonSerializer.Serialize(entity) : "{}";
        entity.Name = input.Name.Trim(); entity.Code = code; entity.Description = input.Description?.Trim(); entity.IsActive = input.IsActive;
        entity.AllowMultiple = input.AllowMultiple; entity.LeafOnly = input.LeafOnly; entity.SortOrder = input.SortOrder; entity.Revision = Guid.NewGuid();
        if (!id.HasValue) db.Dimensions.Add(entity);
        History(db, account, entity.Id, null, before, JsonSerializer.Serialize(entity));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return entity.Id;
    }
    public async Task<Guid> SaveValueAsync(Guid dimensionId, Guid? id, DimensionValue input, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct); var account = await Require(db, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(db, account, ct);
        var dimension = await db.Dimensions.SingleOrDefaultAsync(x => x.AccountId == account && x.Id == dimensionId, ct) ?? throw new UnauthorizedAccessException();
        if (input.DimensionId != dimensionId) throw new DimensionValidationException("DimensionCannotMoveDimension");
        var values = await db.DimensionValues.Where(x => x.AccountId == account && x.DimensionId == dimensionId).ToListAsync(ct);
        var code = Code(input.Code); ValidateName(input.Name);
        if (input.SortOrder < 0) throw new DimensionValidationException("DimensionInvalidInput");
        if (values.Any(x => x.Code == code && x.Id != id)) throw new DimensionValidationException("DimensionDuplicateCode");
        var entity = id.HasValue ? values.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException() : new DimensionValue { Id = Guid.NewGuid(), AccountId = account, DimensionId = dimensionId };
        Revision(id.HasValue, entity.Revision, input.Revision);
        var beforePath = id.HasValue ? DimensionHierarchy.Build(dimension, values).Single(x => x.Id == id).Path : null;
        var before = id.HasValue ? JsonSerializer.Serialize(new { Dimension = dimension.Name, DimensionCode = dimension.Code, Value = entity, Path = beforePath }) : "{}";
        var parent = input.ParentId; var visited = new HashSet<Guid>();
        while (parent.HasValue)
        {
            if (parent == entity.Id || !visited.Add(parent.Value)) throw new DimensionValidationException("DimensionCycle");
            var ancestor = values.SingleOrDefault(x => x.Id == parent) ?? throw new DimensionValidationException("DimensionInvalidParent");
            parent = ancestor.ParentId;
        }
        entity.Name = input.Name.Trim(); entity.Code = code; entity.ParentId = input.ParentId; entity.IsActive = input.IsActive; entity.SortOrder = input.SortOrder; entity.Revision = Guid.NewGuid();
        if (!id.HasValue) { db.DimensionValues.Add(entity); values.Add(entity); }
        var path = DimensionHierarchy.Build(dimension, values).Single(x => x.Id == entity.Id).Path;
        History(db, account, dimensionId, entity.Id, before, JsonSerializer.Serialize(new { Dimension = dimension.Name, DimensionCode = dimension.Code, Value = entity, Path = path }));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return entity.Id;
    }
    // No delete operation: stable references and register history are always retained.
    private static Task Lock(TenantPlatformDbContext db, Guid account, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM accounts WHERE \"Id\" = {account} FOR UPDATE", ct);
    private static void Revision(bool exists, Guid actual, Guid supplied)
    { if (exists && actual != supplied) throw new DimensionValidationException("DimensionConcurrency"); }
    private static void ValidateName(string? name)
    { if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new DimensionValidationException("DimensionInvalidInput"); }
    private static string Code(string? code)
    {
        var normalized = code?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(normalized, "^[A-Z0-9_.-]{1,50}$", RegexOptions.CultureInvariant)) throw new DimensionValidationException("DimensionInvalidCode");
        return normalized;
    }
    private void History(TenantPlatformDbContext db, Guid account, Guid dimension, Guid? value, string before, string after) =>
        db.DimensionHistory.Add(new DimensionHistory { Id = Guid.NewGuid(), AccountId = account, DimensionId = dimension, ValueId = value,
            ActorUserId = context.Current.UserId, RecordedUtc = clock.GetUtcNow(), BeforeJson = before, AfterJson = after });
}
