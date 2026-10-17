using System.Text.Json;
using System.Text.RegularExpressions;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Application.Security;

namespace TenantPlatform.Application.Dimensions;

public sealed record DimensionRegister(List<Dimension> Dimensions, List<DimensionValue> Values, List<DimensionHistory> History, Dictionary<Guid, string> Actors);
public interface IDimensionData
{
    Task<DimensionRegister> ReadAsync(Guid accountId, CancellationToken ct);
    Task<IDimensionTransaction> BeginAsync(Guid accountId, CancellationToken ct);
}
public interface IDimensionTransaction : IAsyncDisposable
{
    List<Dimension> Dimensions { get; }
    List<DimensionValue> Values { get; }
    void Add(Dimension dimension);
    void Add(DimensionValue value);
    void Add(DimensionHistory history);
    Task CommitAsync(CancellationToken ct);
}
public sealed class DimensionService(ICurrentActor actor, IAuthorizationService authorization, IDimensionData data, TimeProvider clock)
{
    private async Task<ActorSnapshot> Require(CancellationToken ct)
    {
        var current = actor.GetSnapshot();
        if (!current.IsAuthenticated || current.AccountId is not Guid account ||
            !await authorization.CanAsync(Permissions.AccountStructure.Manage, new(ResourceKind.Account, AccountId: account), ct))
            throw new UnauthorizedAccessException();
        return current;
    }
    public async Task<DimensionRegister> GetAsync(CancellationToken ct = default)
    {
        var current = await Require(ct);
        return await data.ReadAsync(current.AccountId!.Value, ct);
    }
    public async Task<Guid> SaveDimensionAsync(Guid? id, Dimension input, CancellationToken ct = default)
    {
        input = JsonSerializer.Deserialize<Dimension>(JsonSerializer.Serialize(input))!;
        var current = await Require(ct); var account = current.AccountId!.Value;
        await using var db = await data.BeginAsync(account, ct);
        if (await Require(ct) != current) throw new UnauthorizedAccessException();
        var code = Code(input.Code); ValidateName(input.Name);
        if (input.Description?.Length > 2000 || input.SortOrder < 0) throw new DimensionValidationException("DimensionInvalidInput");
        if (db.Dimensions.Any(x => x.Code == code && x.Id != id)) throw new DimensionValidationException("DimensionDuplicateCode");
        var entity = id.HasValue ? db.Dimensions.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException()
            : new Dimension { Id = Guid.NewGuid(), AccountId = account };
        Revision(id.HasValue, entity.Revision, input.Revision);
        var before = id.HasValue ? JsonSerializer.Serialize(entity) : "{}";
        entity.Name = input.Name.Trim(); entity.Code = code; entity.Description = input.Description?.Trim(); entity.IsActive = input.IsActive;
        entity.AllowMultiple = input.AllowMultiple; entity.LeafOnly = input.LeafOnly; entity.SortOrder = input.SortOrder; entity.Revision = Guid.NewGuid();
        if (!id.HasValue) db.Add(entity);
        History(db, current.UserId, account, entity.Id, null, before, JsonSerializer.Serialize(entity));
        await db.CommitAsync(ct); return entity.Id;
    }
    public async Task<Guid> SaveValueAsync(Guid dimensionId, Guid? id, DimensionValue input, CancellationToken ct = default)
    {
        input = JsonSerializer.Deserialize<DimensionValue>(JsonSerializer.Serialize(input))!;
        var current = await Require(ct); var account = current.AccountId!.Value;
        await using var db = await data.BeginAsync(account, ct);
        if (await Require(ct) != current) throw new UnauthorizedAccessException();
        var dimension = db.Dimensions.SingleOrDefault(x => x.Id == dimensionId) ?? throw new UnauthorizedAccessException();
        if (input.DimensionId != dimensionId) throw new DimensionValidationException("DimensionCannotMoveDimension");
        var values = db.Values.Where(x => x.DimensionId == dimensionId).ToList();
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
        if (!id.HasValue) { db.Add(entity); values.Add(entity); }
        var path = DimensionHierarchy.Build(dimension, values).Single(x => x.Id == entity.Id).Path;
        History(db, current.UserId, account, dimensionId, entity.Id, before, JsonSerializer.Serialize(new { Dimension = dimension.Name, DimensionCode = dimension.Code, Value = entity, Path = path }));
        await db.CommitAsync(ct); return entity.Id;
    }
    // No delete operation: stable references and register history are always retained.
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
    private void History(IDimensionTransaction db, Guid actorId, Guid account, Guid dimension, Guid? value, string before, string after) =>
        db.Add(new DimensionHistory { Id = Guid.NewGuid(), AccountId = account, DimensionId = dimension, ValueId = value,
            ActorUserId = actorId, RecordedUtc = clock.GetUtcNow(), BeforeJson = before, AfterJson = after });
}
