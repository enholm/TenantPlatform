using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Properties;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Services.Agreements;

static class FinalModelChecks
{
    public static void Run()
    {
        using var db = new TenantPlatformDbContextDesignTimeFactory().CreateDbContext([]);
        var model = db.GetService<IDesignTimeModel>().Model;
        foreach (var removed in new[] { "GeographicArea", "OrganizationElement" })
        {
            Verify.That(typeof(Account).Assembly.GetType("TenantPlatform.Core.Accounts." + removed) is null && model.FindEntityType("TenantPlatform.Core.Accounts." + removed) is null, removed + " absent from current CLR assembly and EF model");
            foreach (var type in new[] { typeof(Agreement), typeof(SaveAgreementRequest), typeof(AgreementDetailsDto), typeof(AgreementOptionsDto) })
                Verify.That(type.GetProperties().All(p => !p.Name.StartsWith(removed, StringComparison.Ordinal)), type.Name + " contains no " + removed + " compatibility fields/options");
        }
        Verify.That(typeof(ApproveAnalysisRequest).GetProperty("Agreement")!.PropertyType == typeof(SaveAgreementRequest), "analysis uses the same clean agreement request contract");
        Verify.That(!model.GetEntityTypes().Any(t => t.GetTableName() is "geographic_areas" or "organization_elements"), "final model contains no legacy tables");
        var agreement = model.FindEntityType(typeof(Agreement))!;
        Verify.That(new[] { typeof(Organization), typeof(Building), typeof(Unit) }.All(t => agreement.GetForeignKeys().Any(f => f.PrincipalEntityType.ClrType == t && f.DeleteBehavior == DeleteBehavior.Restrict)), "actual counterparty, building and unit relationships remain restricted");
        foreach (var type in new[] { typeof(Dimension), typeof(DimensionValue), typeof(DimensionTranslation), typeof(AgreementDimensionAssignment), typeof(AgreementDimensionRule), typeof(AgreementDimensionHistory) })
            Verify.That(model.FindEntityType(type)!.GetForeignKeys().All(f => f.DeleteBehavior == DeleteBehavior.Restrict), type.Name + " generic relationships retain Restrict deletion");
        var values = model.FindEntityType(typeof(DimensionValue))!;
        Verify.That(values.GetForeignKeys().Any(f => f.PrincipalEntityType == values && f.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "DimensionId", "ParentId" })), "generic parent FK is account and dimension aware");
        var migrations = db.GetService<IMigrationsAssembly>();
        var ordered = migrations.Migrations.OrderBy(x => x.Key).ToList();
        var legacyIndex = ordered.FindIndex(x => x.Key.EndsWith("_RemoveLegacyAgreementClassifications"));
        var legacy = migrations.CreateMigration(ordered[legacyIndex].Value, db.Database.ProviderName!);
        var allowedAdditiveMigrations = new[] { "_AddLeasingAgreementDimensions", "_AddReportPages" };
        Verify.That(legacyIndex > 0 && ordered.Skip(legacyIndex + 1)
                .All(x => allowedAdditiveMigrations.Any(suffix => x.Key.EndsWith(suffix))),
            "legacy drop remains after its additive replacement migrations; later additive features are allowed");
        var up = legacy.UpOperations;
        Verify.That(up.Count == 8 && up.Take(2).All(x => x is DropForeignKeyOperation) && up.Skip(2).Take(2).All(x => x is DropIndexOperation) && up.Skip(4).Take(2).All(x => x is DropColumnOperation) && up.Skip(6).All(x => x is DropTableOperation), "new Up contains only two FKs, two indexes, two columns, then two tables");
        Verify.That(up.OfType<DropColumnOperation>().All(x => x.Table == "agreements") && up.OfType<DropColumnOperation>().Select(x => x.Name).Order().SequenceEqual(new[] { "GeographicAreaId", "OrganizationElementId" }) && up.OfType<DropTableOperation>().Select(x => x.Name).Order().SequenceEqual(new[] { "geographic_areas", "organization_elements" }), "drop operations target exactly the replaced classification schema");
        Verify.That(!up.Any(x => x is SqlOperation or InsertDataOperation or UpdateDataOperation) && legacy.DownOperations.Count == 12, "legacy removal has no backfill/copy SQL and Down reconstructs schema only");
        var schema = new SchemaAudit();
        foreach (var entry in ordered)
        {
            var migration = migrations.CreateMigration(entry.Value, db.Database.ProviderName!);
            schema.Apply(migration.UpOperations, entry.Key);
        }
        schema.Compare(model.GetRelationalModel());
        var rollback = new SchemaAudit();
        foreach (var entry in ordered.Take(legacyIndex + 1))
            rollback.Apply(migrations.CreateMigration(entry.Value, db.Database.ProviderName!).UpOperations, entry.Key);
        rollback.Apply(legacy.DownOperations, "legacy migration Down");
        var previous = migrations.CreateMigration(ordered[legacyIndex - 1].Value, db.Database.ProviderName!);
        var previousModel = db.GetService<IModelRuntimeInitializer>().Initialize(previous.TargetModel, designTime: true);
        rollback.Compare(previousModel.GetRelationalModel());
        Verify.That(rollback.HasTable("geographic_areas") && rollback.HasTable("organization_elements"), "Down restores both table structures");
        rollback.Apply(legacy.UpOperations, "legacy migration Up after Down");
        var legacyModel = db.GetService<IModelRuntimeInitializer>().Initialize(legacy.TargetModel, designTime: true);
        rollback.Compare(legacyModel.GetRelationalModel());
        Verify.That(schema.DataSqlCount > 0, $"{ordered.Count} migrations inspected; {schema.DataSqlCount} historical data SQL operations classified, never executed");
        Verify.That(!db.Database.HasPendingModelChanges() && db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed, "current snapshot matches final model with connection closed");
        var guard = db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()!.Interceptors!
            .OfType<Microsoft.EntityFrameworkCore.Diagnostics.DbConnectionInterceptor>().Single();
        // Invoke the guard directly, never ask EF or Npgsql to open a connection.
        try { guard.ConnectionOpening(db.Database.GetDbConnection(), null!, default); throw new Exception("Offline guard did not reject simulated opening"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("design-time context is offline")) { Verify.That(true, "unchanged design-time guard rejects connection before provider opening"); }
    }
}
