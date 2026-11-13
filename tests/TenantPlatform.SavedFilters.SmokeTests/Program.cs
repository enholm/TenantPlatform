using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;
using TenantPlatform.Web.Services.SavedFilters;

var options = new DbContextOptionsBuilder<TenantPlatformDbContext>()
    .UseInMemoryDatabase("saved-filter-smoke-" + Guid.NewGuid())
    .Options;
var factory = new TestFactory(options);
var accountA = Guid.NewGuid();
var accountB = Guid.NewGuid();
var userA = Guid.NewGuid();
var userB = Guid.NewGuid();

await using (var db = await factory.CreateDbContextAsync())
{
    db.Accounts.AddRange(
        new Account { Id = accountA, Name = "Account A" },
        new Account { Id = accountB, Name = "Account B" });
    db.Users.AddRange(
        new User { Id = userA, Email = "a@example.test", FirstName = "A", LastName = "User" },
        new User { Id = userB, Email = "b@example.test", FirstName = "B", LastName = "User" });
    db.UserAccounts.AddRange(
        new UserAccount { Id = Guid.NewGuid(), AccountId = accountA, UserId = userA },
        new UserAccount { Id = Guid.NewGuid(), AccountId = accountA, UserId = userB },
        new UserAccount { Id = Guid.NewGuid(), AccountId = accountB, UserId = userA });
    await db.SaveChangesAsync();

    var entity = db.Model.FindEntityType(typeof(SavedFilter))!;
    Check.That(entity.GetTableName() == "saved_filters", "saved filters use the mapped persistent table");
    Check.That(entity.FindProperty(nameof(SavedFilter.CriteriaJson))?
            .FindAnnotation(RelationalAnnotationNames.ColumnType)?.Value as string == "jsonb",
        "saved criteria use PostgreSQL jsonb");
    Check.That(entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(
        [nameof(SavedFilter.AccountId), nameof(SavedFilter.UserId), nameof(SavedFilter.Page), nameof(SavedFilter.NormalizedName)])),
        "normalized names are unique per account, user and page");
}

CriteriaChecks.Run();
await UiChecks.RunAsync();

var current = new TestUserContext(userA, accountA);
var service = new SavedFilterService(factory, current,
    new FixedTimeProvider(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero)));

await Check.ThrowsAsync<SavedFilterValidationException>(
    () => service.CreateAsync(accountA, SavedFilterPage.Agreements, "Invalid", "[]"),
    "non-object criteria are rejected");

var agreement = await service.CreateAsync(accountA, SavedFilterPage.Agreements, "  My filter  ", "{\"search\":\"one\"}");
Check.That(agreement.Name == "My filter", "saved filter names are trimmed");
var leasing = await service.CreateAsync(accountA, SavedFilterPage.Leasing, "My filter", "{\"search\":\"lease\"}");
Check.That(leasing.Id != agreement.Id, "the same name can be used on another page");
Check.Sequence((await service.ListAsync(accountA, SavedFilterPage.Agreements)).Select(x => x.Id), [agreement.Id],
    "listing is scoped to the current page");

await Check.ThrowsAsync<SavedFilterValidationException>(
    () => service.CreateAsync(accountA, SavedFilterPage.Agreements, "my FILTER", "{}"),
    "case-insensitive name collisions do not overwrite implicitly");

var overwritten = await service.OverwriteAsync(accountA, SavedFilterPage.Agreements, agreement.Id,
    "{\"search\":\"changed\"}");
Check.That(overwritten.Name == "My filter" && overwritten.CriteriaJson.Contains("changed", StringComparison.Ordinal),
    "overwrite changes only the selected filter criteria");

current.Set(userB, accountA);
Check.That((await service.ListAsync(accountA, SavedFilterPage.Agreements)).Count == 0,
    "another user cannot see the first user's filters");
await Check.ThrowsAsync<UnauthorizedAccessException>(
    () => service.OverwriteAsync(accountA, SavedFilterPage.Agreements, agreement.Id, "{}"),
    "another user cannot overwrite the first user's filter");
var secondUser = await service.CreateAsync(accountA, SavedFilterPage.Agreements, "My filter", "{}");
Check.That(secondUser.Id != agreement.Id, "the same name is allowed for another user");

current.Set(userA, accountB);
Check.That((await service.ListAsync(accountB, SavedFilterPage.Agreements)).Count == 0,
    "filters are isolated between selected accounts");
await Check.ThrowsAsync<UnauthorizedAccessException>(
    () => service.ListAsync(accountA, SavedFilterPage.Agreements),
    "the server rejects a tenant other than the selected account");
await Check.ThrowsAsync<SavedFilterValidationException>(
    () => service.ListAsync(accountB, (SavedFilterPage)999),
    "unknown page identities are rejected");

Console.WriteLine($"Saved-filter smoke checks passed ({Check.Count} checks; EF InMemory only; no database connection or app startup). ");

