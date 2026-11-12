using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Infrastructure.Initialization;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Services.Agreements;

static class HierarchyChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var f = new InsightFixture(); await f.Seed();
        var service = f.Service(); var grouping = new AgreementInsightGrouping(DimensionId: f.Dimension);
        var top = await service.GetInsightsAsync(f.Account, grouping);
        check(top.TotalUniqueAgreements == 4 && top.ContextUniqueAgreements == 4 && top.GroupSum == 5 && top.Groups.Single(g => g.NodeId == f.Root).Count == 3 && top.Groups.Single(g => g.NodeId == f.Other).Count == 1, "multiple roots: four unique visible agreements, five displayed occurrences, common ancestors deduplicated");
        check(top.Groups.Single(g => g.Kind == AgreementInsightGroupKind.Unspecified).Count == 1 && top.Options.All(n => n.Id != f.Hidden) && top.Options.Any(n => n.Id == f.Inactive && !n.IsActive), "unassigned only means no explicit row; historical inactive assignment and hidden-branch pruning preserved");
        var root = await service.GetInsightsAsync(f.Account, grouping, f.Root);
        check(root.ContextUniqueAgreements == 3 && root.GroupSum == 4 && root.Groups.All(g => g.Kind != AgreementInsightGroupKind.Unspecified) && root.Groups.Single(g => g.NodeId == f.Parent).Count == 3, "root drill groups immediate children only and deduplicates sibling overlap");
        var parent = await service.GetInsightsAsync(f.Account, grouping, f.Parent);
        check(parent.ContextUniqueAgreements == 3 && parent.GroupSum == 5 && parent.Groups.Single(g => g.Kind == AgreementInsightGroupKind.Direct) is { Count: 2, NodeId: null } && parent.Groups.Single(g => g.NodeId == f.Branch).Count == 2, "direct parent plus descendant overlaps with child group but parent unique total is three");
        var branch = await service.GetInsightsAsync(f.Account, grouping, f.Branch);
        var leaf = await service.GetInsightsAsync(f.Account, grouping, f.Leaf);
        check(branch.Groups.Single().NodeId == f.Leaf && leaf.Options.Single(n => n.Id == f.Leaf).Depth == 3 && leaf.Groups.Single() is { Kind: AgreementInsightGroupKind.Direct, Count: 2, NodeId: null }, "four hierarchy levels, immediate-child drill and controlled leaf direct group");
        await using (var db = f.Factory.CreateDbContext())
        {
            foreach (var excluded in await db.Dimensions.Where(x => x.AccountId == f.Account && x.Code.StartsWith("EX")).Select(x => x.Id).ToListAsync())
                await Denied(() => service.GetInsightsAsync(f.Account, new(DimensionId: excluded)), check, "inactive, disabled or unconfigured dimension cannot be requested directly");
            var definition = await db.Dimensions.SingleAsync(x => x.Id == f.Dimension);
            definition.AllowMultiple = false; definition.LeafOnly = true; await db.SaveChangesAsync();
        }
        var historicalSelections = await service.GetInsightsAsync(f.Account, grouping, f.Parent);
        check(!historicalSelections.AllowMultiple && historicalSelections.ContextUniqueAgreements == 3 && historicalSelections.GroupSum == 5, "global single/leaf-only change never removes historical parent or multiple assignments from counts");
        await Denied(() => service.GetInsightsAsync(f.Account, grouping, f.Hidden), check, "hidden node rejected without names");
        await Denied(() => service.GetInsightsAsync(f.Account, grouping, f.ForeignNode), check, "foreign node rejected without names");
        await Denied(() => service.GetInsightsAsync(f.Account, new(DimensionId: f.ForeignDimension)), check, "foreign dimension rejected");
        await Denied(() => service.GetInsightDimensionsAsync(f.ForeignAccount), check, "selected-account mismatch rejected at catalog boundary");
        foreach (var (language, name) in new[] { ("nb-NO", "Norsk dimensjon"), ("en-GB", "English dimension"), ("sv-SE", "Svensk dimension") })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var options = await service.GetInsightDimensionsAsync(f.Account);
            check(options is [{ Id: var id, Name: var label }] && id == f.Dimension && label == name, "ordinary member catalog includes only active/enabled dimensions with stable localized ID: " + language);
        }
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        var unshared = f.Service(f.Stranger);
        var noAccess = await unshared.GetInsightsAsync(f.Account, grouping);
        check((await unshared.GetInsightDimensionsAsync(f.Account)).Count == 1 && noAccess.TotalUniqueAgreements == 0 && noAccess.Options.Count == 0, "available definition is distinct from authorized nodes and agreements for an unshared member");
        var reader = f.Service(f.Reader);
        check((await reader.GetInsightDimensionsAsync(f.Account)).Count == 1 && (await reader.GetInsightsAsync(f.Account, grouping)).TotalUniqueAgreements == 1, "read-only shared user loads catalog and counts without edit/register permission");
        var platformContext = new Context(f.Reader, f.Account, true);
        var platformReader = new AgreementService(f.Factory, platformContext, new TenantPlatform.Web.Security.Authorization.TenantAuthorizationService(f.Factory, platformContext), null!, NullLogger<AgreementService>.Instance);
        check((await platformReader.GetInsightsAsync(f.Account, grouping)).TotalUniqueAgreements == 1, "platform administrator flag does not bypass account agreement access");
        await Denied(() => reader.GetOptionsAsync(f.Account, f.Shared), check, "read-only dashboard does not imply agreement edit permission");
        await using (var db = f.Factory.CreateDbContext())
        {
            var member = await db.UserAccounts.SingleAsync(x => x.UserId == f.User);
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = member.Id, Role = UserRole.AccountAdmin });
            await db.SaveChangesAsync();
        }
        var admin = await service.GetInsightsAsync(f.Account, grouping, f.Hidden);
        check(admin.TotalUniqueAgreements == 5 && admin.ContextUniqueAgreements == 1 && admin.Options.Any(x => x.Id == f.Hidden), "account admin sees hidden account branch, excluding archived and foreign agreements");
        foreach (var invalid in new[] { new AgreementInsightGrouping(), new(AgreementInsightDimension.Type, f.Dimension), new((AgreementInsightDimension)99), new(DimensionId: Guid.Empty) })
        {
            try { await service.GetInsightsAsync(f.Account, invalid); throw new Exception("Invalid grouping accepted"); }
            catch (ArgumentException) { check(true, "grouping discriminator is unambiguous and validated"); }
        }
        await using (var db = f.Factory.CreateDbContext())
        { (await db.Users.SingleAsync(x => x.Id == f.User)).IsActive = false; await db.SaveChangesAsync(); }
        await Denied(() => service.GetInsightDimensionsAsync(f.Account), check, "cached context cannot bypass inactive membership check");
        await Denied(() => service.GetInsightsAsync(f.Account, grouping), check, "inactive user also rejected at count boundary");
        PureChecks(f, check);
        Demo(check);
    }
    static void PureChecks(InsightFixture f, Action<bool, string> check)
    {
        var dimension = new Dimension { Id = f.Dimension, AccountId = f.Account, AllowMultiple = true, LeafOnly = false };
        var values = new[] { new DimensionValue { Id = f.Root, DimensionId = f.Dimension, AccountId = f.Account, Name = "Root" }, new DimensionValue { Id = f.Leaf, ParentId = f.Root, DimensionId = f.Dimension, AccountId = f.Account, Name = "Leaf" } };
        var assigned = new AgreementInsightAssignment(f.User, f.Leaf);
        var duplicate = AgreementInsightHierarchy.Aggregate(dimension, values, [f.User, f.User], [assigned, assigned], null, false);
        check(duplicate.TotalUniqueAgreements == 1 && duplicate.GroupSum == 1 && duplicate.Groups.Single().Count == 1, "pure production aggregator deduplicates duplicate join/assignment rows by agreement identity");
        var parentOnly = AgreementInsightHierarchy.Aggregate(dimension, values, [f.User], [new(f.User, f.Root)], f.Root, true);
        check(parentOnly.ContextUniqueAgreements == 1 && parentOnly.Groups.Single(x => x.Kind == AgreementInsightGroupKind.Direct).Count == 1 && parentOnly.Groups.Single(x => x.NodeId == f.Leaf).Count == 0, "parent-only assignment remains direct and never contributes to a child");
        values[1].IsActive = false;
        var inactiveOnly = AgreementInsightHierarchy.Aggregate(dimension, values, [f.User], [assigned], null, false);
        check(inactiveOnly.Groups.Single().Kind == AgreementInsightGroupKind.Node && inactiveOnly.Groups.Single().Count == 1 && inactiveOnly.Options.Any(x => x.Id == f.Leaf && !x.IsActive), "inactive-only assignment rolls up and never becomes unassigned");
        var empty = AgreementInsightHierarchy.Aggregate(dimension, values, [], [], f.Root, true);
        check(empty.TotalUniqueAgreements == 0 && empty.ContextUniqueAgreements == 0 && empty.GroupSum == 0, "empty authorized admin context has controlled zero denominator");
        values[0].ParentId = f.Leaf;
        try { AgreementInsightHierarchy.Aggregate(dimension, values, [], [], null, true); throw new Exception("Cycle accepted"); }
        catch (AgreementValidationException ex) { check(ex.Message == "DashboardInvalidHierarchy", "cyclic graph rejected instead of recursive loop or silent undercount"); }
        values[0].ParentId = Guid.NewGuid();
        try { AgreementInsightHierarchy.Aggregate(dimension, values, [], [], null, true); throw new Exception("Orphan accepted"); }
        catch (AgreementValidationException ex) { check(ex.Message == "DashboardInvalidHierarchy", "missing-parent graph rejected explicitly"); }
        values[0].ParentId = null;
        try { AgreementInsightHierarchy.Aggregate(dimension, values, [f.User], [new(f.User, Guid.NewGuid())], null, true); throw new Exception("Unknown value accepted"); }
        catch (AgreementValidationException ex) { check(ex.Message == "DashboardInvalidHierarchy", "unknown assigned value never becomes unassigned silently"); }
    }
    static async Task Denied(Func<Task> action, Action<bool, string> check, string label)
    { try { await action(); throw new Exception(label); } catch (UnauthorizedAccessException) { check(true, label); } }
    static void Demo(Action<bool, string> check)
    {
        var graph = MagidaDemoData.Create(SeedIds.MagidaAdministrator, DateTimeOffset.UnixEpoch);
        using var reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "demo-reference.json")));
        check(graph.Agreements.Count == 300 && typeof(Agreement).GetProperty("GeographicAreaId") is null && typeof(Agreement).GetProperty("OrganizationElementId") is null, "production demo integration uses 300 agreements with no legacy FK source");
        foreach (var (code, key, roots, count) in new[] { ("GEO", "geography", 4, 75), ("DEPT", "organization_structure", 5, 60) })
        {
            var dimension = graph.Dimensions.Single(x => x.Code == code);
            var pairs = graph.Assignments.Where(x => x.DimensionId == dimension.Id).Select(x => new AgreementInsightAssignment(x.AgreementId, x.ValueId)).ToList();
            AgreementInsightsDto Get(Guid? selected) => AgreementInsightHierarchy.Aggregate(dimension, graph.Values, graph.Agreements.Select(x => x.Id), pairs, selected, true);
            var top = Get(null);
            check(top.TotalUniqueAgreements == 300 && top.ContextUniqueAgreements == 300 && top.GroupSum == 300 && top.Groups.Count == roots && top.Groups.All(x => x.Count == count), code + " production demo: expected root counts and 300 unique agreements");
            var expected = reference.RootElement.GetProperty("distributions").GetProperty(key).EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());
            foreach (var (id, node) in expected)
            {
                var context = Get(id);
                if (context.ContextUniqueAgreements != node.GetProperty("inclusive_unique_agreements").GetInt32() || context.Groups.Where(g => g.Kind == AgreementInsightGroupKind.Direct).Sum(x => x.Count) != node.GetProperty("explicit_agreements").GetInt32())
                    throw new Exception("Demo node context/direct mismatch " + code + " " + id);
                foreach (var group in context.Groups.Where(g => g.NodeId.HasValue))
                    if (group.Count != expected[group.NodeId!.Value].GetProperty("inclusive_unique_agreements").GetInt32()) throw new Exception("Demo child mismatch " + group.NodeId);
                if (context.Groups.Any(g => g.Kind == AgreementInsightGroupKind.Unspecified)) throw new Exception("Missing category below root " + id);
            }
            check(true, code + " every node drill/context/direct/child count matches frozen reference, including all leaves");
        }
    }
}

