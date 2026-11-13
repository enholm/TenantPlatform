using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Localization;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Core.Reports;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;
using TenantPlatform.Web.Services.Reports;

if (args.Contains("--browser-fixtures", StringComparer.Ordinal))
{
    Console.WriteLine(JsonSerializer.Serialize(BrowserHarnessFixtures.Create(),
        new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return;
}

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
var postgresConnection = Environment.GetEnvironmentVariable("REPORT_TEST_CONNECTION");
var fixture = new Fixture(postgresConnection);
if (fixture.IsPostgres)
    await PostgresChecks.PrepareAsync(fixture);
await fixture.SeedAsync();
await AgreementChecks.RunAsync(fixture);
await LeasingChecks.RunAsync(fixture);
await CatalogChecks.RunAsync(fixture);
await PeriodChecks.RunAsync(fixture);
await CompatibilityChecks.RunAsync(fixture);
await TreeChecks.RunAsync(fixture);
await ExecutionChecks.RunAsync(fixture);
await LeasingExecutionChecks.RunAsync(fixture);
WorkspaceChecks.Run();
ChartLayoutChecks.Run();
await ReportUiChecks.RunAsync();
await PageChecks.RunAsync(fixture);
if (fixture.IsPostgres)
{
    await PostgresChecks.VerifyAsync(fixture);
    Console.WriteLine($"Report module PostgreSQL smoke checks passed ({Check.Count} checks; isolated disposable database; no app startup).");
}
else
{
    Console.WriteLine($"Report module smoke checks passed ({Check.Count} checks; pure date logic and EF InMemory only; no PostgreSQL translation, database connection or app startup).");
}

static class AgreementChecks
{
    public static async Task RunAsync(Fixture f)
    {
        var admin = f.Agreements(f.Admin);
        var all = await admin.ListAsync(f.Account, new AgreementFilter());
        Check.Sequence(all.Items.Select(x => x.Id), [f.AgreementAlpha, f.AgreementArchived, f.AgreementBeta, f.AgreementGamma],
            "agreement list retains title/id ordering and includes archived records");
        Check.That(all.TotalCount == 4 && all.Page == 1 && all.PageSize == 25, "agreement paging contract is unchanged");

        var search = await admin.ListAsync(f.Account, new AgreementFilter { Search = "beta partner" });
        Check.Sequence(search.Items.Select(x => x.Id), [f.AgreementBeta], "agreement search still includes counterparty name");
        Check.Sequence((await admin.ListAsync(f.Account, new AgreementFilter { Status = AgreementStatus.Draft })).Items.Select(x => x.Id),
            [f.AgreementBeta], "agreement status filter retains parity");
        Check.Sequence((await admin.ListAsync(f.Account, new AgreementFilter { Type = AgreementType.Lease })).Items.Select(x => x.Id),
            [f.AgreementAlpha, f.AgreementArchived], "agreement type filter retains parity");
        Check.Sequence((await admin.ListAsync(f.Account, new AgreementFilter { OwnerUserId = f.OwnerB })).Items.Select(x => x.Id),
            [f.AgreementGamma], "agreement owner filter retains parity");
        Check.Sequence((await admin.ListAsync(f.Account, new AgreementFilter { Attention = AgreementAttention.Notice })).Items.Select(x => x.Id),
            [f.AgreementAlpha], "attention filter keeps dashboard semantics and excludes archived records");

        var parent = await admin.ListAsync(f.Account, new AgreementFilter { Dimensions = [f.Dimension(f.DimensionOne, f.Root)] });
        Check.Sequence(parent.Items.Select(x => x.Id), [f.AgreementAlpha, f.AgreementBeta],
            "agreement parent dimension filter includes descendants");
        var and = await admin.ListAsync(f.Account, new AgreementFilter
        {
            Dimensions = [f.Dimension(f.DimensionOne, f.Root), f.Dimension(f.DimensionTwo, f.DepartmentA)]
        });
        Check.Sequence(and.Items.Select(x => x.Id), [f.AgreementAlpha], "agreement dimensions retain AND semantics");
        var unspecified = await admin.ListAsync(f.Account, new AgreementFilter
        {
            Dimensions = [new DimensionListFilterSelection { DimensionId = f.DimensionOne, IncludeUnspecified = true }]
        });
        Check.Sequence(unspecified.Items.Select(x => x.Id), [f.AgreementArchived, f.AgreementGamma],
            "agreement not-specified filter retains assignment semantics");
        var historical = await admin.ListAsync(f.Account, new AgreementFilter { Dimensions = [f.Dimension(f.HistoricalDimension, f.HistoricalValue)] });
        Check.Sequence(historical.Items.Select(x => x.Id), [f.AgreementAlpha], "historical agreement dimension choices remain filterable");
        Check.That(all.DimensionFilters.Dimensions.Single(x => x.DimensionId == f.HistoricalDimension).IsHistorical,
            "historical agreement dimensions remain in the authorized filter catalog");

        var reader = await f.Agreements(f.Reader).ListAsync(f.Account, new AgreementFilter());
        Check.Sequence(reader.Items.Select(x => x.Id), [f.AgreementAlpha, f.AgreementBeta],
            "agreement resource grants do not leak other agreements");
        Check.That(reader.Owners.Select(x => x.Id).SequenceEqual([f.OwnerA]),
            "agreement filter lookup does not leak inaccessible owner names");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Agreements(f.Reader, f.ForeignAccount)
            .ListAsync(f.ForeignAccount, new AgreementFilter()), "agreement selected-account and membership checks remain enforced");
    }
}

static class LeasingChecks
{
    public static async Task RunAsync(Fixture f)
    {
        var admin = f.Leasing(f.Admin);
        var all = await admin.ListAsync(f.Account, new LeasingListFilter());
        Check.Sequence(all.Items.Select(x => x.Id),
            [f.FrameworkAlpha, f.AcquisitionChild, f.AcquisitionHidden, f.FrameworkHidden, f.AcquisitionStandalone],
            "leasing list retains common name/id ordering across separate authorized scopes");
        Check.That(all.Count == 5 && all.Page == 1 && all.PageSize == 25, "leasing paging contract is unchanged");

        Check.Sequence((await admin.ListAsync(f.Account, new LeasingListFilter { Search = "standalone-ref" })).Items.Select(x => x.Id),
            [f.AcquisitionStandalone], "leasing search still includes acquisition reference");
        Check.Sequence((await admin.ListAsync(f.Account, new LeasingListFilter { Kind = "frameworks" })).Items.Select(x => x.Id),
            [f.FrameworkAlpha, f.FrameworkHidden], "leasing framework kind retains parity");
        Check.Sequence((await admin.ListAsync(f.Account, new LeasingListFilter { Kind = "acquisitions" })).Items.Select(x => x.Id),
            [f.AcquisitionChild, f.AcquisitionHidden, f.AcquisitionStandalone], "leasing acquisition kind retains parity");
        Check.Sequence((await admin.ListAsync(f.Account, new LeasingListFilter { Kind = "orders" })).Items.Select(x => x.Id),
            [f.AcquisitionHidden, f.AcquisitionStandalone], "leasing orders kind means standalone acquisitions");
        Check.Sequence((await admin.ListAsync(f.Account, new LeasingListFilter { Status = "Open" })).Items.Select(x => x.Id),
            [f.FrameworkAlpha], "leasing shared status filter keeps type-specific enum parsing");
        Check.Sequence((await admin.ListAsync(f.Account, new LeasingListFilter { Owner = f.OwnerA })).Items.Select(x => x.Id),
            [f.FrameworkAlpha, f.AcquisitionStandalone], "leasing owner filter retains parity");

        var inherited = await admin.ListAsync(f.Account, new LeasingListFilter
        {
            Kind = "acquisitions", Dimensions = [f.Dimension(f.DimensionTwo, f.DepartmentA)]
        });
        Check.Sequence(inherited.Items.Select(x => x.Id), [f.AcquisitionChild],
            "leasing acquisition filter uses the inherited agreement-dimension snapshot");
        var parent = await admin.ListAsync(f.Account, new LeasingListFilter { Dimensions = [f.Dimension(f.DimensionOne, f.Root)] });
        Check.Sequence(parent.Items.Select(x => x.Id), [f.FrameworkAlpha, f.AcquisitionChild, f.AcquisitionStandalone],
            "leasing parent dimension filter includes descendants for both scopes");
        var and = await admin.ListAsync(f.Account, new LeasingListFilter
        {
            Dimensions = [f.Dimension(f.DimensionOne, f.Root), f.Dimension(f.DimensionTwo, f.DepartmentA)]
        });
        Check.Sequence(and.Items.Select(x => x.Id), [f.FrameworkAlpha, f.AcquisitionChild],
            "leasing dimensions retain AND semantics");
        var unspecified = await admin.ListAsync(f.Account, new LeasingListFilter
        {
            Dimensions = [new DimensionListFilterSelection { DimensionId = f.DimensionOne, IncludeUnspecified = true }]
        });
        Check.Sequence(unspecified.Items.Select(x => x.Id), [f.AcquisitionHidden, f.FrameworkHidden],
            "leasing not-specified filter retains semantics across both scopes");
        var historical = await admin.ListAsync(f.Account, new LeasingListFilter
        {
            Dimensions = [f.Dimension(f.HistoricalDimension, f.HistoricalValue)]
        });
        Check.Sequence(historical.Items.Select(x => x.Id), [f.FrameworkAlpha],
            "historical leasing agreement-dimension choices remain filterable");

        var owner = await f.Leasing(f.OwnerA).ListAsync(f.Account, new LeasingListFilter());
        Check.Sequence(owner.Items.Select(x => x.Id), [f.FrameworkAlpha, f.AcquisitionChild, f.AcquisitionStandalone],
            "leasing framework ownership grants child acquisition access without leaking other resources");
        Check.That(owner.Items.Any(x => x.Id == f.AcquisitionStandalone) && owner.Items.All(x => x.Id != f.AcquisitionHidden),
            "standalone acquisition scope remains owner-bound");
        var limitReader = await f.Leasing(f.LimitReader).ListAsync(f.Account, new LeasingListFilter());
        Check.Sequence(limitReader.Items.Select(x => x.Id), [f.FrameworkAlpha, f.FrameworkHidden],
            "leasing limit-reader expands only the framework scope, not acquisitions");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Leasing(f.OwnerA, f.ForeignAccount)
            .ListAsync(f.ForeignAccount, new LeasingListFilter()), "leasing selected-account and membership checks remain enforced");
    }
}

