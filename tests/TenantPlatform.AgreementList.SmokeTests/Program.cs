using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Application;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

await SnapshotChecks();
await AttentionChecks.Snapshot();
await DashboardChecks.Snapshot();
await InsightsChecks.PureAndSnapshot();
await DetailsChecks.Snapshot();
await FollowupChecks.Snapshot();
var guard = new ReadOnlyGuard();
var options = new DbContextOptionsBuilder<TenantPlatformDbContext>()
    .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new AuditSaveChangesInterceptor(new AuditActor()), guard).Options;
await Run(new Factory(options), guard, "InMemory");

// Translation only: never opens a connection or initializes an application database.
await using (var sql = new TenantPlatformDbContext(new DbContextOptionsBuilder<TenantPlatformDbContext>()
    .UseNpgsql("Host=localhost;Database=translation_only").Options))
{
    foreach (var kind in Enum.GetValues<AgreementAttention>())
    {
        var query = AgreementListQueries.Attention(sql.Agreements.Where(x => x.AccountId == Guid.Empty),
            AgreementAttentionRules.Create(kind, new(2026, 10, 17))).ToQueryString();
        Check(query.Contains("AccountId") && query.Contains("IsArchived") && query.Contains("Status"), "PostgreSQL Attention translation: " + kind);
    }
}
var connection = Environment.GetEnvironmentVariable("AGREEMENT_TEST_CONNECTION");
if (string.IsNullOrWhiteSpace(connection)) Console.WriteLine("SKIP PostgreSQL execution: AGREEMENT_TEST_CONNECTION is not configured.");
else
{
    var cs = new NpgsqlConnectionStringBuilder(connection);
    if (cs.Database != "tenant_agreement_tests") throw new InvalidOperationException("Only tenant_agreement_tests is allowed.");
    var schema = "agreement_list_" + Guid.NewGuid().ToString("N");
    await using var setup = new NpgsqlConnection(connection); await setup.OpenAsync();
    await using (var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", setup)) await command.ExecuteNonQueryAsync();
    try
    {
        cs.SearchPath = schema;
        var postgresGuard = new ReadOnlyGuard();
        var factory = new Factory(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseNpgsql(cs.ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditActor()), postgresGuard).Options);
        await using (var db = factory.CreateDbContext()) await db.Database.MigrateAsync();
        await Run(factory, postgresGuard, "PostgreSQL");
    }
    finally
    {
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", setup); await command.ExecuteNonQueryAsync();
    }
}
Console.WriteLine("Agreement list checks passed.");

