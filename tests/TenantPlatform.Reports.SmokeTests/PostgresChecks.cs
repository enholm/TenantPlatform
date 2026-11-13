using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Authentication;
using TenantPlatform.Web.Services.Reports;

static class PostgresChecks
{
    private const string ExpectedDatabase = "tenantplatform_report_step9";
    private const string ReportMigration = "20261112212606_AddReportPages";

    public static async Task PrepareAsync(Fixture fixture)
    {
        await using var db = fixture.Open();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (!string.Equals(connection.Database, ExpectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException($"REPORT_TEST_CONNECTION must target the disposable database '{ExpectedDatabase}'.");

        await db.Database.MigrateAsync();
        Check.That((await db.Database.GetAppliedMigrationsAsync()).Last() == ReportMigration,
            "empty PostgreSQL database applies the complete migration chain through AddReportPages");
        Check.That(!db.Database.HasPendingModelChanges(),
            "the migrated PostgreSQL schema has no pending model changes");

        var migrator = db.GetService<IMigrator>();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        var previous = migrations[^2];
        await migrator.MigrateAsync(previous);
        await connection.OpenAsync();
        await using (var missing = new NpgsqlCommand(
            "select to_regclass('public.report_pages') is null and to_regclass('public.report_cards') is null", connection))
            Check.That((bool)(await missing.ExecuteScalarAsync())!,
                "down migration removes only the report persistence tables");
        await connection.CloseAsync();
        await migrator.MigrateAsync(ReportMigration);
        Check.That((await db.Database.GetAppliedMigrationsAsync()).Last() == ReportMigration,
            "report migration applies again after a down/up cycle");
    }

    public static async Task VerifyAsync(Fixture fixture)
    {
        await using var db = fixture.Open();
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await connection.OpenAsync();

        await using (var types = new NpgsqlCommand("""
            select
              (select data_type = 'jsonb' from information_schema.columns where table_schema='public' and table_name='report_cards' and column_name='FiltersJson'),
              (select data_type = 'jsonb' from information_schema.columns where table_schema='public' and table_name='report_cards' and column_name='DisplayJson'),
              (select count(*) from pg_constraint where conrelid='public.report_cards'::regclass and contype='c'),
              (select count(*) from pg_constraint where conrelid='public.report_cards'::regclass and contype='f')
            """, connection))
        await using (var reader = await types.ExecuteReaderAsync())
        {
            await reader.ReadAsync();
            Check.That(reader.GetBoolean(0) && reader.GetBoolean(1),
                "report filter and display payloads use PostgreSQL jsonb columns");
            Check.That(reader.GetInt64(2) >= 9 && reader.GetInt64(3) == 2,
                "PostgreSQL exposes the report-card check and tenant-safe foreign-key constraints");
        }

        var card = await db.ReportCards.AsNoTracking().OrderBy(x => x.Id).FirstAsync();
        var page = await db.ReportPages.AsNoTracking().SingleAsync(x => x.Id == card.ReportPageId);
        Check.That(card.FiltersJson.Contains("alpha", StringComparison.Ordinal) &&
            card.DisplayJson.Contains("showLegend", StringComparison.Ordinal),
            "jsonb payloads roundtrip through the report page service without semantic loss");

        var pages = fixture.Pages(fixture.Admin);
        var first = await pages.CreateAsync(fixture.Account, new("Concurrent A"));
        var second = await pages.CreateAsync(fixture.Account, new("Concurrent B"));
        var forward = pages.SaveWorkspaceAsync(fixture.Account, new(
        [
            new(first.Id, first.Revision, "Forward A", []),
            new(second.Id, second.Revision, "Forward B", [])
        ]));
        var reverse = fixture.Pages(fixture.Admin).SaveWorkspaceAsync(fixture.Account, new(
        [
            new(second.Id, second.Revision, "Reverse B", []),
            new(first.Id, first.Revision, "Reverse A", [])
        ]));
        var outcomes = await Task.WhenAll(Outcome(forward), Outcome(reverse));
        Check.That(outcomes.Count(x => x) == 1,
            "concurrent cross-page saves on separate contexts produce one winner and one revision conflict");
        var firstAfter = await pages.GetAsync(fixture.Account, first.Id);
        var secondAfter = await pages.GetAsync(fixture.Account, second.Id);
        Check.That((firstAfter.Name == "Forward A" && secondAfter.Name == "Forward B") ||
            (firstAfter.Name == "Reverse A" && secondAfter.Name == "Reverse B"),
            "deterministic FOR UPDATE ordering prevents partial cross-page persistence");

        await using var invalid = new NpgsqlCommand("""
            insert into report_cards
            ("Id","AccountId","ReportPageId","Name","Module","DefinitionVersion","FiltersJson","PeriodRule",
             "GroupingKind","DisplayJson","GridColumn","GridRow","GridWidth","GridHeight","SortOrder")
            values (@id,@account,@page,'invalid',0,1,'[]'::jsonb,0,0,'{}'::jsonb,0,1,1,1,0)
            """, connection);
        invalid.Parameters.AddWithValue("id", Guid.NewGuid());
        invalid.Parameters.AddWithValue("account", page.AccountId);
        invalid.Parameters.AddWithValue("page", page.Id);
        try
        {
            await invalid.ExecuteNonQueryAsync();
            throw new InvalidOperationException("Expected PostgreSQL to reject an invalid report-card layout.");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            Check.That(true, "PostgreSQL rejects an invalid report-card layout through its check constraint");
        }

        var browserPassword = Environment.GetEnvironmentVariable("REPORT_BROWSER_SEED_PASSWORD");
        if (!string.IsNullOrEmpty(browserPassword))
        {
            var admin = await db.Users.SingleAsync(x => x.Id == fixture.Admin);
            var login = new LoginAccount
            {
                Id = Guid.NewGuid(), UserId = admin.Id, Email = admin.Email,
                IsEnabled = true, LastAccountId = fixture.Account, CreatedUtc = DateTimeOffset.UtcNow
            };
            login.PasswordHash = new PasswordService().HashPassword(login, browserPassword);
            db.LoginAccounts.Add(login);
            await db.SaveChangesAsync();
            Console.WriteLine($"BROWSER_FIXTURE account={fixture.Account:D} user={admin.Id:D} email={admin.Email}");
        }
    }

    private static async Task<bool> Outcome(Task<IReadOnlyList<ReportPageDto>> operation)
    {
        try
        {
            await operation;
            return true;
        }
        catch (ReportPageConflictException)
        {
            return false;
        }
    }
}
