using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Application.AccountSettings;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Security;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Agreements;
using TenantPlatform.Infrastructure.Authorization;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Services.Agreements;

static class InsightsChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL insights: " + name); Console.WriteLine($"PASS {provider} insights: {name}"); }
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var user = Guid.NewGuid(); var other = Guid.NewGuid();
        var admin = Guid.NewGuid(); var platform = Guid.NewGuid(); var foreignOwner = Guid.NewGuid(); var member = Guid.NewGuid();
        var party = Guid.NewGuid(); var secondParty = Guid.NewGuid(); var foreignParty = Guid.NewGuid();
        var geo = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        var org = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        Agreement Make(string name, Guid owner, int? node, AgreementType type = AgreementType.Lease, AgreementStatus status = AgreementStatus.Active, Guid? counterparty = null, bool archived = false, bool foreign = false) => new()
        {
            Id = Guid.NewGuid(), AccountId = foreign ? b : a, OwnerUserId = owner, CounterpartyOrganizationId = counterparty ?? party,
            Title = name, Type = type, Status = status, IsArchived = archived, StartDate = new(2020, 1, 1),
            GeographicAreaId = node.HasValue ? geo[node.Value] : null, OrganizationElementId = node.HasValue ? org[node.Value] : null,
            CreatedUtc = DateTimeOffset.UtcNow, UpdatedUtc = DateTimeOffset.UtcNow, CreatedByUserId = user, UpdatedByUserId = user, Revision = Guid.NewGuid()
        };
        var own = Make("Root", user, 0); var read = Make("Read child", other, 1, AgreementType.SoftwareLicense, AgreementStatus.Draft);
        var edit = Make("Edit leaf", other, 2, status: AgreementStatus.Terminated);
        var ownLeaf = Make("Own leaf", user, 2, counterparty: secondParty);
        var unassigned = Make("Unassigned", user, null, AgreementType.Other, AgreementStatus.Expired, foreignParty);
        var nonmemberOwner = Make("Nonmember owner", foreignOwner, 1, AgreementType.Other, counterparty: secondParty);
        var hidden = Make("Hidden", other, 3); var hidden2 = Make("Hidden two", other, 3);
        var archived = Make("Archived", user, 3, archived: true);
        var foreignAgreement = Make("Foreign", foreignOwner, 5, counterparty: foreignParty, foreign: true);
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = a, Name = "Insight A" }, new Account { Id = b, Name = "Insight B" });
            foreach (var id in new[] { user, other, admin, platform, foreignOwner })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = id == foreignOwner ? "Foreign secret" : "Name", LastName = id.ToString(), IsActive = true, IsPlatformAdmin = id == platform });
                var membership = new UserAccount { Id = id == user ? member : Guid.NewGuid(), AccountId = id == foreignOwner ? b : a, UserId = id };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.Organizations.AddRange(new Organization { Id = party, AccountId = a, Name = "Same name" }, new Organization { Id = secondParty, AccountId = a, Name = "Same name" }, new Organization { Id = foreignParty, AccountId = b, Name = "Foreign party secret" });
            for (var i = 0; i < 6; i++)
            {
                var name = new[] { "Root", "Child", "Leaf", "Hidden", "Unused", "Foreign metadata" }[i];
                db.GeographicAreas.Add(new() { Id = geo[i], AccountId = i == 5 ? b : a, ParentId = i is 1 or 2 ? geo[i - 1] : null, Name = name });
                db.OrganizationElements.Add(new() { Id = org[i], AccountId = i == 5 ? b : a, ParentId = i is 1 or 2 ? org[i - 1] : null, Name = name });
            }
            db.Agreements.AddRange(own, read, edit, ownLeaf, unassigned, nonmemberOwner, hidden, hidden2, archived, foreignAgreement);
            foreach (var (agreement, level) in new[] { (read, AgreementAccessLevel.Read), (edit, AgreementAccessLevel.Edit), (nonmemberOwner, AgreementAccessLevel.Read) })
                db.AgreementAccess.Add(new() { Id = Guid.NewGuid(), AccountId = a, AgreementId = agreement.Id, UserId = user, Level = level });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, user, a));
        var auth = new AuthorizationService(actor, new AuthorizationData(factory), TimeProvider.System);
        var data = new AgreementInsightsData(factory);
        var service = new AgreementInsightsService(auth, data);
        async Task<AgreementInsightsDto> Read(AgreementInsightDimension dimension, Guid? selected = null)
        {
            guard.Protect = true;
            try { return await service.GetInsightsAsync(a, dimension, selected); }
            finally { guard.Protect = false; }
        }
        async Task Denied(Func<Task> action, string name)
        {
            try { await action(); throw new Exception("Expected denial: " + name); }
            catch (UnauthorizedAccessException) { Check(true, name); }
        }
        foreach (var dimension in Enum.GetValues<AgreementInsightDimension>()) Check((await Read(dimension)).Total == 6, "authorized count includes all statuses, excludes archive/hidden/foreign: " + dimension);
        var types = await Read(AgreementInsightDimension.Type);
        Check(types.Groups.Select(x => x.Type).SequenceEqual(new AgreementType?[] { AgreementType.Lease, AgreementType.SoftwareLicense, AgreementType.Other }) && types.Groups.Select(x => x.Count).SequenceEqual(new[] { 3, 1, 2 }), "type SQL groups ordered by enum, counts agreements rather than money");
        var statuses = await Read(AgreementInsightDimension.Status);
        Check(statuses.Groups.Select(x => x.Status).SequenceEqual(Enum.GetValues<AgreementStatus>().Cast<AgreementStatus?>()) && statuses.Groups.Select(x => x.Count).SequenceEqual(new[] { 1, 3, 1, 1 }), "status SQL grouping preserves Draft/Active/Terminated/Expired");
        var owners = await Read(AgreementInsightDimension.Owner);
        Check(owners.Groups.Select(x => x.Count).SequenceEqual(new[] { 3, 2, 1 }) && owners.Groups.Last().Key == "missing" && !owners.Groups.Any(x => x.Name?.Contains("Foreign secret") == true), "owner left join restricted to account members; nonmember maps to missing without foreign label");
        var parties = await Read(AgreementInsightDimension.Counterparty);
        Check(parties.Groups.Select(x => x.Count).SequenceEqual(new[] { 3, 2, 1 }) && parties.Groups.Last().Key == "missing" && !parties.Groups.Any(x => x.Name?.Contains("Foreign party") == true), "counterparty left join is account-restricted even for existing foreign reference");
        await using (var db = factory.CreateDbContext()) { (await db.Agreements.SingleAsync(x => x.Id == own.Id)).CounterpartyOrganizationId = foreignParty; await db.SaveChangesAsync(); }
        var tiedParties = await Read(AgreementInsightDimension.Counterparty);
        Check(tiedParties.Groups.Where(x => x.Name == "Same name").Select(x => x.Key).SequenceEqual(new[] { party, secondParty }.Order().Select(x => x.ToString())), "equal count/name groups have stable ID ordering");
        await using (var db = factory.CreateDbContext()) { (await db.Agreements.SingleAsync(x => x.Id == own.Id)).CounterpartyOrganizationId = party; await db.SaveChangesAsync(); }
        guard.Protect = true;
        try
        {
            var aggregates = await data.GetHierarchyAsync(await auth.GetAgreementListScopeAsync(a), AgreementInsightDimension.Geography, CancellationToken.None);
            Check(aggregates.Counts.Count == 4 && aggregates.Counts.Sum(x => x.Count) == 6, "hierarchy port returns authorized counts per node rather than one assignment per agreement");
        }
        finally { guard.Protect = false; }
        foreach (var dimension in new[] { AgreementInsightDimension.Geography, AgreementInsightDimension.Organization })
        {
            var ids = dimension == AgreementInsightDimension.Geography ? geo : org;
            var root = await Read(dimension);
            Check(root.Options.Select(x => x.Id).SequenceEqual(ids.Take(3)) && root.Groups.Single(x => x.Key == ids[0].ToString()).Count == 5 && root.Groups.Single(x => x.Key == "missing").Count == 1, "metadata limited to assigned nodes and ancestors; root count no duplication: " + dimension);
            var atRoot = await Read(dimension, ids[0]);
            Check(atRoot.Total == 5 && atRoot.Groups.Single(x => x.Direct).Count == 1 && atRoot.Groups.Single(x => !x.Direct).Count == 4, "selected root separates direct assignments and nearest child: " + dimension);
            var atChild = await Read(dimension, ids[1]); var atLeaf = await Read(dimension, ids[2]);
            Check(atChild.Total == 4 && atChild.Groups.All(x => x.Count == 2) && atLeaf.Groups.Single() is { Count: 2, Direct: true }, "child and leaf subtree filtering preserves counts: " + dimension);
            await Denied(() => Read(dimension, ids[3]), "unrelated metadata node denied: " + dimension);
            await Denied(() => Read(dimension, ids[5]), "foreign node denied: " + dimension);
            await Denied(() => Read(dimension, Guid.NewGuid()), "unknown node denied: " + dimension);
            var wrongDimension = dimension == AgreementInsightDimension.Geography ? org[0] : geo[0];
            await Denied(() => Read(dimension, wrongDimension), "node from wrong dimension denied: " + dimension);
        }
        foreach (var d in new[] { AgreementInsightDimension.Type, AgreementInsightDimension.Status, AgreementInsightDimension.Owner, AgreementInsightDimension.Counterparty })
        {
            try { await Read(d, geo[0]); throw new Exception("Expected selected-node rejection"); } catch (ArgumentException) { Check(true, "flat dimension rejects selected node instead of silently ignoring it: " + d); }
        }
        try { await Read((AgreementInsightDimension)999); throw new Exception("Expected invalid enum rejection"); } catch (ArgumentOutOfRangeException) { Check(true, "unknown dimension denied"); }
        // Removing a counterparty is blocked in PostgreSQL; corrupt/legacy joins are additionally exercised in InMemory.
        if (provider == "InMemory")
        {
            await using (var db = factory.CreateDbContext()) { var row = await db.Agreements.SingleAsync(x => x.Id == own.Id); row.CounterpartyOrganizationId = Guid.NewGuid(); row.Type = (AgreementType)999; row.Status = (AgreementStatus)999; (await db.Agreements.SingleAsync(x => x.Id == edit.Id)).OwnerUserId = Guid.NewGuid(); await db.SaveChangesAsync(); }
            Check((await Read(AgreementInsightDimension.Owner)).Groups.Single(x => x.Key == "missing").Count == 2 && (await Read(AgreementInsightDimension.Counterparty)).Groups.Single(x => x.Key == "missing").Count == 2 && (await Read(AgreementInsightDimension.Type)).Groups.Single(x => x.Key == "999").Type is null && (await Read(AgreementInsightDimension.Status)).Groups.Single(x => x.Key == "status-999").Status is null, "legacy missing reference and unknown enum labels fail closed");
            await using (var db = factory.CreateDbContext()) { var row = await db.Agreements.SingleAsync(x => x.Id == own.Id); row.CounterpartyOrganizationId = party; row.Type = AgreementType.Lease; row.Status = AgreementStatus.Active; (await db.Agreements.SingleAsync(x => x.Id == edit.Id)).OwnerUserId = other; await db.SaveChangesAsync(); }
        }
        else
        {
            await using var db = factory.CreateDbContext();
            (await db.Agreements.SingleAsync(x => x.Id == own.Id)).CounterpartyOrganizationId = Guid.NewGuid();
            try { await db.SaveChangesAsync(); throw new Exception("Expected FK rejection"); } catch (DbUpdateException) { Check(true, "PostgreSQL rejects missing counterparty; no safety constraints disabled"); }
        }
        actor.Current = new(true, admin, a);
        foreach (var dimension in Enum.GetValues<AgreementInsightDimension>()) Check((await Read(dimension)).Total == 8, "admin sees account data only: " + dimension);
        Check((await Read(AgreementInsightDimension.Geography, geo[4])).Total == 0 && (await Read(AgreementInsightDimension.Geography)).Options.Count == 5, "admin retains full account hierarchy, including empty selectable branch");
        actor.Current = new(true, platform, a);
        foreach (var dimension in Enum.GetValues<AgreementInsightDimension>()) Check((await Read(dimension)).Total == 0 && (await Read(dimension)).Options.Count == 0, "PlatformAdmin has no automatic data or metadata access: " + dimension);
        actor.Current = new(true, platform, b); await Denied(() => service.GetInsightsAsync(b, AgreementInsightDimension.Type), "platform without membership denied");
        actor.Current = new(false, user, a); await Denied(() => Read(AgreementInsightDimension.Type), "missing authentication");
        actor.Current = new(true, user, null); await Denied(() => Read(AgreementInsightDimension.Type), "missing selected account");
        actor.Current = new(true, user, b); await Denied(() => Read(AgreementInsightDimension.Type), "wrong account argument");
        actor.Current = new(true, Guid.NewGuid(), a); await Denied(() => Read(AgreementInsightDimension.Type), "missing user/membership");
        actor.Current = new(true, user, a);
        await using (var db = factory.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == user)).IsActive = false; await db.SaveChangesAsync(); }
        await Denied(() => Read(AgreementInsightDimension.Type), "inactive user");
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == user)).IsActive = true;
            db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == a && x.AgreementId == read.Id && x.UserId == user)); await db.SaveChangesAsync();
        }
        Check((await Read(AgreementInsightDimension.Type)).Total == 5 && (await Read(AgreementInsightDimension.Geography, geo[1])).Total == 3, "grant revocation affects flat and hierarchical aggregates at next call in same instance");
        await using (var db = factory.CreateDbContext()) { db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == member)); await db.SaveChangesAsync(); }
        await Denied(() => Read(AgreementInsightDimension.Type), "membership revoked at next call");
        var context = new WebContext(user, a); var probe = new InsightsProbe();
        var facade = new AgreementService(null!, new ListProbe(), new AttentionProbe(), new DashboardProbe(), probe, new DetailsProbe(), new FollowupProbe(), null!, null!, null!, null!, null!, null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        Check(ReferenceEquals(await facade.GetInsightsAsync(a, AgreementInsightDimension.Geography, geo[0], cancellation.Token), probe.Result) && probe.Account == a && probe.Dimension == AgreementInsightDimension.Geography && probe.Selected == geo[0] && probe.Token == cancellation.Token, "Web facade forwards account, dimension, selection, cancellation and exact result");
        await using (var db = factory.CreateDbContext()) Check(!await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == a) && !await db.AgreementDeadlines.AnyAsync(x => x.AccountId == a), "read-only port never initializes settings/deadlines; SaveChanges guarded on all reads");
    }

    public static async Task PureAndSnapshot()
    {
        var root = Guid.NewGuid(); var orphan = Guid.NewGuid(); var cycleA = Guid.NewGuid(); var cycleB = Guid.NewGuid(); var self = Guid.NewGuid();
        var rows = AccountRegisterHierarchy.Build(new[] { (root, (Guid?)null, "Root"), (orphan, (Guid?)Guid.NewGuid(), "Orphan"), (cycleA, (Guid?)cycleB, "Cycle A"), (cycleB, (Guid?)cycleA, "Cycle B"), (self, (Guid?)self, "Self") });
        if (rows.Count != 1 || rows[0].Id != root) throw new Exception("Hierarchy legacy orphan/cycle semantics changed");
        var equalNames = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var tiedRows = AccountRegisterHierarchy.Build(equalNames.Select(id => (id, (Guid?)null, "Same")));
        if (!tiedRows.Select(x => x.Id).SequenceEqual(equalNames.Order())) throw new Exception("Hierarchy name tie ordering failed");
        var grouped = AgreementInsightRules.GroupHierarchy(new[] { new AgreementHierarchyCount(root, 2), new AgreementHierarchyCount(orphan, 3) }, rows, null);
        if (grouped.Sum(x => x.Count) != 5 || grouped.Single(x => x.Key == "missing").Count != 3) throw new Exception("Weighted hierarchy grouping failed");
        var account = Guid.NewGuid(); var user = Guid.NewGuid(); var actor = new Actor(new(true, user, account)); var gate = new InsightsGate();
        var service = new AgreementInsightsService(new AuthorizationService(actor, gate, TimeProvider.System), gate);
        var dimension = AgreementInsightDimension.Type; Guid? selected = null; using var cancellation = new CancellationTokenSource();
        var pending = service.GetInsightsAsync(account, dimension, selected, cancellation.Token);
        dimension = AgreementInsightDimension.Geography; selected = root; actor.Current = new(false, Guid.NewGuid(), null);
        gate.Membership.SetResult(new(true, true, false, [])); await pending;
        if (actor.Reads != 1 || gate.Scope?.UserId != user || gate.Dimension != AgreementInsightDimension.Type || gate.Token != cancellation.Token) throw new Exception("Insights snapshot/token failed");
        Console.WriteLine("PASS insights: pure hierarchy handles orphan/cycles, weighted grouping; immutable actor/filter snapshot and cancellation without HTTP/Blazor");
    }
}
sealed class InsightsProbe : IAgreementInsightsService
{
    public Guid Account; public AgreementInsightDimension Dimension; public Guid? Selected; public CancellationToken Token;
    public AgreementInsightsDto Result = new([], []);
    public Task<AgreementInsightsDto> GetInsightsAsync(Guid accountId, AgreementInsightDimension dimension, Guid? selected = null, CancellationToken ct = default) { Account = accountId; Dimension = dimension; Selected = selected; Token = ct; return Task.FromResult(Result); }
}
sealed class InsightsGate : IAuthorizationData, IAgreementInsightsData
{
    public TaskCompletionSource<AuthorizationSubject> Membership = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AgreementListScope? Scope; public AgreementInsightDimension Dimension; public CancellationToken Token;
    public Task<AuthorizationSubject> GetSubjectAsync(Guid u, Guid? a, CancellationToken ct) => Membership.Task;
    public Task<IReadOnlyList<AgreementInsightGroup>> GetGroupsAsync(AgreementListScope scope, AgreementInsightDimension dimension, CancellationToken ct) { Scope = scope; Dimension = dimension; Token = ct; return Task.FromResult<IReadOnlyList<AgreementInsightGroup>>([]); }
    public Task<AgreementHierarchyData> GetHierarchyAsync(AgreementListScope scope, AgreementInsightDimension dimension, CancellationToken ct) => throw new NotSupportedException();
    public Task<ResourceAccessFacts?> GetResourceAsync(Guid a, Guid u, ResourceKind k, Guid id, DateOnly t, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> AccountExistsAsync(Guid a, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasAgreementRelationshipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasLeasingOwnershipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
}