sealed class InsightFixture
{
    public Guid Account = Guid.NewGuid(), ForeignAccount = Guid.NewGuid(), User = Guid.NewGuid(), Reader = Guid.NewGuid(), Owner = Guid.NewGuid(), Stranger = Guid.NewGuid();
    public Guid Dimension = Guid.NewGuid(), ForeignDimension = Guid.NewGuid(), Root = Guid.NewGuid(), Parent = Guid.NewGuid(), Branch = Guid.NewGuid(), Leaf = Guid.NewGuid(), Sibling = Guid.NewGuid(), Other = Guid.NewGuid(), Hidden = Guid.NewGuid(), Inactive = Guid.NewGuid(), ForeignNode = Guid.NewGuid(), Shared = Guid.NewGuid();
    public Factory Factory { get; } = new(new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public AgreementService Service(Guid? user = null)
    { var context = new Context(user ?? User, Account); return new(Factory, context, new TenantAuthorizationService(Factory, context), null!, NullLogger<AgreementService>.Instance); }
    public async Task Seed()
    {
        await using var db = Factory.CreateDbContext();
        db.Accounts.AddRange(new Account { Id = Account }, new Account { Id = ForeignAccount });
        foreach (var user in new[] { User, Reader, Owner, Stranger })
        {
            db.Users.Add(new User { Id = user, Email = user + "@test", IsActive = true });
            db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), AccountId = Account, UserId = user });
        }
        db.Dimensions.Add(new Dimension { Id = Dimension, AccountId = Account, Name = "Fallback", Code = "ANY", AllowMultiple = true, LeafOnly = false,
            Translations = new[] { ("nb-NO", "Norsk dimensjon"), ("en-GB", "English dimension"), ("sv-SE", "Svensk dimension") }.Select(x => new DimensionTranslation { Id = Guid.NewGuid(), AccountId = Account, DimensionId = Dimension, LanguageCode = x.Item1, Name = x.Item2 }).ToList() });
        db.AgreementDimensionRules.Add(new AgreementDimensionRule { AccountId = Account, DimensionId = Dimension, IsEnabled = true });
        for (var i = 0; i < 3; i++)
        {
            var id = Guid.NewGuid(); db.Dimensions.Add(new Dimension { Id = id, AccountId = Account, Name = "Excluded", Code = "EX" + i, IsActive = i != 1 });
            if (i != 2) db.AgreementDimensionRules.Add(new AgreementDimensionRule { AccountId = Account, DimensionId = id, IsEnabled = i == 1 });
        }
        db.Dimensions.Add(new Dimension { Id = ForeignDimension, AccountId = ForeignAccount, Name = "Foreign definition" });
        db.AgreementDimensionRules.Add(new AgreementDimensionRule { AccountId = ForeignAccount, DimensionId = ForeignDimension, IsEnabled = true });
        foreach (var (id, parent, name) in new[] { (Root, (Guid?)null, "Root"), (Parent, (Guid?)Root, "Parent"), (Branch, (Guid?)Parent, "Branch"), (Leaf, (Guid?)Branch, "Leaf"), (Sibling, (Guid?)Root, "Sibling"), (Other, (Guid?)null, "Other root"), (Hidden, (Guid?)null, "Hidden branch"), (Inactive, (Guid?)Parent, "Historical inactive") })
            db.DimensionValues.Add(new DimensionValue { Id = id, AccountId = Account, DimensionId = Dimension, ParentId = parent, Name = name, IsActive = id != Inactive });
        db.DimensionValues.Add(new DimensionValue { Id = ForeignNode, AccountId = ForeignAccount, DimensionId = ForeignDimension, Name = "Foreign node" });
        void Agreement(Guid id, Guid owner, params Guid[] values)
        {
            db.Agreements.Add(new Agreement { Id = id, AccountId = Account, OwnerUserId = owner });
            foreach (var value in values) db.AgreementDimensionAssignments.Add(new AgreementDimensionAssignment { Id = Guid.NewGuid(), AccountId = Account, AgreementId = id, DimensionId = Dimension, ValueId = value });
        }
        Agreement(Guid.NewGuid(), User, Leaf, Sibling, Other);
        Agreement(Shared, Owner, Parent, Inactive);
        Agreement(Guid.NewGuid(), User, Parent, Leaf);
        Agreement(Guid.NewGuid(), User);
        Agreement(Guid.NewGuid(), Owner, Hidden);
        var archived = Guid.NewGuid(); Agreement(archived, User, Other); db.Agreements.Local.Single(x => x.Id == archived).IsArchived = true;
        var foreign = Guid.NewGuid(); db.Agreements.Add(new Agreement { Id = foreign, AccountId = ForeignAccount, OwnerUserId = User });
        db.AgreementDimensionAssignments.Add(new AgreementDimensionAssignment { Id = Guid.NewGuid(), AccountId = ForeignAccount, AgreementId = foreign, DimensionId = ForeignDimension, ValueId = ForeignNode });
        foreach (var user in new[] { User, Reader }) db.AgreementAccess.Add(new AgreementAccess { Id = Guid.NewGuid(), AccountId = Account, AgreementId = Shared, UserId = user, Level = AgreementAccessLevel.Read });
        await db.SaveChangesAsync();
    }
}