static class CatalogChecks
{
    public static async Task RunAsync(Fixture f)
    {
        var catalog = f.Catalog(f.Admin);
        var agreements = await catalog.GetAsync(f.Account, ReportModule.Agreements);
        Check.That(agreements.Filters.Select(x => x.Key).ToHashSet().SetEquals(
        [
            new AgreementReportFilterKey(AgreementReportFilter.Search),
            new AgreementReportFilterKey(AgreementReportFilter.Status),
            new AgreementReportFilterKey(AgreementReportFilter.Type),
            new AgreementReportFilterKey(AgreementReportFilter.Owner),
            new AgreementReportFilterKey(AgreementReportFilter.Attention)
        ]), "agreement report catalog publishes exactly the verified list filters");
        Check.That(agreements.DimensionScope == ReportDimensionScope.Agreement &&
            agreements.DimensionFilters.Dimensions.Any(x => x.DimensionId == f.HistoricalDimension),
            "agreement catalog reuses the list dimension catalog including historical choices");
        Check.That(agreements.DynamicFilters.All(x => x.Key.Scope == ReportDimensionScope.Agreement) &&
            agreements.DynamicFilters.Select(x => x.Key.DimensionId).SequenceEqual(
                agreements.DimensionFilters.Dimensions.Select(x => x.DimensionId)),
            "agreement dynamic filters have typed GUID identities over the shared dimension catalog");
        Check.That(agreements.Groupings.OfType<ReportGroupingDefinition>().All(x =>
            x.Key is not ReportDimensionGroupingKey key || key.Scope == ReportDimensionScope.Agreement),
            "agreement dynamic groupings have typed agreement scope");
        Check.That(NoPropertyCapabilities(agreements), "agreement catalog cannot offer property, building or unit filters/groupings");

        var leasing = await catalog.GetAsync(f.Account, ReportModule.Leasing);
        Check.That(leasing.Filters.Select(x => x.Key).ToHashSet().SetEquals(
        [
            new LeasingReportFilterKey(LeasingCatalogFilter.Search),
            new LeasingReportFilterKey(LeasingCatalogFilter.Kind),
            new LeasingReportFilterKey(LeasingCatalogFilter.Status),
            new LeasingReportFilterKey(LeasingCatalogFilter.Owner)
        ]), "leasing report catalog publishes exactly the verified list filters");
        Check.That(leasing.DimensionScope == ReportDimensionScope.LeasingAgreement &&
            leasing.DeferredDimensionScopes.SequenceEqual([ReportDimensionScope.LeasingLineAllocation]),
            "leasing catalog separates agreement dimensions from deferred line/allocation dimensions");
        Check.That(NoPropertyCapabilities(leasing), "leasing catalog cannot offer property or equipment-location filters/groupings");
        var kinds = leasing.Filters.Single(x => x.Key == new LeasingReportFilterKey(LeasingCatalogFilter.Kind)).Options;
        Check.That(kinds.Any(x => x.Value == new ReportEnumValue<LeasingCatalogKind>(LeasingCatalogKind.StandaloneAcquisitions) &&
            x.Label.ResourceKey == "LeasingStandaloneAcquisitions"), "leasing standalone kind has a stable typed identity and localized resource key");

        agreements.Validate(
        [
            new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Status),
                new ReportEnumValue<AgreementStatus>(AgreementStatus.Active)),
            new ReportDimensionFilterSelection(new ReportDimensionFilterKey(ReportDimensionScope.Agreement, f.DimensionOne), f.Root, false)
        ]);
        Check.That(true, "catalog accepts offered fixed and hierarchical dimension values");
        await Check.ThrowsAsync<ReportCatalogValidationException>(() => catalog.ValidateAsync(f.Account, ReportModule.Agreements,
            [new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Owner), new ReportEntityValue(Guid.NewGuid()))]),
            "catalog rejects an unavailable owner identity");
        await Check.ThrowsAsync<ReportCatalogValidationException>(() => catalog.ValidateAsync(f.Account, ReportModule.Agreements,
            [new ReportDimensionFilterSelection(new ReportDimensionFilterKey(ReportDimensionScope.Agreement, Guid.NewGuid()), f.Root, false)]),
            "catalog rejects an unknown dimension identity");
        await Check.ThrowsAsync<ReportCatalogValidationException>(() => catalog.ValidateAsync(f.Account, ReportModule.Agreements,
            [new ReportDimensionFilterSelection(new ReportDimensionFilterKey(ReportDimensionScope.Agreement, f.DimensionOne), f.ForeignValue, false)]),
            "catalog rejects a dimension value outside the authorized catalog");
        await Check.ThrowsAsync<ReportCatalogValidationException>(() => catalog.ValidateAsync(f.Account, ReportModule.Leasing,
            [new ReportDimensionFilterSelection(new ReportDimensionFilterKey(ReportDimensionScope.LeasingLineAllocation, f.DimensionOne), f.Root, false)]),
            "catalog rejects deferred line/allocation dimensions as list filters");
        await Check.ThrowsAsync<ReportCatalogValidationException>(() => catalog.ValidateAsync(f.Account, ReportModule.Leasing,
            [new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Status), new ReportEnumValue<AgreementStatus>(AgreementStatus.Active))]),
            "catalog rejects a filter identity from the wrong module");

        var readerCatalog = await f.Catalog(f.Reader).GetAsync(f.Account, ReportModule.Agreements);
        var ownerOptions = readerCatalog.Filters.Single(x => x.Key == new AgreementReportFilterKey(AgreementReportFilter.Owner)).Options;
        Check.That(ownerOptions.Count == 1 && ownerOptions[0].Value == new ReportEntityValue(f.OwnerA),
            "report lookup values follow the same authorized agreement scope");
        await Check.ThrowsAsync<UnauthorizedAccessException>(() => f.Catalog(f.Reader, f.ForeignAccount)
            .GetAsync(f.ForeignAccount, ReportModule.Agreements), "report catalog rechecks tenant membership");
    }

    private static bool NoPropertyCapabilities(ReportModuleCatalog catalog) =>
        catalog.Filters.All(x => NotProperty(x.Label)) && catalog.Groupings.All(x => NotProperty(x.Label));

    private static bool NotProperty(ReportCatalogLabel label)
    {
        var value = (label.ResourceKey ?? "") + " " + (label.Text ?? "");
        return !new[] { "Building", "Unit", "Property", "Location", "Eiendom", "Bygg", "Lokasjon" }
            .Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase));
    }
}