static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
{
    var a = Guid.NewGuid(); var b = Guid.NewGuid(); var user = Guid.NewGuid(); var other = Guid.NewGuid();
    var hiddenOwner = Guid.NewGuid(); var admin = Guid.NewGuid(); var foreign = Guid.NewGuid(); var platform = Guid.NewGuid();
    var partyA = Guid.NewGuid(); var partyB = Guid.NewGuid();
    var clock = new Clock(new(2026, 10, 16, 22, 30, 0, TimeSpan.Zero)); // 17 October in Oslo, still 16 October UTC.
    var today = new DateOnly(2026, 10, 17);
    var membershipIds = new Dictionary<Guid, Guid>();
    Agreement Make(string name, Guid owner, Guid? account = null) => new()
    {
        Id = Guid.NewGuid(), AccountId = account ?? a, Title = name, OwnerUserId = owner,
        CounterpartyOrganizationId = account == b ? partyB : partyA, Type = AgreementType.Other, Status = AgreementStatus.Active,
        StartDate = new(2020, 1, 1), EndDate = today.AddDays(120), NoticeDeadline = today,
        RenewalDate = today.AddDays(90), Form = AgreementForm.Legacy,
        CreatedUtc = clock.GetUtcNow(), UpdatedUtc = clock.GetUtcNow(), CreatedByUserId = owner, UpdatedByUserId = owner,
        Revision = Guid.NewGuid()
    };
    var own = Make("Owned", user); var read = Make("Read shared", other); var edit = Make("Edit shared", other);
    var hidden = Make("Hidden", hiddenOwner); var foreignAgreement = Make("Foreign", foreign, b);
    var archived = Make("Archived", user); archived.IsArchived = true;
    var expired = Make("Expired", user); expired.EndDate = today.AddDays(-1);
    var draft = Make("Draft", user); draft.Status = AgreementStatus.Draft;
    var terminated = Make("Terminated", user); terminated.Status = AgreementStatus.Terminated;
    var day90 = Make("Boundary90", user); day90.NoticeDeadline = today.AddDays(90);
    var day91 = Make("Boundary91", user); day91.NoticeDeadline = today.AddDays(91);
    var paging = Enumerable.Range(0, 28).Select(_ => Make("Paging", other)).ToArray();
    await using (var db = factory.CreateDbContext())
    {
        db.Accounts.AddRange(new Account { Id = a, Name = "List A" }, new Account { Id = b, Name = "List B" });
        foreach (var id in new[] { user, other, hiddenOwner, admin, foreign, platform })
        {
            db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = id.ToString(), LastName = "List", IsActive = true, IsPlatformAdmin = id == platform });
            var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = id == foreign ? b : a, UserId = id };
            membershipIds[id] = membership.Id; db.UserAccounts.Add(membership);
            if (id == admin || id == foreign) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
        }
        db.Organizations.AddRange(new Organization { Id = partyA, AccountId = a, Name = "Counterparty A" }, new Organization { Id = partyB, AccountId = b, Name = "Counterparty B" });
        db.Agreements.AddRange(new[] { own, read, edit, hidden, foreignAgreement, archived, expired, draft, terminated, day90, day91 }.Concat(paging));
        db.AgreementAccess.AddRange(new AgreementAccess { Id = Guid.NewGuid(), AccountId = a, AgreementId = read.Id, UserId = user, Level = AgreementAccessLevel.Read },
            new AgreementAccess { Id = Guid.NewGuid(), AccountId = a, AgreementId = edit.Id, UserId = user, Level = AgreementAccessLevel.Edit });
        await db.SaveChangesAsync();
    }
    await AuthorizationChecks.Run(factory, a, b, user, membershipIds[user], admin, platform, partyA, own.Id, read.Id, edit.Id, hidden.Id, foreignAgreement.Id, archived.Id, provider);
    await AttentionChecks.Run(factory, guard, a, b, user, admin, platform, membershipIds[user], read.Id, partyA, provider);
    await DashboardChecks.Run(factory, guard, provider);
    await InsightsChecks.Run(factory, guard, provider);
    await DetailsChecks.Run(factory, guard, provider);
    await FollowupChecks.Run(factory, guard, provider);
    await BuildingChecks.Run(factory, guard, provider);
    await UserAdministrationChecks.Run(factory, guard, provider);
    await OccupancyChecks.Run(factory, guard, provider);
    await ServiceDefinitionChecks.Run(factory, guard, provider);
    await ServiceRequestChecks.Run(factory, guard, provider);
    await AgreementDocumentChecks.Run(factory, guard, provider);
    await AuthenticationChecks.Run(factory, guard, provider);
    await BootstrapChecks.Run(factory, provider);
    await MailJobChecks.Run(factory, provider);
    var actor = new Actor(new(true, user, a));
    var data = new AgreementListData(factory);
    var service = new AgreementListService(new TenantPlatform.Application.Security.AuthorizationService(actor, new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), data, clock);
    async Task<AgreementPageDto> List(AgreementFilter? filter = null)
    {
        guard.Protect = true;
        try { return await service.ListAsync(a, filter ?? new()); }
        finally { guard.Protect = false; }
    }
    var first = await List();
    Check(first.TotalCount == 9 && !first.Items.Any(x => x.Id == hidden.Id || x.Id == foreignAgreement.Id), provider + " owner, Read/Edit grants, rows and count");
    Check(first.Owners.Select(x => x.Id).ToHashSet().SetEquals([user, other]), "owner choices exclude hidden and foreign owners");
    Check((await List(new() { Search = "Hidden" })).TotalCount == 0 && (await List(new() { OwnerUserId = hiddenOwner })).TotalCount == 0, "search and owner filter cannot broaden scope");
    Check((await List(new() { Search = "COUNTERPARTY A" })).TotalCount == first.TotalCount, "case-insensitive counterparty search");
    Check((await List(new() { Search = "Owned" })).Owners.Count == first.Owners.Count, "owner options computed before search filters");
    Check((await List()).Items.Any(x => x.Id == archived.Id), "ordinary list preserves archived records");
    foreach (var kind in Enum.GetValues<AgreementAttention>())
    {
        var actual = await List(new() { Attention = kind });
        var expected = AgreementDashboardQueries.Attention(new[] { own, read, edit, archived, expired, draft, terminated, day90, day91 }.AsQueryable(), kind, today).Select(x => x.Id).ToHashSet();
        Check(actual.Items.Select(x => x.Id).ToHashSet().SetEquals(expected), "Attention / Oslo default / archive / status: " + kind);
    }
    Check((await List(new() { Attention = AgreementAttention.Notice })).Items.Select(x => x.Id).ToHashSet().SetEquals([own.Id, read.Id, edit.Id, expired.Id, day90.Id]), "Notice independently preserves active-only and archive semantics");
    Check((await List(new() { Attention = AgreementAttention.Expired })).Items.Single().Id == expired.Id, "Expired is strictly before account-local today");
    await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Add(new() { AccountId = a, TimeZoneId = "UTC" }); await db.SaveChangesAsync(); }
    Check((await List(new() { Attention = AgreementAttention.Expired })).TotalCount == 0, "saved UTC timezone overrides Oslo default at midnight boundary");
    await using (var db = factory.CreateDbContext()) { db.AgreementReminderSettings.Remove(await db.AgreementReminderSettings.SingleAsync(x => x.AccountId == a)); await db.SaveChangesAsync(); }
    Check(!(await List(new() { Attention = AgreementAttention.Notice })).Items.Any(x => x.Id == day91.Id) &&
        (await List(new() { Attention = AgreementAttention.Notice })).Items.Any(x => x.Id == day90.Id), "inclusive 90-day boundary");
    Check((await List(new() { Type = AgreementType.Lease })).TotalCount == 0 && (await List(new() { Status = AgreementStatus.Draft })).Items.Single().Id == draft.Id, "type and status filters");
    await using (var db = factory.CreateDbContext()) Check(!await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == a) && !await db.AgreementDeadlines.AnyAsync(x => x.AccountId == a), "list and Attention never initialize settings or deadlines");
    actor.Current = new(true, admin, a);
    var page1 = await List(new() { Search = "Paging", Page = -1 }); var page2 = await List(new() { Search = "Paging", Page = 999 });
    Check(page1.TotalCount == 28 && page1.Page == 1 && page1.Items.Count == 25 && page2.Page == 2 && page2.Items.Count == 3, "admin, page size and clamping");
    var ordered = paging.OrderBy(x => x.Title).ThenBy(x => x.Id).Select(x => x.Id);
    Check(page1.Items.Concat(page2.Items).Select(x => x.Id).SequenceEqual(ordered), "stable title / ID sort and page boundaries");
    actor.Current = new(true, platform, a);
    Check((await List()).TotalCount == 0, "global PlatformAdmin flag does not grant account-wide read");
    actor.Current = new(true, platform, b); await Denied(() => service.ListAsync(b, new()), "PlatformAdmin without selected account membership");
    actor.Current = new(false, user, a); await Denied(() => List(), "unauthenticated");
    actor.Current = new(true, user, null); await Denied(() => List(), "no selected account");
    actor.Current = new(true, user, b); await Denied(() => List(), "forged account argument");
    actor.Current = new(true, foreign, a); await Denied(() => List(), "no membership in selected account");
    actor.Current = new(true, user, a);
    await using (var db = factory.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == user)).IsActive = false; await db.SaveChangesAsync(); }
    await Denied(() => List(), "inactive user");
    await using (var db = factory.CreateDbContext())
    {
        (await db.Users.SingleAsync(x => x.Id == user)).IsActive = true;
        db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == a && x.AgreementId == read.Id)); await db.SaveChangesAsync();
    }
    Check(!(await List()).Items.Any(x => x.Id == read.Id), "grant revoked at next call");
    actor.Current = new(true, admin, a);
    await using (var db = factory.CreateDbContext()) { db.UserAccountRoles.Remove(await db.UserAccountRoles.SingleAsync(x => x.UserAccountId == membershipIds[admin])); await db.SaveChangesAsync(); }
    Check((await List()).TotalCount == 0, "admin role revoked at next call");
    actor.Current = new(true, user, a);
    await using (var db = factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == membershipIds[user])); await db.SaveChangesAsync(); }
    await Denied(() => List(), "membership revoked at next call");
    // The Web facade must use the injected use case, not run its previous EF query.
    var probe = new ListProbe(); var context = new WebContext(user, a);
    var facade = new AgreementService(null!, probe, new AttentionProbe(), new DashboardProbe(), new InsightsProbe(), new DetailsProbe(), new FollowupProbe(), null!, null!, null!, null!, null!, null!, null!, null!);
    var filter = new AgreementFilter { Search = "forwarded" }; using var cancel = new CancellationTokenSource();
    var forwarded = await facade.ListAsync(a, filter, cancel.Token);
    Check(ReferenceEquals(forwarded, probe.Result) && probe.Account == a && ReferenceEquals(probe.Filter, filter) && probe.Token == cancel.Token, "Web facade delegates account, filter, token and result");
    var webActor = new CurrentActorAdapter(context).GetSnapshot();
    Check(webActor == new ActorSnapshot(true, user, a), "trusted Web adapter uses current authentication context");
    Check(factory.Created > 20, "short-lived context factory used per data operation");
    guard.Protect = true;
    try { await Task.WhenAll(new AgreementListService(new TenantPlatform.Application.Security.AuthorizationService(new Actor(new(true, other, a)), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), data, clock).ListAsync(a, new()), new AgreementListService(new TenantPlatform.Application.Security.AuthorizationService(new Actor(new(true, hiddenOwner, a)), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), data, clock).ListAsync(a, new())); }
    finally { guard.Protect = false; }
    Console.WriteLine("PASS " + provider + " concurrent independent operations, no SaveChanges during reads");
}

