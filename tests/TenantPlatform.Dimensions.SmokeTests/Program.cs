using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Infrastructure.Persistence;

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
LocalizationChecks.Run();
await ServiceChecks.Run();
await ComponentChecks.Run();
await AccountDeletionChecks.Run();
FinalModelChecks.Run();

// Model construction and migration operation inspection only: this factory has
// no Web host/configuration and rejects any attempt to open a connection.
await using (var db = new TenantPlatformDbContextDesignTimeFactory().CreateDbContext([]))
{
    Verify.That(!db.Database.HasPendingModelChanges(), "Npgsql design-time model matches current migration snapshot (no connection)");
    var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
    var translation = model.FindEntityType(typeof(DimensionTranslation))!;
    Verify.That(translation.GetIndexes().Any(x => x.IsUnique && x.Properties.Select(p => p.Name)
        .SequenceEqual(new[] { "AccountId", "DimensionId", "LanguageCode" })), "translation unique index contains account, dimension and language");
    var fk = translation.GetForeignKeys().Single();
    Verify.That(fk.DeleteBehavior == DeleteBehavior.Restrict && fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "DimensionId" }) &&
        fk.PrincipalKey.Properties.Select(p => p.Name).SequenceEqual(new[] { "AccountId", "Id" }), "translation FK is account-aware and restricts deletion");
    Verify.That(translation.FindProperty("Name")!.GetMaxLength() == 200 && translation.FindProperty("Description")!.GetMaxLength() == 2000 &&
        translation.FindProperty("LanguageCode")!.GetMaxLength() == 10, "translation field limits are configured");
    var assembly = db.GetService<IMigrationsAssembly>();
    var migration = assembly.CreateMigration(assembly.Migrations.Single(x => x.Key.EndsWith("_AddDimensionTranslations")).Value, db.Database.ProviderName!);
    Verify.That(migration.UpOperations.Count == 2 && migration.UpOperations.OfType<CreateTableOperation>().Single().Name == "dimension_translations" &&
        migration.UpOperations.OfType<CreateIndexOperation>().Single().IsUnique, "additive migration contains only translation table and unique index");
    Verify.That(migration.DownOperations.Single() is DropTableOperation { Name: "dimension_translations" }, "rollback affects only new translation table");
    Verify.That(db.Database.GetDbConnection().State == System.Data.ConnectionState.Closed, "design-time connection remains closed");
}
Console.WriteLine($"Dimensions smoke checks passed ({Verify.Count} checks; no database connections, SQL execution, migration application or initialization).");

static class Verify
{
    public static int Count { get; private set; }
    public static void That(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Count++;
        Console.WriteLine("PASS: " + message);
    }
    public static async Task Throws<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); } catch (T) { That(true, message); return; }
        throw new Exception("FAIL: " + message + " (expected " + typeof(T).Name + ")");
    }
}