sealed class Fixture
{
    public Guid Account { get; } = Guid.NewGuid();
    public Guid ForeignAccount { get; } = Guid.NewGuid();
    public Guid Admin { get; } = Guid.NewGuid();
    public Guid OwnerA { get; } = Guid.NewGuid();
    public Guid OwnerB { get; } = Guid.NewGuid();
    public Guid Reader { get; } = Guid.NewGuid();
    public Guid LimitReader { get; } = Guid.NewGuid();
    public Guid NoModuleAccess { get; } = Guid.NewGuid();
    public Guid ForeignUser { get; } = Guid.NewGuid();
    public Guid PartyA { get; } = Guid.NewGuid();
    public Guid PartyB { get; } = Guid.NewGuid();
    public Guid ForeignParty { get; } = Guid.NewGuid();
    public Guid DimensionOne { get; } = Guid.NewGuid();
    public Guid DimensionTwo { get; } = Guid.NewGuid();
    public Guid HistoricalDimension { get; } = Guid.NewGuid();
    public Guid ForeignDimension { get; } = Guid.NewGuid();
    public Guid Root { get; } = Guid.NewGuid();
    public Guid Child { get; } = Guid.NewGuid();
    public Guid Grandchild { get; } = Guid.NewGuid();
    public Guid DepartmentA { get; } = Guid.NewGuid();
    public Guid DepartmentB { get; } = Guid.NewGuid();
    public Guid HistoricalValue { get; } = Guid.NewGuid();
    public Guid ForeignValue { get; } = Guid.NewGuid();
    public Guid AgreementAlpha { get; } = Guid.NewGuid();
    public Guid AgreementArchived { get; } = Guid.NewGuid();
    public Guid AgreementBeta { get; } = Guid.NewGuid();
    public Guid AgreementGamma { get; } = Guid.NewGuid();
    public Guid AlphaLine { get; } = Guid.NewGuid();
    public Guid BetaLine { get; } = Guid.NewGuid();
    public Guid GammaLine { get; } = Guid.NewGuid();
    public Guid FrameworkAlpha { get; } = Guid.NewGuid();
    public Guid FrameworkHidden { get; } = Guid.NewGuid();
    public Guid AcquisitionChild { get; } = Guid.NewGuid();
    public Guid AcquisitionStandalone { get; } = Guid.NewGuid();
    public Guid AcquisitionHidden { get; } = Guid.NewGuid();
    public Guid ChildItem { get; } = Guid.NewGuid();
    public Guid StandaloneItem { get; } = Guid.NewGuid();
    public Guid ChildEquipment { get; } = Guid.NewGuid();
    public Guid StandaloneEquipment { get; } = Guid.NewGuid();
    private readonly InMemoryDatabaseRoot root = new();
    private readonly string database = Guid.NewGuid().ToString();
    private readonly string? postgresConnection;