static class CriteriaChecks
{
    public static void Run()
    {
        var dimensionA = Guid.NewGuid();
        var valueA = Guid.NewGuid();
        var source = new AgreementFilter
        {
            Search = "current draft", Status = AgreementStatus.Draft, Type = AgreementType.Lease,
            OwnerUserId = Guid.NewGuid(), Attention = AgreementAttention.Renewal, Page = 9,
            Dimensions = [new DimensionListFilterSelection { DimensionId = dimensionA, ValueId = valueA }]
        };
        var target = new AgreementFilter
        {
            Search = "stale", Status = AgreementStatus.Active, Page = 7,
            Dimensions = [new DimensionListFilterSelection { DimensionId = Guid.NewGuid(), IncludeUnspecified = true }]
        };
        var json = SavedFilterCriteria.Capture(source);
        SavedFilterCriteria.Apply(json, target);
        Check.That(target.Search == source.Search && target.Status == source.Status && target.Type == source.Type &&
            target.OwnerUserId == source.OwnerUserId && target.Attention == source.Attention && target.Page == 1,
            "agreement criteria fully replace old fields and reset paging");
        Check.That(target.Dimensions.Count == 1 && target.Dimensions[0].DimensionId == dimensionA &&
            target.Dimensions[0].ValueId == valueA, "agreement dimension criteria round-trip");
        Check.That(!json.Contains("page", StringComparison.OrdinalIgnoreCase), "paging is not persisted");

        var followup = new AgreementDeadlineFilter
        {
            Search = "deadline", AssignedUserId = Guid.NewGuid(), AgreementType = AgreementType.ServiceMaintenance,
            Kind = AgreementDeadlineKind.Renewal, Status = AgreementFollowupStatus.Completed,
            State = AgreementDeadlineState.Replaced, Mine = true, IncludeCompleted = true,
            HorizonDays = 90, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 2, 1), Page = 4
        };
        var followupTarget = new AgreementDeadlineFilter { Search = "stale", CustomDates = true, Page = 8 };
        var followupJson = SavedFilterCriteria.Capture(followup, 90);
        var range = SavedFilterCriteria.Apply(followupJson, followupTarget);
        Check.That(range == 90 && !followupTarget.CustomDates && followupTarget.From is null && followupTarget.To is null &&
            followupTarget.Search == "deadline" && followupTarget.Mine && followupTarget.IncludeCompleted && followupTarget.Page == 1,
            "follow-up presets round-trip and replace inactive custom dates");

        followup.CustomDates = true;
        var customJson = SavedFilterCriteria.Capture(followup, 0);
        range = SavedFilterCriteria.Apply(customJson, followupTarget);
        Check.That(range == 0 && followupTarget.CustomDates && followupTarget.From == followup.From && followupTarget.To == followup.To,
            "follow-up custom dates round-trip");

        var leasing = new LeasingListFilter
        {
            Search = "equipment", Kind = "orders", Status = "Draft", Owner = Guid.NewGuid(), Page = 5,
            Dimensions = [new DimensionListFilterSelection { DimensionId = dimensionA, IncludeUnspecified = true }]
        };
        var leasingTarget = new LeasingListFilter
        {
            Search = "stale", Kind = "all", Page = 3,
            Dimensions = [new DimensionListFilterSelection { DimensionId = Guid.NewGuid(), ValueId = Guid.NewGuid() }]
        };
        SavedFilterCriteria.Apply(SavedFilterCriteria.Capture(leasing), leasingTarget);
        Check.That(leasingTarget.Search == leasing.Search && leasingTarget.Kind == "orders" &&
            leasingTarget.Status == leasing.Status && leasingTarget.Owner == leasing.Owner && leasingTarget.Page == 1,
            "leasing criteria fully replace old fields and reset paging");
        Check.That(leasingTarget.Dimensions.Count == 1 && leasingTarget.Dimensions[0].IncludeUnspecified &&
            leasingTarget.Dimensions[0].DimensionId == dimensionA, "leasing not-specified dimensions round-trip");
        Check.Throws<SavedFilterValidationException>(() =>
            SavedFilterCriteria.Apply("{\"search\":null,\"status\":999,\"type\":null,\"ownerUserId\":null,\"attention\":null,\"dimensions\":[]}", target),
            "unknown saved enum values are rejected");
    }
}

sealed class TestFactory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{
    public TenantPlatformDbContext CreateDbContext() => new(options);
    public Task<TenantPlatformDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}

sealed class TestUserContext(Guid userId, Guid accountId) : ICurrentUserContextService
{
    public CurrentUserContext Current { get; private set; } = Context(userId, accountId);
    public void Set(Guid user, Guid account) => Current = Context(user, account);
    private static CurrentUserContext Context(Guid user, Guid account) =>
        new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account };
}

sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => value;
}

static class Check
{
    public static int Count { get; private set; }
    public static void That(bool condition, string description)
    {
        Count++;
        if (!condition) throw new InvalidOperationException($"Check failed: {description}");
    }

    public static void Sequence<T>(IEnumerable<T> actual, IEnumerable<T> expected, string description) =>
        That(actual.SequenceEqual(expected), description);

    public static async Task ThrowsAsync<T>(Func<Task> action, string description) where T : Exception
    {
        try { await action(); }
        catch (T) { Count++; return; }
        throw new InvalidOperationException($"Check failed: {description}");
    }

    public static void Throws<T>(Action action, string description) where T : Exception
    {
        try { action(); }
        catch (T) { Count++; return; }
        throw new InvalidOperationException($"Check failed: {description}");
    }
}
