using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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

static class DetailsChecks
{
    public static async Task Run(Factory factory, ReadOnlyGuard guard, string provider)
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL details: " + name); Console.WriteLine($"PASS {provider} details: {name}"); }
        var account = Guid.NewGuid(); var foreignAccount = Guid.NewGuid(); var owner = Guid.NewGuid(); var reader = Guid.NewGuid();
        var editor = Guid.NewGuid(); var admin = Guid.NewGuid(); var stranger = Guid.NewGuid(); var foreign = Guid.NewGuid(); var platform = Guid.NewGuid();
        var party = Guid.NewGuid(); var foreignParty = Guid.NewGuid(); var member = Guid.NewGuid();
        var foreignBuilding = Guid.NewGuid(); var foreignUnit = Guid.NewGuid();
        var areaRoot = Guid.NewGuid(); var area = Guid.NewGuid(); var element = Guid.NewGuid();
        var clock = new Clock(new(2026, 10, 17, 10, 0, 0, TimeSpan.Zero));
        Agreement Make(string title, Guid? tenant = null) => new()
        {
            Id = Guid.NewGuid(), AccountId = tenant ?? account, Title = title, OwnerUserId = tenant == foreignAccount ? foreign : owner,
            CounterpartyOrganizationId = tenant == foreignAccount ? foreignParty : party, Type = AgreementType.Other, Status = AgreementStatus.Active,
            StartDate = new(2020, 1, 1), CurrentPeriodStartDate = new(2026, 1, 1), EndDate = new(2027, 12, 31),
            Form = AgreementForm.Renewing, NoticeMode = AgreementNoticeMode.BeforeRenewal, NoticeCount = 1, NoticeUnit = AgreementNoticeUnit.Months,
            RenewalDate = new(2027, 3, 31), CreatedUtc = clock.GetUtcNow(), UpdatedUtc = clock.GetUtcNow(),
            CreatedByUserId = foreign, UpdatedByUserId = foreign, Revision = Guid.NewGuid()
        };
        var target = Make("Details target"); target.OrganizationElementId = element; target.GeographicAreaId = area;
        var unrelated = Make("Unrelated"); unrelated.OwnerUserId = stranger;
        var foreignAgreement = Make("Foreign", foreignAccount);
        var snapshot = AgreementNoticeSnapshot.From(target);
        await using (var db = factory.CreateDbContext())
        {
            db.Accounts.AddRange(new Account { Id = account, Name = "Details A" }, new Account { Id = foreignAccount, Name = "Details B" });
            foreach (var id in new[] { owner, reader, editor, admin, stranger, foreign, platform })
            {
                db.Users.Add(new User { Id = id, Email = id + "@example.test", FirstName = id == foreign ? "Foreign secret" : "Details", LastName = id.ToString(), IsActive = true, IsPlatformAdmin = id == platform });
                var membership = new UserAccount { Id = id == reader ? member : Guid.NewGuid(), UserId = id, AccountId = id == foreign ? foreignAccount : account };
                db.UserAccounts.Add(membership);
                if (id == admin) db.UserAccountRoles.Add(new() { Id = Guid.NewGuid(), UserAccountId = membership.Id, Role = UserRole.AccountAdmin });
            }
            db.Organizations.AddRange(new Organization { Id = party, AccountId = account, Name = "Local party" }, new Organization { Id = foreignParty, AccountId = foreignAccount, Name = "Foreign party secret" });
            db.GeographicAreas.AddRange(new GeographicArea { Id = areaRoot, AccountId = account, Name = "Root" }, new GeographicArea { Id = area, AccountId = account, ParentId = areaRoot, Name = "Leaf", IsActive = false });
            db.OrganizationElements.Add(new() { Id = element, AccountId = account, Name = "Organization", IsActive = false });
            db.Buildings.Add(new() { Id = foreignBuilding, AccountId = foreignAccount, Name = "Foreign building secret" });
            db.Units.Add(new() { Id = foreignUnit, AccountId = foreignAccount, BuildingId = foreignBuilding, Name = "Foreign unit secret" });
            db.Agreements.AddRange(target, unrelated, foreignAgreement);
            foreach (var (id, level) in new[] { (reader, AgreementAccessLevel.Read), (editor, AgreementAccessLevel.Edit), (foreign, AgreementAccessLevel.Read) })
                db.AgreementAccess.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = target.Id, UserId = id, Level = level });
            AgreementDocument Doc(Agreement agreement, string name, Guid uploader) => new()
            {
                Id = Guid.NewGuid(), AccountId = agreement.AccountId, AgreementId = agreement.Id, OriginalFileName = name,
                StorageKey = Guid.NewGuid().ToString("N"), MediaType = "application/pdf", Size = 100, Category = AgreementDocumentCategory.Contract,
                UploadedUtc = clock.GetUtcNow(), UploadedByUserId = uploader
            };
            db.AgreementDocuments.AddRange(Doc(target, "local.pdf", owner), Doc(target, "departed-uploader.pdf", foreign), Doc(unrelated, "hidden.pdf", stranger), Doc(foreignAgreement, "foreign.pdf", foreign));
            db.AgreementNoticeHistory.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = target.Id, ActorUserId = foreign,
                Action = AgreementNoticeAction.RuleChanged, CreatedUtc = clock.GetUtcNow(), BeforeJson = JsonSerializer.Serialize(snapshot), AfterJson = JsonSerializer.Serialize(snapshot) });
            await db.SaveChangesAsync();
        }
        var actor = new Actor(new(true, owner, account)); var auth = new AuthorizationService(actor, new AuthorizationData(factory), clock);
        var service = new AgreementDetailsReader(auth, new AgreementDetailsData(factory));
        async Task<AgreementDetailsDto> Read(Guid? id = null)
        {
            guard.Protect = true;
            try { return await service.ReadAsync(account, id ?? target.Id); }
            finally { guard.Protect = false; }
        }
        async Task Denied(Func<Task> action, string name)
        {
            try { await action(); throw new Exception("Expected denial: " + name); }
            catch (UnauthorizedAccessException) { Check(true, name); }
        }
        var stored = await Read();
        Check(stored.Id == target.Id && stored.CanEdit && stored.CanManageAccess && !stored.DeadlinesInitialized && stored.NoticeDeadline is null && stored.NoticeSnapshot == snapshot, "direct Application returns stored details without lazy recalculation or initialization");
        Check(stored.CounterpartyName == "Local party" && stored.OwnerName.StartsWith("Details") && stored.CreatedByName == "" && stored.UpdatedByName == "" && stored.GeographicAreaName == "Root / Leaf" && stored.OrganizationElementName == "Organization", "metadata restricted to account; inactive structure paths preserved");
        Check(stored.Documents.Count == 2 && !stored.Documents.Any(x => x.FileName is "foreign.pdf" or "hidden.pdf") && stored.Documents.Single(x => x.FileName == "departed-uploader.pdf").UploadedByName == "" && stored.NoticeHistory.Single().Actor == "", "document/history metadata stays on selected account/agreement without foreign user labels");
        Check(stored.BuildingName is null && stored.UnitName is null && stored.IndexName is null && stored.Terms is null, "missing optional metadata remains absent");
        await using (var db = factory.CreateDbContext())
        {
            var row = await db.Agreements.SingleAsync(x => x.Id == target.Id);
            row.CounterpartyOrganizationId = foreignParty; row.BuildingId = foreignBuilding; row.UnitId = foreignUnit; await db.SaveChangesAsync();
        }
        var restricted = await Read();
        Check(restricted.CounterpartyName == "" && restricted.BuildingName is null && restricted.UnitName is null, "legacy foreign references cannot expose counterparty/building/unit labels");
        await using (var db = factory.CreateDbContext())
        {
            var row = await db.Agreements.SingleAsync(x => x.Id == target.Id);
            row.CounterpartyOrganizationId = party; row.BuildingId = null; row.UnitId = null; await db.SaveChangesAsync();
        }
        Check(stored.Access.Count == 3 && stored.Access.Single(x => x.UserId == foreign).Name == "", "owner can manage stored grants without exposing a nonmember name");
        actor.Current = new(true, reader, account); var readOnly = await Read();
        Check(!readOnly.CanEdit && !readOnly.CanManageAccess && readOnly.Access.Count == 0, "Read grant reads details but cannot edit or inspect sharing administration");
        actor.Current = new(true, editor, account); var editOnly = await Read();
        Check(editOnly.CanEdit && !editOnly.CanManageAccess && editOnly.Access.Count == 0, "Edit grant does not grant sharing administration");
        actor.Current = new(true, admin, account); Check((await Read()).CanManageAccess && (await Read(unrelated.Id)).CanEdit, "AccountAdmin reads/manages account resources");
        await Denied(() => Read(foreignAgreement.Id), "foreign agreement denied even to account admin");
        actor.Current = new(true, stranger, account); await Denied(() => Read(), "unavailable agreement denied");
        await Denied(() => Read(Guid.NewGuid()), "unknown agreement denied");
        actor.Current = new(true, platform, account); await Denied(() => Read(), "global PlatformAdmin has no automatic account resource access");
        actor.Current = new(true, platform, foreignAccount); await Denied(() => service.ReadAsync(foreignAccount, foreignAgreement.Id), "PlatformAdmin without membership denied");
        actor.Current = new(false, owner, account); await Denied(() => Read(), "missing authentication");
        actor.Current = new(true, owner, null); await Denied(() => Read(), "missing account");
        actor.Current = new(true, owner, foreignAccount); await Denied(() => Read(), "wrong selected account");
        actor.Current = new(true, Guid.NewGuid(), account); await Denied(() => Read(), "missing user/membership");
        actor.Current = new(true, owner, account);
        await using (var db = factory.CreateDbContext()) { (await db.Users.SingleAsync(x => x.Id == owner)).IsActive = false; await db.SaveChangesAsync(); }
        await Denied(() => Read(), "inactive user denied");
        await using (var db = factory.CreateDbContext())
        {
            (await db.Users.SingleAsync(x => x.Id == owner)).IsActive = true;
            var row = await db.Agreements.SingleAsync(x => x.Id == target.Id); row.IsArchived = true; row.Status = AgreementStatus.Terminated; await db.SaveChangesAsync();
        }
        var archived = await Read(); Check(archived.IsArchived && archived.Status == AgreementStatus.Terminated && !archived.CanEdit && archived.CanManageAccess, "archived/terminated resource readable, edit blocked, owner retains sharing rights");
        await using (var db = factory.CreateDbContext()) { (await db.Agreements.SingleAsync(x => x.Id == target.Id)).IsArchived = false; await db.SaveChangesAsync(); }
        // These are independent assertions against the same common evaluator used for server actions.
        Check((await Read()).CanEdit == await auth.CanAsync(Permissions.Agreements.Update, new(ResourceKind.Agreement, target.Id)) && (await Read()).CanManageAccess == await auth.CanAsync(Permissions.Agreements.ManageSharing, new(ResourceKind.Agreement, target.Id)), "detail flags use common action rules");
        actor.Current = new(true, reader, account);
        await using (var db = factory.CreateDbContext()) { db.AgreementAccess.Remove(await db.AgreementAccess.SingleAsync(x => x.AccountId == account && x.AgreementId == target.Id && x.UserId == reader)); await db.SaveChangesAsync(); }
        await Denied(() => Read(), "same reader observes revoked grant");
        await using (var db = factory.CreateDbContext())
        {
            db.AgreementAccess.Add(new() { Id = Guid.NewGuid(), AccountId = account, AgreementId = target.Id, UserId = reader, Level = AgreementAccessLevel.Read });
            db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.Id == member)); await db.SaveChangesAsync();
        }
        await Denied(() => Read(), "same reader observes revoked membership");
        await using (var db = factory.CreateDbContext())
        {
            db.UserAccounts.Add(new() { Id = member, AccountId = account, UserId = reader });
            Check(!await db.AgreementDeadlines.AnyAsync(x => x.AccountId == account) && !await db.AgreementReminderSettings.AnyAsync(x => x.AccountId == account) && !(await db.Agreements.SingleAsync(x => x.Id == target.Id)).DeadlinesInitialized, "all direct reads leave deadlines/settings/backfill flag untouched");
            await db.SaveChangesAsync();
        }
        AgreementService Web(Guid id)
        {
            var context = new WebContext(id, account);
            return new(null!, new ListProbe(), new AttentionProbe(), new DashboardProbe(), new InsightsProbe(), new AgreementDetailsReader(new AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new AuthorizationData(factory), clock), new AgreementDetailsData(factory)), new AgreementFollowupReader(new AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new AuthorizationData(factory), clock), new AgreementFollowupData(factory), clock), new AgreementDeadlineReader(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementDeadlineData(factory), clock), new AgreementInitializationService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementDeadlineInitializer(factory, clock)), new AgreementFollowupWriter(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementFollowupWriteData(factory), clock), new AgreementIndexService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementIndexData(factory), clock), new AgreementDocumentService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementDocumentData(factory, null!, Microsoft.Extensions.Logging.Abstractions.NullLogger<AgreementDocumentData>.Instance), null!, clock), new AgreementManagementService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementManagementData(factory), clock), new AgreementFinancialService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementFinancialData(factory), clock), new AgreementAnalysisService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementAnalysisData(factory), null!, new AgreementManagementService(new TenantPlatform.Application.Security.AuthorizationService(new TenantPlatform.Web.Security.Application.CurrentActorAdapter(context), new TenantPlatform.Infrastructure.Authorization.AuthorizationData(factory), TimeProvider.System), new AgreementManagementData(factory), clock), new AgreementAnalysisCleanupService(new AgreementAnalysisCleanupData(factory), null!, clock, new TenantPlatform.Infrastructure.Authorization.HostSystemWorkContext(TenantPlatform.Application.Security.SystemWorkKind.AgreementAnalysisCleanup)), clock));
        }
        // Legacy GUI initialization is explicit, authorized and restricted to the selected resource.
        await Denied(() => Web(stranger).GetAsync(account, target.Id), "unauthorized GUI read cannot initialize target");
        await using (var db = factory.CreateDbContext()) Check(!(await db.Agreements.SingleAsync(x => x.Id == target.Id)).DeadlinesInitialized && !await db.AgreementDeadlines.AnyAsync(x => x.AccountId == account), "denied GUI read leaves target flag and deadlines untouched");
        var gui = Web(reader);
        if (provider == "PostgreSQL")
        {
            var results = await Task.WhenAll(gui.GetAsync(account, target.Id), Web(editor).GetAsync(account, target.Id));
            Check(results.All(x => x.DeadlinesInitialized), "concurrent GUI initialization retries parent revision conflicts");
        }
        var initialized = await gui.GetAsync(account, target.Id);
        Check(initialized.DeadlinesInitialized && !initialized.CanEdit && initialized.NoticeDeadline == new DateOnly(2027, 2, 28) && initialized.NoticeSnapshot.NoticeDeadline == initialized.NoticeDeadline, "GUI preserves Read compatibility and stored month-end notice recalculation");
        var renderingServices = new ServiceCollection().AddLogging().AddLocalization(options => options.ResourcesPath = "Resources");
        await using (var providerScope = renderingServices.BuildServiceProvider())
        await using (var renderer = new HtmlRenderer(providerScope, providerScope.GetRequiredService<ILoggerFactory>()))
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<TenantPlatform.Web.Components.Pages.Agreements.AgreementNoticeSnapshotView>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Value"] = initialized.NoticeSnapshot }));
                var html = System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
                Check(html.Contains(initialized.NoticeDeadline!.Value.ToString("d")) && html.Contains(initialized.RenewalDate!.Value.ToString("d")), "existing detail notice component renders moved snapshot contract and initialized dates");
            });
        }
        int count; Guid revision;
        await using (var db = factory.CreateDbContext())
        {
            count = await db.AgreementDeadlines.CountAsync(x => x.AccountId == account && x.AgreementId == target.Id);
            revision = (await db.Agreements.SingleAsync(x => x.Id == target.Id)).Revision;
            Check(count == 3 && !(await db.Agreements.SingleAsync(x => x.Id == unrelated.Id)).DeadlinesInitialized && !await db.AgreementDeadlines.AnyAsync(x => x.AgreementId == unrelated.Id || x.AgreementId == foreignAgreement.Id), "GUI backfills only authorized target; unrelated and foreign agreements stay untouched");
        }
        await gui.GetAsync(account, target.Id);
        await using (var db = factory.CreateDbContext()) Check(await db.AgreementDeadlines.CountAsync(x => x.AccountId == account && x.AgreementId == target.Id) == count && (await db.Agreements.SingleAsync(x => x.Id == target.Id)).Revision == revision, "repeated GUI read is idempotent without new revision or duplicate deadlines");
        actor.Current = new(true, reader, account); Check((await Read()).DeadlinesInitialized, "direct reader can observe initialized stored data without writing");
        var probe = new DetailsProbe(); var contextProbe = new WebContext(owner, account);
        var facade = new AgreementService(null!, new ListProbe(), new AttentionProbe(), new DashboardProbe(), new InsightsProbe(), probe, new FollowupProbe(), null!, null!, null!, null!, null!, null!, null!, null!);
        using var cancellation = new CancellationTokenSource();
        Check(ReferenceEquals(await facade.GetAsync(account, target.Id, cancellation.Token), probe.Result) && probe.Account == account && probe.Id == target.Id && probe.Token == cancellation.Token, "Web facade forwards account, id, token and exact initialized result");
    }

    public static async Task Snapshot()
    {
        var account = Guid.NewGuid(); var user = Guid.NewGuid(); var agreement = Guid.NewGuid();
        var actor = new Actor(new(true, user, account)); var gate = new DetailsGate(); using var cancellation = new CancellationTokenSource();
        var service = new AgreementDetailsReader(new AuthorizationService(actor, gate, TimeProvider.System), gate);
        var pending = service.ReadAsync(account, agreement, cancellation.Token); actor.Current = new(false, Guid.NewGuid(), null);
        gate.Membership.SetResult(new(true, true, false, [])); await pending;
        if (actor.Reads != 1 || gate.Scope?.ListScope.UserId != user || gate.Scope?.AgreementId != agreement || !gate.Scope.CanEdit || gate.Token != cancellation.Token) throw new Exception("Details snapshot failed");
        Console.WriteLine("PASS details: one trusted actor snapshot through authorization, resource and data ports; token forwarded, no HTTP/Blazor");
    }
}
sealed class DetailsProbe : IAgreementDetailsReader
{
    public Task<AgreementReportRow> ReadSummaryAsync(Guid a, Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Guid Account; public Guid Id; public CancellationToken Token;
    public AgreementDetailsDto Result = new() { DeadlinesInitialized = true };
    public Task<AgreementDetailsDto> ReadAsync(Guid accountId, Guid agreementId, CancellationToken ct = default) { Account = accountId; Id = agreementId; Token = ct; return Task.FromResult(Result); }
}
sealed class DetailsGate : IAuthorizationData, IAgreementDetailsData
{
    public Task<AgreementReportRow> ReadSummaryAsync(AgreementReadScope scope, CancellationToken ct) => throw new NotSupportedException();
    public TaskCompletionSource<AuthorizationSubject> Membership = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AgreementReadScope? Scope; public CancellationToken Token;
    public Task<AuthorizationSubject> GetSubjectAsync(Guid u, Guid? a, CancellationToken ct) => Membership.Task;
    public Task<ResourceAccessFacts?> GetResourceAsync(Guid a, Guid u, ResourceKind k, Guid id, DateOnly t, CancellationToken ct) => Task.FromResult<ResourceAccessFacts?>(new(a, OwnerUserId: u));
    public Task<AgreementDetailsDto> ReadAsync(AgreementReadScope scope, CancellationToken ct) { Scope = scope; Token = ct; return Task.FromResult(new AgreementDetailsDto()); }
    public Task<bool> AccountExistsAsync(Guid a, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasAgreementRelationshipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> HasLeasingOwnershipAsync(Guid a, Guid u, CancellationToken ct) => throw new NotSupportedException();
}