    public Fixture(string? postgresConnection = null)
    {
        this.postgresConnection = string.IsNullOrWhiteSpace(postgresConnection) ? null : postgresConnection;
    }

    public bool IsPostgres => postgresConnection is not null;

    public TenantPlatformDbContext Open()
    {
        var builder = new DbContextOptionsBuilder<TenantPlatformDbContext>();
        if (postgresConnection is null)
            builder.UseInMemoryDatabase(database, root);
        else
            builder.UseNpgsql(postgresConnection);
        var options = builder.Options;
        return new(options);
    }

    public AgreementService Agreements(Guid user, Guid? selected = null)
    {
        var context = new TestContext(user, selected ?? Account);
        var factory = new Factory(this);
        return new(factory, context, new TenantAuthorizationService(factory, context), null!,
            NullLogger<AgreementService>.Instance, new TestClock());
    }

    public LeasingService Leasing(Guid user, Guid? selected = null)
    {
        var context = new TestContext(user, selected ?? Account);
        var factory = new Factory(this);
        return new(factory, context, new TenantAuthorizationService(factory, context), null!,
            new TestClock(), NullLogger<LeasingService>.Instance);
    }

    public ReportCatalogService Catalog(Guid user, Guid? selected = null) =>
        new(Agreements(user, selected), Leasing(user, selected));

    public ReportLocalDateService LocalDate(Guid user, DateTimeOffset now, Guid? selected = null)
    {
        var context = new TestContext(user, selected ?? Account);
        return new(new Factory(this), context, new TestClock(now));
    }

    public ReportPageService Pages(Guid user, Guid? selected = null)
    {
        var context = new TestContext(user, selected ?? Account);
        var factory = new Factory(this);
        var authorization = new TenantAuthorizationService(factory, context);
        return new(factory, context, authorization,
            new ReportCatalogService(Agreements(user, selected), Leasing(user, selected)),
            new ReportDefinitionCompatibilityValidator(), new TestClock());
    }

