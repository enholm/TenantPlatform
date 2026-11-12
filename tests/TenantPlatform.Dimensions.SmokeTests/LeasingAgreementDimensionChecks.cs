using System.Collections;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Services.Leasing;

static class LeasingAgreementDimensionChecks
{
    public static async Task Run()
    {
        var dimension = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var option = new LeasingAgreementDimensionOption(dimension, "DEP", "Avdeling", "Beskrivelse", true, false, true,
        [
            new(first, null, "IT", "IT", "Konsern / IT", 1, true, true),
            new(second, null, "Drift", "OPS", "Konsern / Drift", 1, true, false)
        ]);
        var inherited = new LeasingAgreementDimensionEditState(new(true, true, [option]));
        Verify.That(inherited.ReadOnly && inherited.Inherited && inherited.Selected(dimension).SetEquals([first]) && inherited.Changes(false) is null,
            "linked acquisition exposes inherited agreement dimensions read-only without a client patch");
        inherited.Toggle(dimension, second, true);
        Verify.That(inherited.Selected(dimension).SetEquals([first]), "read-only inherited state rejects client-side selection changes");
        inherited.MakeEditable(); inherited.SetSingle(dimension, second);
        var detached = inherited.Changes(false)!;
        Verify.That(!inherited.ReadOnly && !inherited.Inherited && detached.Single().ValueIds!.SequenceEqual([second]),
            "detaching preserves the snapshot and then permits an explicit standalone edit");

        var method = typeof(LeasingService).GetMethod("InheritedPlan", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selectionType = typeof(LeasingService).GetNestedType("AgreementSelection", BindingFlags.NonPublic)!;
        var listType = typeof(List<>).MakeGenericType(selectionType); var source = (IList)Activator.CreateInstance(listType)!;
        source.Add(Activator.CreateInstance(selectionType, dimension, first, "Department", "DEP", "IT", "IT", "Company / IT")!);
        var frameworkA = Guid.NewGuid(); var frameworkB = Guid.NewGuid();
        var copied = (IList)method.Invoke(null, [frameworkA, frameworkB, false, null, source])!;
        Verify.That(copied.Count == 1, "changing framework selects a fresh snapshot from the new framework");
        Verify.That(method.Invoke(null, [frameworkA, frameworkA, false, null, source]) is null,
            "unrelated edit under the same framework does not rewrite the inherited snapshot");
        Verify.That(method.Invoke(null, [frameworkA, null, false, null, source]) is null,
            "framework detachment leaves the last acquisition snapshot unchanged");
        try
        {
            method.Invoke(null, [frameworkA, frameworkA, false,
                new List<LeasingAgreementDimensionPatch> { new() { DimensionId = dimension, ValueIds = [second] } }, source]);
            throw new Exception("FAIL: expected inherited override rejection");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is LeasingValidationException { Message: "LeasingAgreementDimensionsInherited" })
        {
            Verify.That(true, "server inheritance policy rejects manipulated acquisition dimension input");
        }

        using var db = new TenantPlatformDbContextDesignTimeFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        var lineRule = model.FindEntityType(typeof(LeasingDimensionRule))!;
        var agreementRule = model.FindEntityType(typeof(LeasingAgreementDimensionRule))!;
        Verify.That(lineRule.GetTableName() == "leasing_dimension_rules" && agreementRule.GetTableName() == "leasing_agreement_dimension_rules",
            "agreement-level and line-level Leasing rules use independent tables");
        Verify.That(agreementRule.GetCheckConstraints().Any(x => x.Sql.Contains("NOT \"IsRequired\" OR \"IsEnabled\"", StringComparison.Ordinal)),
            "agreement rule persistence enforces required implies available");
        foreach (var type in new[] { typeof(LeasingFrameworkDimensionAssignment), typeof(LeasingAcquisitionDimensionAssignment) })
        {
            var entity = model.FindEntityType(type)!;
            Verify.That(entity.GetForeignKeys().All(x => x.DeleteBehavior == DeleteBehavior.Restrict) &&
                entity.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).TakeLast(2).SequenceEqual(["DimensionId", "ValueId"])),
                type.Name + " has Restrict account-aware parents/value and a unique assignment index");
        }
        var migrations = db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(migrations.Migrations.Single(x => x.Key.EndsWith("_AddLeasingAgreementDimensions")).Value, db.Database.ProviderName!);
        Verify.That(migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).ToHashSet().SetEquals([
            "leasing_agreement_dimension_rules", "leasing_framework_dimension_assignments", "leasing_acquisition_dimension_assignments"])
            && migration.UpOperations.All(x => x is CreateTableOperation or CreateIndexOperation),
            "additive Leasing agreement-dimension migration creates only the three new tables and their indexes");
        Verify.That(migration.DownOperations.OfType<DropTableOperation>().Count() == 3,
            "Leasing agreement-dimension rollback drops only its three new tables");

        var options = new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var memory = new TenantPlatformDbContext(options);
        var actor = Guid.NewGuid(); var account = Guid.NewGuid(); var frameworkId = Guid.NewGuid(); var acquisitionId = Guid.NewGuid();
        var root = Guid.NewGuid(); var multipleDimension = Guid.NewGuid(); var multipleFirst = Guid.NewGuid(); var multipleSecond = Guid.NewGuid();
        memory.Accounts.Add(new Account { Id = account, Name = "Test", DefaultLanguage = "nb-NO" });
        memory.Dimensions.Add(new Dimension { Id = dimension, AccountId = account, Name = "Department", Code = "DEP", IsActive = true, LeafOnly = true });
        memory.DimensionValues.AddRange(
            new DimensionValue { Id = root, AccountId = account, DimensionId = dimension, Name = "Company", Code = "ROOT", IsActive = true },
            new DimensionValue { Id = first, AccountId = account, DimensionId = dimension, ParentId = root, Name = "IT", Code = "IT", IsActive = true },
            new DimensionValue { Id = second, AccountId = account, DimensionId = dimension, ParentId = root, Name = "Operations", Code = "OPS", IsActive = true });
        memory.Dimensions.Add(new Dimension { Id = multipleDimension, AccountId = account, Name = "Locations", Code = "LOC", IsActive = true, AllowMultiple = true });
        memory.DimensionValues.AddRange(
            new DimensionValue { Id = multipleFirst, AccountId = account, DimensionId = multipleDimension, Name = "North", Code = "N", IsActive = true },
            new DimensionValue { Id = multipleSecond, AccountId = account, DimensionId = multipleDimension, Name = "South", Code = "S", IsActive = true });
        memory.LeasingAgreementDimensionRules.Add(new() { AccountId = account, DimensionId = dimension, IsEnabled = true, IsRequired = true });
        memory.LeasingAgreementDimensionRules.Add(new() { AccountId = account, DimensionId = multipleDimension, IsEnabled = true });
        var framework = new LeasingFramework { Id = frameworkId, AccountId = account, DimensionAssignments =
        [
            new() { Id = Guid.NewGuid(), DimensionId = dimension, ValueId = second, DimensionName = "Department", DimensionCode = "DEP", ValueName = "Operations", ValueCode = "OPS", ValuePath = "Company / Operations" }
        ] };
        framework.DimensionAssignments[0].AccountId = framework.AccountId; framework.DimensionAssignments[0].FrameworkId = framework.Id;
        var acquisition = new LeasingAcquisition { Id = acquisitionId, AccountId = framework.AccountId, FrameworkId = framework.Id, Revision = Guid.NewGuid(),
            DimensionAssignments = [new() { Id = Guid.NewGuid(), DimensionId = dimension, ValueId = first, DimensionName = "Department", DimensionCode = "DEP", ValueName = "IT", ValueCode = "IT", ValuePath = "Company / IT" }] };
        acquisition.DimensionAssignments[0].AccountId = framework.AccountId; acquisition.DimensionAssignments[0].AcquisitionId = acquisition.Id;
        memory.LeasingFrameworks.Add(framework); memory.LeasingAcquisitions.Add(acquisition); await memory.SaveChangesAsync();

        var plan = typeof(LeasingService).GetMethod("PlanAgreementDimensions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var emptySelections = Activator.CreateInstance(listType)!;
        async Task<object?> Plan(List<LeasingAgreementDimensionPatch>? patches, bool creating, object existing)
        {
            var pending = (Task)plan.Invoke(null, [memory, account, existing, patches, creating, CancellationToken.None])!;
            await pending; return pending.GetType().GetProperty("Result")!.GetValue(pending);
        }
        var valid = (IList)(await Plan([new() { DimensionId = dimension, ValueIds = [first] }], true, emptySelections))!;
        Verify.That(valid.Count == 1, "required agreement dimension accepts one selectable leaf value");
        await ThrowsPlan(() => Plan([], true, emptySelections), "LeasingAgreementDimensionRequired", "required agreement dimension is enforced on new records");
        await ThrowsPlan(() => Plan([new() { DimensionId = dimension, ValueIds = [first, second] }], true, emptySelections), "LeasingAgreementDimensionSingle", "single-select agreement dimension rejects multiple values");
        var multiple = (IList)(await Plan([
            new() { DimensionId = dimension, ValueIds = [first] },
            new() { DimensionId = multipleDimension, ValueIds = [multipleFirst, multipleSecond] }
        ], true, emptySelections))!;
        Verify.That(multiple.Count == 3, "multi-select agreement dimension accepts multiple selectable values");
        await ThrowsPlan(() => Plan([new() { DimensionId = dimension, ValueIds = [root] }], true, emptySelections), "LeasingAgreementDimensionUnavailable", "leaf-only agreement dimension rejects a parent value");
        await ThrowsPlan(() => Plan([new() { DimensionId = Guid.NewGuid(), ValueIds = [first] }], true, emptySelections), "LeasingAgreementDimensionInvalid", "unknown or foreign-account dimension identity is rejected");
        await ThrowsPlan(() => Plan([new() { DimensionId = dimension, ValueIds = [Guid.NewGuid()] }], true, emptySelections), "LeasingAgreementDimensionInvalid", "unknown or foreign-account value identity is rejected");
        var historical = (IList)Activator.CreateInstance(listType)!;
        historical.Add(Activator.CreateInstance(selectionType, dimension, root, "Department", "DEP", "Company", "ROOT", "Company")!);
        Verify.That(await Plan([new() { DimensionId = dimension, ValueIds = [root] }], false, historical) is null,
            "unchanged historical parent selection remains valid on unrelated edits");
        var oldRevision = acquisition.Revision;
        var service = new LeasingService(null!, null!, null!, null!, TimeProvider.System, NullLogger<LeasingService>.Instance);
        var propagate = typeof(LeasingService).GetMethod("PropagateFrameworkDimensions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)propagate.Invoke(service, [memory, framework, actor, "Classification update", CancellationToken.None])!;
        await memory.SaveChangesAsync();
        Verify.That(acquisition.DimensionAssignments.Single().ValueId == second && acquisition.Revision != oldRevision &&
            await memory.LeasingHistory.AnyAsync(x => x.AcquisitionId == acquisition.Id && x.ActorUserId == actor && x.Action == "FrameworkDimensionsUpdated"),
            "framework dimension changes replace linked acquisition snapshots and record revisioned history");

        static async Task ThrowsPlan(Func<Task<object?>> action, string key, string message)
        {
            try { await action(); throw new Exception("FAIL: expected " + key); }
            catch (LeasingValidationException validation) when (validation.Message == key) { Verify.That(true, message); }
            catch (TargetInvocationException ex) when (ex.InnerException is LeasingValidationException validation && validation.Message == key) { Verify.That(true, message); }
        }
    }
}