static async Task SnapshotChecks()
{
    var account = Guid.NewGuid(); var user = Guid.NewGuid();
    var actor = new Actor(new(true, user, account)); var gate = new GateData();
    var service = new AgreementListService(new TenantPlatform.Application.Security.AuthorizationService(actor, gate, TimeProvider.System), gate, TimeProvider.System);
    var filter = new AgreementFilter { Search = "original", Page = 2 };
    var pending = service.ListAsync(account, filter);
    actor.Current = new(false, Guid.NewGuid(), null); filter.Search = "changed"; filter.Page = 99;
    gate.Membership.SetResult(new(true, true, false, [])); await pending;
    Check(actor.Reads == 1 && gate.Scope?.UserId == user && gate.Query?.Search == "original" && gate.Query.Page == 2, "one immutable actor and filter snapshot across awaits, Application runs without HTTP/Blazor");
    var references = typeof(AgreementListService).Assembly.GetReferencedAssemblies().Select(x => x.Name ?? "");
    Check(!references.Any(x => x.Contains("EntityFramework") || x.Contains("AspNetCore") || x.Contains("Infrastructure") || x.Contains("Web")), "Application has no EF / ASP.NET / Infrastructure / Web assembly dependency");
}
static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); Console.WriteLine("PASS " + message); }
static async Task Denied(Func<Task> action, string message) { try { await action(); throw new Exception("Expected denial: " + message); } catch (UnauthorizedAccessException) { Console.WriteLine("PASS denial: " + message); } }
sealed class Actor(ActorSnapshot initial) : ICurrentActor { public ActorSnapshot Current = initial; public int Reads; public ActorSnapshot GetSnapshot() { Reads++; return Current; } }
sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext> { public int Created; public TenantPlatformDbContext CreateDbContext() { Interlocked.Increment(ref Created); return new(options); } }
sealed class AuditActor : IAuditUserContext { public Guid? UserId => null; public Guid? AccountId => null; public string? Email => null; }
sealed class ReadOnlyGuard : SaveChangesInterceptor
{
    public bool Protect;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    { if (Protect) throw new Exception("Read attempted SaveChanges"); return ValueTask.FromResult(result); }
}
sealed class WebContext(Guid user, Guid account) : ICurrentUserContextService { public CurrentUserContext Current => new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account, IsPlatformAdmin = true }; }
sealed class ListProbe : IAgreementListService
{
    public Guid Account; public AgreementFilter? Filter; public CancellationToken Token;
    public AgreementPageDto Result = new([], 123, 1, 25, []);
    public Task<AgreementPageDto> ListAsync(Guid accountId, AgreementFilter filter, CancellationToken ct) { Account = accountId; Filter = filter; Token = ct; return Task.FromResult(Result); }
}
sealed class GateData : IAgreementListData, IAuthorizationData, IAgreementAttentionData, IAgreementDashboardData
{
    public TaskCompletionSource<AuthorizationSubject> Membership = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AgreementListScope? Scope; public AgreementListQuery? Query;
    public Task<AgreementDashboardList> QueryAsync(AgreementListScope scope, AgreementPriorityQuery query, CancellationToken ct) { Scope = scope; return Task.FromResult(new AgreementDashboardList(query.Today, [])); }
    public Task<AgreementAttentionDto> CountAsync(AgreementListScope scope, AgreementAttentionQuery query, CancellationToken ct) { Scope = scope; return Task.FromResult(new AgreementAttentionDto(query.Today, 0, 0, 0)); }
    public Task<AuthorizationSubject> GetSubjectAsync(Guid u, Guid? a, CancellationToken ct) => Membership.Task;
    public Task<ResourceAccessFacts?> GetResourceAsync(Guid a, Guid u, ResourceKind k, Guid id, DateOnly t, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> AccountExistsAsync(Guid a, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasAgreementRelationshipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasLeasingOwnershipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
    public Task<string> GetReminderTimeZoneAsync(Guid a, CancellationToken ct) => Task.FromResult("Europe/Oslo");
    public Task<AgreementPageDto> QueryAsync(AgreementListScope scope, AgreementListQuery query, AgreementAttentionCriteria? attention, CancellationToken ct) { Scope = scope; Query = query; return Task.FromResult(new AgreementPageDto([], 0, 1, 25, [])); }
}