    public ReportExecutionService Execution(Guid user, Guid? selected = null)
    {
        var context = new TestContext(user, selected ?? Account);
        var factory = new Factory(this);
        var authorization = new TenantAuthorizationService(factory, context);
        return new(authorization, Catalog(user, selected), new ReportDefinitionCompatibilityValidator(),
            new ReportLocalDateService(factory, context, new TestClock()),
            new AgreementReportModule(factory, context, authorization),
            new LeasingReportModule(factory, context, authorization));
    }

    public ReportTreeService Trees(Guid user, Guid? selected = null)
    {
        var context = new TestContext(user, selected ?? Account);
        var factory = new Factory(this);
        var authorization = new TenantAuthorizationService(factory, context);
        return new(authorization, new ReportCatalogService(Agreements(user, selected), Leasing(user, selected)),
            new ReportDefinitionCompatibilityValidator(), new ReportLocalDateService(factory, context, new TestClock()),
            new AgreementReportTreeModule(factory, context, authorization),
            new LeasingReportTreeModule(factory, context, authorization, new TestLocalizer()));
    }

    public async Task SetTimeZoneAsync(string timeZoneId)
    {
        await using var db = Open();
        var settings = await db.AgreementReminderSettings.SingleOrDefaultAsync(x => x.AccountId == Account);
        if (settings is null)
            db.AgreementReminderSettings.Add(new AgreementReminderSettings
                { AccountId = Account, TimeZoneId = timeZoneId, Revision = Guid.NewGuid() });
        else
            settings.TimeZoneId = timeZoneId;
        await db.SaveChangesAsync();
    }

    public DimensionListFilterSelection Dimension(Guid dimension, Guid value) =>
        new() { DimensionId = dimension, ValueId = value };

