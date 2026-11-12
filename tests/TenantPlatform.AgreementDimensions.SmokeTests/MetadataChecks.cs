using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Infrastructure.Persistence;

static class MetadataChecks
{
    public static void Run()
    {
        using var db = new TenantPlatformDbContextDesignTimeFactory().CreateDbContext([]);
        Check.That(!db.Database.HasPendingModelChanges(), "current Npgsql model matches snapshot without database connection");
        var model = db.GetService<IDesignTimeModel>().Model;
        var rule = model.FindEntityType(typeof(AgreementDimensionRule))!;
        Check.That(rule.FindPrimaryKey()!.Properties.Select(x => x.Name).SequenceEqual(new[] { "AccountId", "DimensionId" }) && rule.FindProperty("Revision")!.IsConcurrencyToken,
            "rule key is account-scoped and GUID revision is a concurrency token");
        Check.That(rule.GetCheckConstraints().Single().Sql == "NOT \"IsRequired\" OR \"IsEnabled\"", "rule schema enforces required implies enabled");
        Check.That(rule.GetForeignKeys().Single().Properties.Select(x => x.Name).SequenceEqual(new[] { "AccountId", "DimensionId" }) && rule.GetForeignKeys().Single().DeleteBehavior == DeleteBehavior.Restrict,
            "module rule references only dimensions within its account");
        var assignment = model.FindEntityType(typeof(AgreementDimensionAssignment))!;
        Check.That(assignment.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "AgreementId", "DimensionId", "ValueId" })), "assignment unique index prevents duplicate explicit value per agreement");
        var fks = assignment.GetForeignKeys().ToList();
        Check.That(fks.Count == 2 && fks.All(x => x.DeleteBehavior == DeleteBehavior.Restrict) &&
            fks.Any(x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "AgreementId" }) && x.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "Id" })) &&
            fks.Any(x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "DimensionId", "ValueId" }) && x.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "DimensionId", "Id" })),
            "assignment FKs enforce matching account, agreement, dimension and value");
        var history = model.FindEntityType(typeof(AgreementDimensionHistory))!;
        Check.That(history.GetForeignKeys().Any(x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "AgreementId" })) && history.FindProperty("BeforeJson")!.GetColumnType() == "jsonb", "history is linked account-aware to agreement and stores JSON snapshots");
        Check.That(model.FindEntityType(typeof(Agreement))!.FindProperty("Revision")!.IsConcurrencyToken, "assignment changes reuse existing agreement concurrency token");
        var migrations = db.GetService<IMigrationsAssembly>();
        var migration = migrations.CreateMigration(migrations.Migrations.Single(x => x.Key.EndsWith("_AddAgreementDimensions")).Value, db.Database.ProviderName!);
        var tables = migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).OrderBy(x => x).ToArray();
        Check.That(tables.SequenceEqual(new[] { "agreement_dimension_assignments", "agreement_dimension_history", "agreement_dimension_rules" }) &&
            migration.UpOperations.All(x => x is CreateTableOperation or CreateIndexOperation), "new migration is additive and affects only three agreement-dimension tables and indexes");
        Check.That(migration.DownOperations.Count == 3 && migration.DownOperations.All(x => x is DropTableOperation), "rollback drops only the three newly introduced tables");
        Check.That(model.FindEntityType(typeof(Agreement))!.FindProperty("GeographicAreaId") is null && model.FindEntityType(typeof(Agreement))!.FindProperty("OrganizationElementId") is null,
            "legacy agreement fields are absent from active schema");
        Check.That(db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed, "offline metadata inspection leaves connection closed");
    }
}
