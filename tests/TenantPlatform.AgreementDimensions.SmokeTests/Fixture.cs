using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

sealed class Fixture
{
    public Guid Account { get; } = Guid.NewGuid();
    public Guid ForeignAccount { get; } = Guid.NewGuid();
    public Guid Admin { get; } = Guid.NewGuid();
    public Guid Owner { get; } = Guid.NewGuid();
    public Guid Reader { get; } = Guid.NewGuid();
    public Guid Editor { get; } = Guid.NewGuid();
    public Guid Stranger { get; } = Guid.NewGuid();
    public Guid Party { get; } = Guid.NewGuid();
    public Guid Single { get; } = Guid.NewGuid();
    public Guid Multi { get; } = Guid.NewGuid();
    public Guid Foreign { get; } = Guid.NewGuid();
    public Guid Root { get; } = Guid.NewGuid();
    public Guid A { get; } = Guid.NewGuid();
    public Guid B { get; } = Guid.NewGuid();
    public Guid MRoot { get; } = Guid.NewGuid();
    public Guid M1 { get; } = Guid.NewGuid();
    public Guid M2 { get; } = Guid.NewGuid();
    public Guid ForeignValue { get; } = Guid.NewGuid();
    private readonly InMemoryDatabaseRoot root = new();
    private readonly string database = Guid.NewGuid().ToString();
    public static DateTimeOffset Now => new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    public TenantPlatformDbContext Open(Guid? user = null)
    {
        var options = new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(database, root)
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditUser(user ?? Admin, Account))).Options;
        return new(options);
    }
    public AgreementService Service(Guid? user = null, Guid? selectedAccount = null, bool platformAdmin = false)
    {
        var context = new Context(user ?? Admin, selectedAccount ?? Account, platformAdmin);
        var factory = new Factory(this, context.Current.UserId);
        return new(factory, context, new TenantAuthorizationService(factory, context), null!, NullLogger<AgreementService>.Instance, new Clock());
    }
    public async Task Seed()
    {
        await using var db = Open();
        db.Accounts.AddRange(new Account { Id = Account, Name = "A", DefaultLanguage = "nb-NO" }, new Account { Id = ForeignAccount, Name = "B" });
        foreach (var user in new[] { Admin, Owner, Reader, Editor, Stranger })
        {
            db.Users.Add(new User { Id = user, Email = $"{user}@example.test", FirstName = "User", LastName = user.ToString(), IsActive = true });
            var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = Account, UserId = user };
            db.UserAccounts.Add(membership);
            if (user == Admin) db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
        }
        db.Organizations.Add(new Organization { Id = Party, AccountId = Account, Name = "Business counterparty" });
        foreach (var (id, account, code, multiple) in new[] { (Single, Account, "SINGLE", false), (Multi, Account, "MULTI", true), (Foreign, ForeignAccount, "FOREIGN", false) })
        {
            var dimension = new Dimension { Id = id, AccountId = account, Name = code + " fallback", Code = code, AllowMultiple = multiple, LeafOnly = !multiple, Revision = Guid.NewGuid() };
            dimension.Translations = new[] { ("nb-NO", "Norsk " + code), ("en-GB", "English " + code), ("sv-SE", "Svensk " + code) }
                .Select(x => new DimensionTranslation { Id = Guid.NewGuid(), AccountId = account, DimensionId = id, LanguageCode = x.Item1, Name = x.Item2 }).ToList();
            db.Dimensions.Add(dimension);
        }
        foreach (var (id, dim, account, parent, name) in new[] { (Root, Single, Account, (Guid?)null, "Root"), (A, Single, Account, (Guid?)Root, "Leaf A"), (B, Single, Account, (Guid?)Root, "Leaf B"),
            (MRoot, Multi, Account, (Guid?)null, "Multi root"), (M1, Multi, Account, (Guid?)MRoot, "Multi 1"), (M2, Multi, Account, (Guid?)MRoot, "Multi 2"), (ForeignValue, Foreign, ForeignAccount, (Guid?)null, "Foreign value") })
            db.DimensionValues.Add(new DimensionValue { Id = id, AccountId = account, DimensionId = dim, ParentId = parent, Name = name, Code = id.ToString("N"), Revision = Guid.NewGuid() });
        db.LeasingDimensionRules.Add(new LeasingDimensionRule { AccountId = Account, DimensionId = Single, IsEnabled = true, IsRequired = true, Revision = Guid.NewGuid() });
        await db.SaveChangesAsync();
    }
    public SaveAgreementRequest Request() => new()
    {
        Title = "Agreement", Description = "Preserved description", Type = AgreementType.Other, Status = AgreementStatus.Active,
        Direction = AgreementDirection.Cost, Currency = "NOK", CounterpartyOrganizationId = Party, OwnerUserId = Owner,
        StartDate = new(2026, 1, 1), Form = AgreementForm.Ongoing, NoticeCount = 3, NoticeUnit = AgreementNoticeUnit.Months,
        Terms = "Preserved terms"
    };
    public static AgreementDimensionPatch Patch(Guid dimension, params Guid[] values) => new() { DimensionId = dimension, ValueIds = [..values] };
    public async Task<Guid> Create(List<AgreementDimensionPatch>? changes = null, Guid? user = null)
    {
        var request = Request(); request.DimensionChanges = changes;
        return await CreateRequest(request, user);
    }
    public async Task<Guid> CreateRequest(SaveAgreementRequest request, Guid? user = null)
    {
        await using var db = Open(user);
        return await (Task<Guid>)Invoke(Service(user), "CreateAgreementCoreAsync", db, Account, request, CancellationToken.None);
    }
    public async Task Update(Guid id, List<AgreementDimensionPatch>? changes = null, Guid? user = null, Action<SaveAgreementRequest>? edit = null)
    {
        var request = await Service(user).GetAsync(Account, id);
        request.DimensionChanges = changes; edit?.Invoke(request);
        await UpdateRequest(id, request, user);
    }
    public async Task UpdateRequest(Guid id, SaveAgreementRequest request, Guid? user = null)
    {
        await using var db = Open(user);
        await (Task)Invoke(Service(user), "UpdateAgreementCoreAsync", db, Account, id, request, CancellationToken.None);
    }
    public async Task Rule(Guid dimension, bool enabled = true, bool required = false, Guid? user = null)
    {
        await using var db = Open(user);
        var current = await db.AgreementDimensionRules.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == Account && x.DimensionId == dimension);
        await RuleInput(new() { AccountId = Account, DimensionId = dimension, IsEnabled = enabled, IsRequired = required, Revision = current?.Revision ?? Guid.Empty }, user);
    }
    public async Task RuleInput(AgreementDimensionRule input, Guid? user = null)
    {
        await using var db = Open(user);
        await (Task)Invoke(Service(user), "SaveAgreementDimensionRuleCoreAsync", db, Account, input, CancellationToken.None);
    }
    public static object Invoke(AgreementService service, string method, params object[] args)
    {
        try { return typeof(AgreementService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    public async Task<List<AgreementDimensionAssignment>> Assignments(Guid id)
    {
        await using var db = Open();
        return await db.AgreementDimensionAssignments.AsNoTracking().Where(x => x.AccountId == Account && x.AgreementId == id).OrderBy(x => x.Id).ToListAsync();
    }
    public async Task Mutate(Func<TenantPlatformDbContext, Task> action)
    { await using var db = Open(); await action(db); await db.SaveChangesAsync(); }
    public async Task Grants(Guid agreement)
    {
        await Mutate(db => { db.AgreementAccess.AddRange(new AgreementAccess { Id = Guid.NewGuid(), AccountId = Account, AgreementId = agreement, UserId = Reader, Level = AgreementAccessLevel.Read },
            new AgreementAccess { Id = Guid.NewGuid(), AccountId = Account, AgreementId = agreement, UserId = Editor, Level = AgreementAccessLevel.Edit }); return Task.CompletedTask; });
    }
    public static string Json(object item) => JsonSerializer.Serialize(item);
    private sealed class Factory(Fixture fixture, Guid user) : IDbContextFactory<TenantPlatformDbContext>
    { public TenantPlatformDbContext CreateDbContext() => fixture.Open(user); }
    private sealed class Context(Guid user, Guid account, bool platformAdmin) : ICurrentUserContextService
    { public CurrentUserContext Current { get; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account, IsPlatformAdmin = platformAdmin }; }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class AuditUser(Guid user, Guid account) : IAuditUserContext
    { public Guid? UserId => user; public Guid? AccountId => account; public string? Email => "test@example.test"; }
}