    public async Task SeedAsync()
    {
        await using var db = Open();
        db.Accounts.AddRange(
            new Account { Id = Account, Name = "Main", DefaultLanguage = "nb-NO" },
            new Account { Id = ForeignAccount, Name = "Foreign", DefaultLanguage = "nb-NO" });
        foreach (var (id, first, account, admin) in new[]
        {
            (Admin, "Admin", Account, true), (OwnerA, "Alice", Account, false),
            (OwnerB, "Bob", Account, false), (Reader, "Reader", Account, false),
            (LimitReader, "Limit", Account, false), (NoModuleAccess, "NoAccess", Account, false),
            (ForeignUser, "Foreign", ForeignAccount, true)
        })
        {
            db.Users.Add(new User { Id = id, Email = $"{id:N}@example.test", FirstName = first, LastName = "User", IsActive = true });
            var membership = new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = id };
            db.UserAccounts.Add(membership);
            if (admin) db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            if (id == LimitReader) db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.LeasingLimitApprover });
        }
        db.Organizations.AddRange(
            new Organization { Id = PartyA, AccountId = Account, Name = "Alpha partner" },
            new Organization { Id = PartyB, AccountId = Account, Name = "Beta partner" },
            new Organization { Id = ForeignParty, AccountId = ForeignAccount, Name = "Foreign partner" });

        db.Dimensions.AddRange(
            DimensionEntity(DimensionOne, Account, "GEO", "Geografi", true),
            DimensionEntity(DimensionTwo, Account, "DEP", "Avdeling", true),
            DimensionEntity(HistoricalDimension, Account, "OLD", "Historisk", false),
            DimensionEntity(ForeignDimension, ForeignAccount, "FOREIGN", "Foreign", true));
        db.DimensionValues.AddRange(
            Value(Root, Account, DimensionOne, null, "Norge"),
            Value(Child, Account, DimensionOne, Root, "Øst"),
            Value(Grandchild, Account, DimensionOne, Child, "Oslo"),
            Value(DepartmentA, Account, DimensionTwo, null, "A"),
            Value(DepartmentB, Account, DimensionTwo, null, "B"),
            Value(HistoricalValue, Account, HistoricalDimension, null, "Utgått", false),
            Value(ForeignValue, ForeignAccount, ForeignDimension, null, "Foreign"));
        db.AgreementDimensionRules.AddRange(
            AgreementRule(DimensionOne, true), AgreementRule(DimensionTwo, true), AgreementRule(HistoricalDimension, false));
        db.LeasingAgreementDimensionRules.AddRange(
            LeasingRule(DimensionOne, true), LeasingRule(DimensionTwo, true), LeasingRule(HistoricalDimension, false));

        var alpha = AgreementEntity(AgreementAlpha, Account, "Alpha contract", PartyA, OwnerA,
            AgreementStatus.Active, AgreementType.Lease, false, new(2026, 11, 20));
        alpha.Direction = AgreementDirection.Income; alpha.Currency = "NOK";
        var archived = AgreementEntity(AgreementArchived, Account, "Archived contract", PartyA, OwnerA,
            AgreementStatus.Active, AgreementType.Lease, true, new(2026, 11, 21));
        var beta = AgreementEntity(AgreementBeta, Account, "Beta contract", PartyB, OwnerA,
            AgreementStatus.Draft, AgreementType.Supplier);
        beta.Direction = AgreementDirection.Income; beta.Currency = "EUR";
        var gamma = AgreementEntity(AgreementGamma, Account, "Gamma contract", PartyA, OwnerB,
            AgreementStatus.Active, AgreementType.Other);
        gamma.Direction = AgreementDirection.Cost; gamma.Currency = "GBP";
        db.Agreements.AddRange(alpha, archived, beta, gamma,
            AgreementEntity(Guid.NewGuid(), ForeignAccount, "Foreign contract", ForeignParty, ForeignUser,
                AgreementStatus.Active, AgreementType.Other));
        AssignAgreement(db, AgreementAlpha, DimensionOne, Child);
        AssignAgreement(db, AgreementAlpha, DimensionOne, Root);
        AssignAgreement(db, AgreementAlpha, DimensionTwo, DepartmentA);
        AssignAgreement(db, AgreementAlpha, HistoricalDimension, HistoricalValue);
        AssignAgreement(db, AgreementBeta, DimensionOne, Grandchild);
        AssignAgreement(db, AgreementBeta, DimensionTwo, DepartmentB);
        AssignAgreement(db, AgreementGamma, DimensionTwo, DepartmentA);
        db.AgreementAccess.AddRange(
            new AgreementAccess { Id = Guid.NewGuid(), AccountId = Account, AgreementId = AgreementAlpha, UserId = Reader, Level = AgreementAccessLevel.Read },
            new AgreementAccess { Id = Guid.NewGuid(), AccountId = Account, AgreementId = AgreementBeta, UserId = Reader, Level = AgreementAccessLevel.Read });
        SeedAgreementFinancials(db, alpha, beta, gamma);

        var frameworkAlpha = Framework(FrameworkAlpha, Account, "Alpha framework", "FRAME-1", OwnerA,
            LeasingFrameworkStatus.Open);
        frameworkAlpha.LimitNet = 1_000; frameworkAlpha.LimitVat = 250;
        var frameworkHidden = Framework(FrameworkHidden, Account, "Hidden framework", "FRAME-2", OwnerB,
            LeasingFrameworkStatus.Closed);
        frameworkHidden.Currency = "EUR"; frameworkHidden.LimitNet = 2_000; frameworkHidden.LimitVat = 400;
        db.LeasingFrameworks.AddRange(
            frameworkAlpha, frameworkHidden,
            Framework(Guid.NewGuid(), ForeignAccount, "Foreign framework", "FOREIGN", ForeignUser, LeasingFrameworkStatus.Open));
        var childAcquisition = Acquisition(AcquisitionChild, Account, FrameworkAlpha, "Child acquisition",
            "CHILD-REF", OwnerB, LeasingAcquisitionStatus.Registered);
        PurchaseValues(childAcquisition, 100, 25, 10, 2.5m, 5, 1.25m, 8, 2);
        var standaloneAcquisition = Acquisition(AcquisitionStandalone, Account, null, "Standalone acquisition",
            "STANDALONE-REF", OwnerA, LeasingAcquisitionStatus.Registered);
        standaloneAcquisition.Currency = "EUR";
        PurchaseValues(standaloneAcquisition, 200, 40, 20, 4, 0, 0, 0, 0);
        var hiddenAcquisition = Acquisition(AcquisitionHidden, Account, null, "Hidden acquisition",
            "HIDDEN-REF", OwnerB, LeasingAcquisitionStatus.Draft);
        PurchaseValues(hiddenAcquisition, 300, 75, 0, 0, 0, 0, 0, 0);
        db.LeasingAcquisitions.AddRange(
            childAcquisition, standaloneAcquisition, hiddenAcquisition,
            Acquisition(Guid.NewGuid(), ForeignAccount, null, "Foreign acquisition", "FOREIGN", ForeignUser, LeasingAcquisitionStatus.Registered));
        db.LeasingItems.AddRange(
            new LeasingItem { Id = ChildItem, AccountId = Account, AcquisitionId = AcquisitionChild,
                Position = 1, Description = "Shared item name", ItemNumber = "ITEM-CHILD", Quantity = 5 },
            new LeasingItem { Id = StandaloneItem, AccountId = Account, AcquisitionId = AcquisitionStandalone,
                Position = 1, Description = "Shared item name", ItemNumber = "ITEM-STANDALONE", Quantity = 12 });
        db.LeasingEquipment.AddRange(
            new LeasingEquipment { Id = ChildEquipment, AccountId = Account, AcquisitionId = AcquisitionChild,
                ItemId = ChildItem, Description = "Child device", SerialNumber = "SERIAL-HIDDEN-BRANCH",
                RegisteredDate = new(2026, 1, 2), Revision = Guid.NewGuid() },
            new LeasingEquipment { Id = StandaloneEquipment, AccountId = Account, AcquisitionId = AcquisitionStandalone,
                ItemId = StandaloneItem, Description = "Standalone device", InternalId = "INTERNAL-STANDALONE",
                RegisteredDate = new(2026, 1, 3), Revision = Guid.NewGuid() });
        AssignFramework(db, FrameworkAlpha, DimensionOne, Child);
        AssignFramework(db, FrameworkAlpha, DimensionTwo, DepartmentA);
        AssignFramework(db, FrameworkAlpha, HistoricalDimension, HistoricalValue);
        AssignAcquisition(db, AcquisitionChild, DimensionOne, Child);
        AssignAcquisition(db, AcquisitionChild, DimensionOne, Root);
        AssignAcquisition(db, AcquisitionChild, DimensionTwo, DepartmentA);
        AssignAcquisition(db, AcquisitionStandalone, DimensionOne, Grandchild);
        AssignAcquisition(db, AcquisitionStandalone, DimensionTwo, DepartmentB);
        await db.SaveChangesAsync();
    }

    private Dimension DimensionEntity(Guid id, Guid account, string code, string name, bool active) => new()
    {
        Id = id, AccountId = account, Code = code, Name = name, IsActive = active,
        AllowMultiple = id == DimensionOne, Revision = Guid.NewGuid(),
        Translations =
        [
            new() { Id = Guid.NewGuid(), AccountId = account, DimensionId = id, LanguageCode = "nb-NO", Name = name },
            new() { Id = Guid.NewGuid(), AccountId = account, DimensionId = id, LanguageCode = "en-GB", Name = name + " EN" },
            new() { Id = Guid.NewGuid(), AccountId = account, DimensionId = id, LanguageCode = "sv-SE", Name = name + " SV" }
        ]
    };

    private static DimensionValue Value(Guid id, Guid account, Guid dimension, Guid? parent, string name, bool active = true) =>
        new() { Id = id, AccountId = account, DimensionId = dimension, ParentId = parent, Name = name, Code = id.ToString("N"), IsActive = active, Revision = Guid.NewGuid() };
    private AgreementDimensionRule AgreementRule(Guid dimension, bool enabled) =>
        new() { AccountId = Account, DimensionId = dimension, IsEnabled = enabled, Revision = Guid.NewGuid() };
    private LeasingAgreementDimensionRule LeasingRule(Guid dimension, bool enabled) =>
        new() { AccountId = Account, DimensionId = dimension, IsEnabled = enabled, Revision = Guid.NewGuid() };
    private static Agreement AgreementEntity(Guid id, Guid account, string title, Guid party, Guid owner,
        AgreementStatus status, AgreementType type, bool archived = false, DateOnly? notice = null) => new()
    {
        Id = id, AccountId = account, Title = title, CounterpartyOrganizationId = party, OwnerUserId = owner,
        Status = status, Type = type, StartDate = new(2026, 1, 1), CurrentPeriodStartDate = new(2026, 1, 1),
        EndDate = new(2027, 1, 1), NoticeDeadline = notice, IsArchived = archived,
        CreatedByUserId = owner, UpdatedByUserId = owner, Revision = Guid.NewGuid()
    };
    private void AssignAgreement(TenantPlatformDbContext db, Guid agreement, Guid dimension, Guid value) =>
        db.AgreementDimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = Account, AgreementId = agreement, DimensionId = dimension, ValueId = value });

    private void SeedAgreementFinancials(TenantPlatformDbContext db, Agreement alpha, Agreement beta, Agreement gamma)
    {
        var indexId = Guid.NewGuid();
        alpha.IndexId = indexId;
        db.AgreementIndexRecords.Add(new AgreementIndex
        {
            Id = indexId, AccountId = Account, Code = "CPI", Name = "CPI", Description = "Fixture",
            Source = "Fixture", Resolution = AgreementIndexResolution.Year, ActorUserId = Admin
        });
        db.AgreementIndexSelections.Add(new AgreementIndexSelection
        {
            Id = Guid.NewGuid(), AccountId = Account, AgreementId = AgreementAlpha, IndexId = indexId,
            Sequence = 1, EffectiveFrom = new(2026, 1, 1), ActorUserId = Admin
        });
        db.AgreementIndexValueRecords.AddRange(
            IndexValue(indexId, new(2026, 1, 1), 100), IndexValue(indexId, new(2026, 7, 15), 110));

        db.AgreementLines.AddRange(
            new AgreementLine { Id = AlphaLine, AccountId = Account, AgreementId = AgreementAlpha, ActivatedUtc = DateTimeOffset.UtcNow },
            new AgreementLine { Id = BetaLine, AccountId = Account, AgreementId = AgreementBeta, ActivatedUtc = DateTimeOffset.UtcNow },
            new AgreementLine { Id = GammaLine, AccountId = Account, AgreementId = AgreementGamma, ActivatedUtc = DateTimeOffset.UtcNow });
        db.AgreementLineVersions.AddRange(
            LineVersion(AgreementAlpha, AlphaLine, 1, new(2026, 1, 1), new(2027, 1, 31),
                new(2026, 2, 1), AgreementFrequency.Monthly, AgreementBillingTiming.Advance, true),
            LineVersion(AgreementAlpha, AlphaLine, 2, new(2026, 7, 15), new(2027, 1, 31),
                new(2026, 2, 1), AgreementFrequency.Monthly, AgreementBillingTiming.Advance, true),
            LineVersion(AgreementBeta, BetaLine, 1, new(2026, 1, 1), new(2026, 12, 31),
                new(2026, 1, 1), AgreementFrequency.Yearly, AgreementBillingTiming.Arrears, false),
            LineVersion(AgreementGamma, GammaLine, 1, new(2026, 1, 1), new(2026, 3, 31),
                new(2026, 1, 1), AgreementFrequency.Monthly, AgreementBillingTiming.Arrears, false));
        db.AgreementPriceVersions.AddRange(
            Price(AgreementAlpha, AlphaLine, 1, new(2026, 1, 1), 100, new(2026, 1, 1)),
            Price(AgreementAlpha, AlphaLine, 2, new(2026, 7, 15), 100, new(2026, 1, 1)),
            Price(AgreementBeta, BetaLine, 1, new(2026, 1, 1), 1200),
            Price(AgreementGamma, GammaLine, 1, new(2026, 1, 1), 50));

        AgreementIndexValue IndexValue(Guid index, DateOnly period, decimal value) => new()
        {
            Id = Guid.NewGuid(), AccountId = Account, IndexId = index, Period = period,
            PeriodKey = Guid.NewGuid(), Revision = 1, Value = value, ActorUserId = Admin, Reason = "Fixture"
        };
        AgreementLineVersion LineVersion(Guid agreement, Guid line, int sequence, DateOnly effective,
            DateOnly end, DateOnly payable, AgreementFrequency frequency, AgreementBillingTiming timing,
            bool indexed) => new()
        {
            Id = Guid.NewGuid(), AccountId = Account, AgreementId = agreement, LineId = line,
            Sequence = sequence, EffectiveFrom = effective, ActorUserId = Admin, Name = "Fixture line",
            StartDate = new(2026, 1, 1), EndDate = end, FirstPayableDate = payable,
            Frequency = frequency, Anchor = AgreementAnchor.Calendar, AnchorDate = new(1, 1, 1),
            BillingTiming = timing, Status = AgreementLineStatus.Active, IndexRegulated = indexed
        };
        AgreementPriceVersion Price(Guid agreement, Guid line, int sequence, DateOnly effective,
            decimal unitPrice, DateOnly? indexBase = null) => new()
        {
            Id = Guid.NewGuid(), AccountId = Account, AgreementId = agreement, LineId = line,
            Sequence = sequence, EffectiveFrom = effective, IndexBaseDate = indexBase,
            Quantity = 1, UnitPrice = unitPrice, ActorUserId = Admin
        };
    }
    private LeasingFramework Framework(Guid id, Guid account, string name, string number, Guid owner, LeasingFrameworkStatus status) => new()
    {
        Id = id, AccountId = account, Name = name, Number = number, OwnerUserId = owner,
        FinanceOrganizationId = account == Account ? PartyA : ForeignParty,
        AcquisitionFrom = new(2026, 1, 1), AcquisitionTo = new(2027, 1, 1), Currency = "NOK", Status = status, Revision = Guid.NewGuid()
    };
    private LeasingAcquisition Acquisition(Guid id, Guid account, Guid? framework, string name, string reference,
        Guid owner, LeasingAcquisitionStatus status) => new()
    {
        Id = id, AccountId = account, FrameworkId = framework, Name = name, Reference = reference, OwnerUserId = owner,
        SupplierOrganizationId = account == Account ? PartyA : ForeignParty,
        FinanceOrganizationId = account == Account ? PartyA : ForeignParty,
        PurchaseDate = new(2026, 1, 1), EndDate = new(2027, 1, 1), Currency = "NOK", Status = status, Revision = Guid.NewGuid()
    };
    private static void PurchaseValues(LeasingAcquisition acquisition, decimal net, decimal vat,
        decimal creditNet, decimal creditVat, decimal reversedNet, decimal reversedVat,
        decimal releasedNet, decimal releasedVat)
    {
        var totals = LeasingCalculator.Total(
            [new LeasingItem { Quantity = 1, UnitPrice = net, VatPercent = vat / net * 100m }]);
        acquisition.NetTotal = totals.Net; acquisition.VatTotal = totals.Vat;
        acquisition.GrossTotal = totals.Gross;
        acquisition.CreditNetTotal = creditNet; acquisition.CreditVatTotal = creditVat;
        acquisition.ReversedNetTotal = reversedNet; acquisition.ReversedVatTotal = reversedVat;
        acquisition.ReleasedNetTotal = releasedNet; acquisition.ReleasedVatTotal = releasedVat;
    }
    private void AssignFramework(TenantPlatformDbContext db, Guid framework, Guid dimension, Guid value) =>
        db.LeasingFrameworkDimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = Account, FrameworkId = framework,
            DimensionId = dimension, ValueId = value, DimensionName = dimension.ToString(), DimensionCode = dimension.ToString(),
            ValueName = value.ToString(), ValueCode = value.ToString(), ValuePath = value.ToString() });
    private void AssignAcquisition(TenantPlatformDbContext db, Guid acquisition, Guid dimension, Guid value) =>
        db.LeasingAcquisitionDimensionAssignments.Add(new() { Id = Guid.NewGuid(), AccountId = Account, AcquisitionId = acquisition,
            DimensionId = dimension, ValueId = value, DimensionName = dimension.ToString(), DimensionCode = dimension.ToString(),
            ValueName = value.ToString(), ValueCode = value.ToString(), ValuePath = value.ToString() });

    private sealed class Factory(Fixture fixture) : IDbContextFactory<TenantPlatformDbContext>
    {
        public TenantPlatformDbContext CreateDbContext() => fixture.Open();
    }
    private sealed class TestContext(Guid user, Guid account) : ICurrentUserContextService
    {
        public CurrentUserContext Current { get; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account };
    }
    private sealed class TestClock(DateTimeOffset? now = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now ?? new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class TestLocalizer : IStringLocalizer<TenantPlatformResources>
    {
        public LocalizedString this[string name] => new(name, name == "ReportTreeWithoutFramework" ? "Uten rammeavtale" : name);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(CultureInfo.InvariantCulture, this[name].Value, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

static class Check
{
    public static int Count { get; private set; }
    public static void That(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        Count++;
        Console.WriteLine("PASS: " + message);
    }
    public static void Sequence(IEnumerable<Guid> actual, IEnumerable<Guid> expected, string message) =>
        That(actual.SequenceEqual(expected), message + $" [actual: {string.Join(",", actual)}]");
    public static async Task ThrowsAsync<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { That(true, message); return; }
        throw new Exception($"FAIL: {message} (expected {typeof(T).Name})");
    }
    public static void Throws<T>(Action action, string message, string? resourceKey = null) where T : Exception
    {
        try { action(); }
        catch (T exception)
        {
            That(resourceKey is null || exception.Message == resourceKey,
                message + (resourceKey is null ? "" : $" [expected {resourceKey}, actual {exception.Message}]"));
            return;
        }
        throw new Exception($"FAIL: {message} (expected {typeof(T).Name})");
    }
}
