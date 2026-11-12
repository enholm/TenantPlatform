using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Auditing;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Infrastructure.Auditing;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

static class ServiceChecks
{
    // Exercise the production tracked-graph mutation inside an InMemory context.
    // Reflection bypasses only the relational transaction/account row lock, which
    // InMemory cannot implement. Public read/auth paths are called normally.
    // These checks are NOT proof of PostgreSQL atomicity, locks or constraints.
    private static void Apply(DimensionService service, TenantPlatformDbContext db, Guid account, Dimension entity, Dimension input, bool exists = true)
    {
        try { typeof(DimensionService).GetMethod("ApplyDimensionChanges", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, [db, account, entity, input, exists]); }
        catch (TargetInvocationException e) when (e.InnerException is not null) { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
    }
    private static Dimension Copy(Dimension d) => JsonSerializer.Deserialize<Dimension>(JsonSerializer.Serialize(d))!;
    public static async Task Run()
    {
        var account = Guid.NewGuid(); var other = Guid.NewGuid(); var adminId = Guid.NewGuid(); var memberId = Guid.NewGuid();
        var dbOptions = new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(new AuditUser(adminId, account))).Options;
        var factory = new Factory(dbOptions);
        var adminContext = new Context(adminId, account);
        var admin = new DimensionService(factory, adminContext, new TenantAuthorizationService(factory, adminContext), new Clock());
        var memberContext = new Context(memberId, account);
        var member = new DimensionService(factory, memberContext, new TenantAuthorizationService(factory, memberContext), new Clock());
        var dimension = LocalizationChecks.Example(); dimension.AccountId = account;
        foreach (var t in dimension.Translations) t.AccountId = account;
        dimension.Revision = Guid.NewGuid();
        var foreign = LocalizationChecks.Example(); foreign.AccountId = other;
        foreach (var t in foreign.Translations) t.AccountId = other;
        var value = new DimensionValue { Id = Guid.NewGuid(), AccountId = account, DimensionId = dimension.Id, Name = "Value without translation", Code = "VALUE" };
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "A", DefaultLanguage = "nb-NO" }, new Account { Id = other, Name = "B", DefaultLanguage = "en-GB" });
            db.Users.AddRange(new User { Id = adminId, Email = "admin@example.test", IsActive = true }, new User { Id = memberId, Email = "member@example.test", IsActive = true });
            var adminMembership = new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = adminId };
            db.UserAccounts.AddRange(adminMembership, new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = memberId });
            db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = adminMembership.Id, Role = UserRole.AccountAdmin });
            db.Dimensions.AddRange(dimension, foreign); db.DimensionValues.Add(value);
            db.LeasingAcquisitions.Add(new LeasingAcquisition { Id = Guid.NewGuid(), AccountId = account, OwnerUserId = memberId, Name = "Owned" });
            db.LeasingDimensionRules.Add(new LeasingDimensionRule { AccountId = account, DimensionId = dimension.Id, IsEnabled = true, IsRequired = true });
            await db.SaveChangesAsync();
        }
        var register = await admin.GetAsync();
        Verify.That(register.Dimensions.Count == 1 && register.Dimensions.Single().Translations.Count == 3 && register.Values.Single().Id == value.Id,
            "administration reads only selected account and its translations/values");
        Verify.That(register.DefaultLanguage == "nb-NO" && register.DisplayName(register.Dimensions.Single()) == "Norsk navn", "administration read model resolves localized text with account language");
        await Verify.Throws<UnauthorizedAccessException>(() => member.GetAsync(), "ordinary member cannot read administrative register");
        await Verify.Throws<UnauthorizedAccessException>(() => member.SaveDimensionAsync(dimension.Id, Copy(dimension)), "ordinary member cannot save translations (rejected before any relational operation)");
        var wrongContext = new Context(adminId, other);
        var wrongAccount = new DimensionService(factory, wrongContext, new TenantAuthorizationService(factory, wrongContext), new Clock());
        await Verify.Throws<UnauthorizedAccessException>(() => wrongAccount.GetAsync(), "cached selected account without membership cannot read translations");
        var platformContext = new Context(memberId, account, true);
        var platform = new DimensionService(factory, platformContext, new TenantAuthorizationService(factory, platformContext), new Clock());
        await Verify.Throws<UnauthorizedAccessException>(() => platform.GetAsync(), "platform admin claim alone grants no register administration");

        var leasing = new LeasingService(factory, memberContext, new TenantAuthorizationService(factory, memberContext), null!, new Clock(), NullLogger<LeasingService>.Instance);
        var catalog = await leasing.ClassificationCatalogAsync(account);
        Verify.That(!catalog.CanManage && catalog.Dimensions.Count == 1 && catalog.Dimensions.Single().DisplayName == "Norsk navn",
            "ordinary authorized Leasing owner reads localized catalog without new administrative permissions");
        Verify.That(catalog.Dimensions.Single().Dimension.Name == "Stored name" && catalog.Dimensions.Single().Values.Single().Name == value.Name,
            "Leasing catalog preserves fallback name and dimension value names");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");
        Verify.That(catalog.Dimensions.Single().DisplayName == "English name" && catalog.Dimensions.Single().Dimension.Id == dimension.Id,
            "language switch updates catalog display without changing identity or fallback");
        Verify.That(LeasingService.MissingDimensions(new LeasingItem(), catalog).SequenceEqual(["English name"]), "missing-dimension notices use localized display names");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        await Verify.Throws<UnauthorizedAccessException>(() => leasing.ClassificationCatalogAsync(other), "Leasing catalog rejects a different selected account");

        await using (var db = factory.CreateDbContext())
        {
            var tracked = await db.Dimensions.Include(x => x.Translations).SingleAsync(x => x.Id == dimension.Id && x.AccountId == account);
            var englishId = tracked.Translations.Single(x => x.LanguageCode == "en-GB").Id;
            var patch = Copy(tracked); patch.Translations = [new DimensionTranslation { LanguageCode = " nb-no ", Name = "Nytt norsk navn", Description = " " }];
            var oldRevision = tracked.Revision;
            Apply(admin, db, account, tracked, patch);
            await db.SaveChangesAsync();
            Verify.That(tracked.Revision != oldRevision && tracked.Translations.Single(x => x.LanguageCode == "nb-NO").Name == "Nytt norsk navn" &&
                tracked.Translations.Single(x => x.LanguageCode == "nb-NO").Description is null, "translation edit normalizes supported write language and advances parent revision");
            Verify.That(tracked.Translations.Count == 3 && tracked.Translations.Single(x => x.LanguageCode == "en-GB").Id == englishId &&
                tracked.Translations.Single(x => x.LanguageCode == "en-GB").Name == "English name",
                "omitted languages and stable translation identities are preserved");
            Verify.That(DimensionLocalization.Description(tracked, "nb", null) == "Stored description", "clearing one field restores its fallback without removing other field");
            var history = await db.DimensionHistory.SingleAsync(x => x.DimensionId == tracked.Id);
            using var before = JsonDocument.Parse(history.BeforeJson); using var after = JsonDocument.Parse(history.AfterJson);
            Verify.That(history.AccountId == account && history.ActorUserId == adminId && history.RecordedUtc == new Clock().GetUtcNow() &&
                before.RootElement.GetProperty("Translations").EnumerateArray().Any(x => x.GetProperty("Name").GetString() == "Norsk navn") &&
                after.RootElement.GetProperty("Translations").EnumerateArray().Any(x => x.GetProperty("Name").GetString() == "Nytt norsk navn"), "DimensionHistory records translation before/after with correct account, actor and time");
            Verify.That(await db.AuditLogs.AnyAsync(x => x.AccountId == account && x.EntityType == nameof(DimensionTranslation) && x.Action == "Modified" && x.ChangesJson != null && x.ChangesJson.Contains("Nytt norsk navn")),
                "production audit interceptor records translation changes");
            var count = db.DimensionHistory.Count();
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, patch); return Task.CompletedTask; }, "stale parent revision rejects translation edits");
            Verify.That(db.DimensionHistory.Count() == count && tracked.Translations.Single(x => x.LanguageCode == "nb-NO").Name == "Nytt norsk navn", "stale edit changes neither translation nor history");
            var invalid = Copy(tracked); invalid.Translations = [new() { LanguageCode = "xx-XX", Name = "Reject" }];
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "unknown write language is rejected instead of silently becoming English");
            invalid.Translations = [new() { LanguageCode = "en", Name = "Reject alias on write" }];
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "writes require canonical supported codes, as existing translation editors do");
            invalid.Translations = [new() { LanguageCode = "en-GB", Name = "One" }, new() { LanguageCode = " EN-gb ", Name = "Two" }];
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "duplicate languages are rejected after canonical normalization");
            invalid.Translations = [new() { LanguageCode = "en-GB", Name = new string('x', 201) }];
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "translation name retains 200 character limit");
            invalid.Translations = [new() { LanguageCode = "en-GB", Description = new string('x', 2001) }];
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "translation description retains 2000 character limit");
            invalid = Copy(tracked); invalid.Name = " ";
            await Verify.Throws<DimensionValidationException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "fallback name remains mandatory even with translations");
            foreach (var bad in new[] {
                new DimensionTranslation { AccountId = other, LanguageCode = "en-GB", Name = "Foreign" },
                new DimensionTranslation { DimensionId = foreign.Id, LanguageCode = "en-GB", Name = "Foreign" },
                new DimensionTranslation { Id = foreign.Translations.First().Id, LanguageCode = "en-GB", Name = "Foreign" } })
            {
                invalid = Copy(tracked); invalid.Translations = [bad];
                await Verify.Throws<UnauthorizedAccessException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "forged translation account/dimension/row identity rejected");
            }
            invalid = Copy(tracked); invalid.AccountId = other;
            await Verify.Throws<UnauthorizedAccessException>(() => { Apply(admin, db, account, tracked, invalid); return Task.CompletedTask; }, "forged parent account rejected");
            await Verify.Throws<UnauthorizedAccessException>(() => { Apply(admin, db, other, tracked, Copy(tracked)); return Task.CompletedTask; }, "mutation cannot target another account's tracked dimension");
            var clear = Copy(tracked); clear.Translations = [new() { LanguageCode = "sv-SE", Name = " ", Description = " " }];
            Apply(admin, db, account, tracked, clear); await db.SaveChangesAsync();
            Verify.That(tracked.Translations.Count == 2 && !await db.DimensionTranslations.AnyAsync(x => x.AccountId == account && x.DimensionId == tracked.Id && x.LanguageCode == "sv-SE"), "explicitly clearing both fields removes just that translation");
            var descriptionOnly = Copy(tracked); descriptionOnly.Translations = [new() { LanguageCode = "sv-SE", Description = "Endast beskrivning" }];
            Apply(admin, db, account, tracked, descriptionOnly); await db.SaveChangesAsync();
            Verify.That(DimensionLocalization.Name(tracked, "sv-SE", null) == tracked.Name && DimensionLocalization.Description(tracked, "sv-SE", null) == "Endast beskrivning", "description-only translations can be added and read independently");
        }
        await using (var first = factory.CreateDbContext())
        await using (var stale = factory.CreateDbContext())
        {
            var freshEntity = await first.Dimensions.Include(x => x.Translations).SingleAsync(x => x.Id == dimension.Id);
            var staleEntity = await stale.Dimensions.Include(x => x.Translations).SingleAsync(x => x.Id == dimension.Id);
            var freshInput = Copy(freshEntity); freshInput.Translations = [new() { LanguageCode = "en-GB", Name = "Winner" }];
            Apply(admin, first, account, freshEntity, freshInput); await first.SaveChangesAsync();
            var staleInput = Copy(staleEntity); staleInput.Translations = [new() { LanguageCode = "en-GB", Name = "Loser" }];
            Apply(admin, stale, account, staleEntity, staleInput);
            await Verify.Throws<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync(), "EF parent concurrency token detects separately tracked stale writer (InMemory only)");
        }
        // New parent + language rows use server-owned identities; no fake DB locks.
        await using (var db = factory.CreateDbContext())
        {
            var created = new Dimension { Id = Guid.NewGuid(), AccountId = account };
            var input = new Dimension { Name = "Created", Code = "NEW", Translations = [new() { LanguageCode = "en-GB", Name = "New translated" }] };
            Apply(admin, db, account, created, input, false); db.Dimensions.Add(created); await db.SaveChangesAsync();
            Verify.That(created.Translations.Single().Id != Guid.Empty && created.Translations.Single().AccountId == account && created.Translations.Single().DimensionId == created.Id,
                "creation binds translation identities to server-owned account and dimension");
        }
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == adminId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await Verify.Throws<UnauthorizedAccessException>(() => admin.GetAsync(), "revoked active-user membership is rechecked despite cached authenticated context");
    }
    private sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
    { public TenantPlatformDbContext CreateDbContext() => new(options); }
    private sealed class Context(Guid user, Guid account, bool platformAdmin = false) : ICurrentUserContextService
    { public CurrentUserContext Current { get; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account, IsPlatformAdmin = platformAdmin }; }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero); }
    private sealed class AuditUser(Guid user, Guid account) : IAuditUserContext
    { public Guid? UserId => user; public Guid? AccountId => account; public string? Email => "admin@example.test"; }
}
