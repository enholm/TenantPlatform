using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.SavedFilters;

public sealed record SavedFilterDto(Guid Id, string Name, string CriteriaJson, DateTimeOffset UpdatedUtc);
public sealed class SavedFilterValidationException(string resourceKey) : Exception(resourceKey);

public interface ISavedFilterService
{
    Task<IReadOnlyList<SavedFilterDto>> ListAsync(Guid accountId, SavedFilterPage page, CancellationToken ct = default);
    Task<SavedFilterDto> CreateAsync(Guid accountId, SavedFilterPage page, string name, string criteriaJson,
        CancellationToken ct = default);
    Task<SavedFilterDto> OverwriteAsync(Guid accountId, SavedFilterPage page, Guid filterId, string criteriaJson,
        CancellationToken ct = default);
}

public sealed class SavedFilterService(
    IDbContextFactory<TenantPlatformDbContext> factory,
    ICurrentUserContextService userContext,
    TimeProvider clock) : ISavedFilterService
{
    private const int JsonLimit = 65_536;

    public async Task<IReadOnlyList<SavedFilterDto>> ListAsync(Guid accountId, SavedFilterPage page,
        CancellationToken ct = default)
    {
        ValidatePage(page);
        await using var db = await factory.CreateDbContextAsync(ct);
        var userId = await UserAsync(db, accountId, ct);
        return await db.SavedFilters.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.UserId == userId && x.Page == page)
            .OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new SavedFilterDto(x.Id, x.Name, x.CriteriaJson, x.UpdatedUtc))
            .ToListAsync(ct);
    }

    public async Task<SavedFilterDto> CreateAsync(Guid accountId, SavedFilterPage page, string name,
        string criteriaJson, CancellationToken ct = default)
    {
        ValidatePage(page);
        var cleanName = Name(name);
        ValidateJson(criteriaJson);
        await using var db = await factory.CreateDbContextAsync(ct);
        var userId = await UserAsync(db, accountId, ct);
        var normalized = Normalize(cleanName);
        if (await db.SavedFilters.AnyAsync(x => x.AccountId == accountId && x.UserId == userId &&
            x.Page == page && x.NormalizedName == normalized, ct))
            throw new SavedFilterValidationException("SavedFilterNameExists");
        var now = clock.GetUtcNow();
        var entity = new SavedFilter
        {
            Id = Guid.NewGuid(), AccountId = accountId, UserId = userId, Page = page,
            Name = cleanName, NormalizedName = normalized, CriteriaJson = criteriaJson,
            CriteriaVersion = 1, CreatedUtc = now, UpdatedUtc = now
        };
        db.SavedFilters.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The pre-check provides a useful validation response while the unique index
            // remains the authority for a concurrent create with the same name.
            if (await NameExistsAsync(db, accountId, userId, page, normalized, ct))
                throw new SavedFilterValidationException("SavedFilterNameExists");
            throw;
        }
        return Map(entity);
    }

    public async Task<SavedFilterDto> OverwriteAsync(Guid accountId, SavedFilterPage page, Guid filterId,
        string criteriaJson, CancellationToken ct = default)
    {
        ValidatePage(page);
        if (filterId == Guid.Empty) Invalid("SavedFilterSelectionRequired");
        ValidateJson(criteriaJson);
        await using var db = await factory.CreateDbContextAsync(ct);
        var userId = await UserAsync(db, accountId, ct);
        var entity = await db.SavedFilters.SingleOrDefaultAsync(x => x.AccountId == accountId &&
            x.UserId == userId && x.Page == page && x.Id == filterId, ct)
            ?? throw new UnauthorizedAccessException();
        entity.CriteriaJson = criteriaJson;
        entity.UpdatedUtc = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    private async Task<Guid> UserAsync(TenantPlatformDbContext db, Guid accountId, CancellationToken ct)
    {
        var current = userContext.Current;
        if (!current.IsAuthenticated || current.CurrentAccountId != accountId)
            throw new UnauthorizedAccessException();
        var active = await db.UserAccounts.AsNoTracking().AnyAsync(x => x.AccountId == accountId &&
            x.UserId == current.UserId && x.User.IsActive, ct);
        if (!active) throw new UnauthorizedAccessException();
        return current.UserId;
    }

    private static async Task<bool> NameExistsAsync(TenantPlatformDbContext db, Guid accountId, Guid userId,
        SavedFilterPage page, string normalized, CancellationToken ct)
    {
        foreach (var entry in db.ChangeTracker.Entries<SavedFilter>()) entry.State = EntityState.Detached;
        return await db.SavedFilters.AsNoTracking().AnyAsync(x => x.AccountId == accountId && x.UserId == userId &&
            x.Page == page && x.NormalizedName == normalized, ct);
    }

    private static string Name(string? value)
    {
        var name = value?.Trim();
        if (string.IsNullOrWhiteSpace(name)) Invalid("SavedFilterNameRequired");
        if (name.Length > 200) Invalid("SavedFilterNameTooLong");
        return name;
    }

    private static string Normalize(string value) => value.ToUpperInvariant();
    private static void ValidatePage(SavedFilterPage page)
    {
        if (!Enum.IsDefined(page)) Invalid("SavedFilterPageInvalid");
    }

    private static void ValidateJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > JsonLimit) Invalid("SavedFilterCriteriaInvalid");
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object) Invalid("SavedFilterCriteriaInvalid");
        }
        catch (JsonException) { Invalid("SavedFilterCriteriaInvalid"); }
    }

    private static SavedFilterDto Map(SavedFilter value) => new(value.Id, value.Name, value.CriteriaJson, value.UpdatedUtc);
    [DoesNotReturn] private static void Invalid(string key) => throw new SavedFilterValidationException(key);
}
