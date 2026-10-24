using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TenantPlatform.Application.Agent;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.PersonalPages;
using TenantPlatform.Infrastructure.Agent;
using TenantPlatform.Infrastructure.Persistence.OAuth;
using TenantPlatform.Web.Security;

static partial class RegistryChecks
{
    private static async Task RunFlexibleReports(WebApplication web, Factory factory, Guid admin, Guid account, Guid source, Browser browser, AgentProtocolFixture model)
    {
        var memory = web.Services.GetRequiredService<ReadAgentSessions>(); memory.ClearUser(admin);
        var binding = (await Pages(p => p.SourcesAsync())).Single(s => s.ConnectionId == source);
        var ids = new List<Guid>();
        await using (var db = factory.CreateDbContext()) {
            var template = await db.Agreements.FirstAsync(a => a.AccountId == account);
            for (var i = 0; i < 27; i++) {
                var id = Guid.NewGuid(); ids.Add(id);
                db.Agreements.Add(new Agreement { Id = id, AccountId = account, OwnerUserId = admin, CounterpartyOrganizationId = template.CounterpartyOrganizationId,
                    Title = "9D fixture " + i.ToString("D2"), Status = AgreementStatus.Active, Type = AgreementType.Supplier,
                    StartDate = new(2025,1,1), CurrentPeriodStartDate = new(2025,1,1), EndDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(20),
                    CreatedByUserId = admin, UpdatedByUserId = admin, Revision = Guid.NewGuid() });
            }
            await db.SaveChangesAsync();
        }
        var legacy = new PageDefinition(1, "Legacy report", source, binding.QueryVersion, binding.AggregateVersion, new(PagePeriodKind.NextMonths),
            new(Search: "9D fixture", Archive: AgreementArchiveSelection.Exclude), [PageComponent.Metric, PageComponent.BuildingChart, PageComponent.AgreementTable], ProfileVersion: binding.ProfileVersion);
        var current = PageDefinitions.Current(legacy);
        Check(current.Version == 2 && current.GroupBy == AgreementReportGrouping.Building && current.Columns!.SequenceEqual(PageDefinitions.LegacyColumns) && legacy.Version == 1 && legacy.Columns is null,
            "9D explicit legacy conversion preserves meaning and does not mutate the original");
        foreach (var bad in new[] { current with { Components = [] }, current with { Components = [PageComponent.Metric, PageComponent.Metric] },
            current with { Columns = [] }, current with { Columns = [PageColumn.Title, PageColumn.Title] }, current with { Columns = [(PageColumn)999] },
            current with { Period = new(PagePeriodKind.All) }, legacy with { Period = new(PagePeriodKind.All, null) }, current with { Period = new(PagePeriodKind.All, null, new(2026,1,1)) },
            current with { GroupBy = (AgreementReportGrouping)999 }, current with { Columns = null } }) {
            try { PersonalPageService.Validate(bad); throw new Exception("Expected invalid declaration"); } catch (PersonalPageException e) when(e.Code == "Invalid") { }
        }
        try { PageDefinitions.Current(legacy with { Version = 99 }); throw new Exception("Expected unsupported version"); } catch (PersonalPageException e) when(e.Code == "Version") { }
        var options = web.Services.GetRequiredService<IOptions<ReadAgentOptions>>().Value;
        var previousKey = options.ApiKey; options.ApiKey = ""; options.Enabled = false; var modelCount = model.Payloads.Count;
        for (var mask = 1; mask < 8; mask++) {
            var components = Enum.GetValues<PageComponent>().Where(c => (mask & (1 << (int)c)) != 0).Reverse().ToArray();
            var spec = current with { Components = components, Columns = [PageColumn.Status, PageColumn.Title, PageColumn.EndDate], GroupBy = mask % 2 == 0 ? AgreementReportGrouping.Status : AgreementReportGrouping.Building };
            var start = PagesProtocol.Calls.Count; var result = await Run(spec); var calls = PagesProtocol.Calls.Skip(start).ToArray();
            Check(calls.Count(c => c.Name == "agreements_query") == (PageDefinitions.NeedsQuery(spec) ? 1 : 0) &&
                calls.Count(c => c.Name == "agreements_aggregate") == (PageDefinitions.NeedsAggregate(spec) ? 1 : 0), "9D minimal actual MCP calls for component combination " + mask);
            Check((result.Table is not null) == PageDefinitions.NeedsQuery(spec) && (result.Aggregate is not null) == PageDefinitions.NeedsAggregate(spec) &&
                result.Columns.SequenceEqual(spec.Columns!) && result.GroupBy == spec.GroupBy && result.Components.SequenceEqual(spec.Components), "9D absent components have absent data; ordered columns/grouping survive " + mask);
            if (result.Aggregate is { } a) Check(a.Complete && a.TotalCount == 27 && a.Groups.Sum(g => g.Count) == 27 && a.GroupBy == spec.GroupBy.ToString(), "9D complete unique aggregate for grouping " + spec.GroupBy);
            if (result.Table is { } t) Check(t.TotalCount == 27 && t.ReturnedCount == 25 && t.HasMore && !t.Complete && !t.SnapshotConsistent, "9D table remains honestly paged without summing its rows");
            if (calls.Length == 2) Check(calls[1].Args.GetProperty("endDateFrom").GetString() == result.Selection.Filter.EndDateFrom!.Value.ToString("yyyy-MM-dd") &&
                calls[1].Args.GetProperty("endDateTo").GetString() == result.Selection.Filter.EndDateTo!.Value.ToString("yyyy-MM-dd") && !calls[1].Args.TryGetProperty("endsWithinMonths", out _), "9D combined calls reuse the target's normalized concrete dates");
            else Check(calls[0].Args.GetProperty("endsWithinMonths").GetInt32() == 3, "9D single-tool plan delegates period normalization to the source");
        }
        Check(model.Payloads.Count == modelCount, "9D all seven component combinations run with AI disabled and no key"); options.ApiKey = previousKey; options.Enabled = true;
        var metric = current with { Components = [PageComponent.Metric], Columns = [] };
        var table = current with { Components = [PageComponent.AgreementTable] };
        await ApproveTool("agreements_query", false);
        var beforeDenied = PagesProtocol.Calls.Count;
        await PageFailure(() => Run(table), "SourceChanged");
        Check(PagesProtocol.Calls.Count == beforeDenied, "9D denied required query performs no fallback or data call");
        Check((await Run(metric)).Aggregate!.TotalCount == 27, "9D metric needs no approval for unused query");
        await ApproveTool("agreements_query", true); await ApproveTool("agreements_aggregate", false);
        await PageFailure(() => Run(metric), "SourceChanged");
        Check((await Run(table)).Table!.TotalCount == 27, "9D table needs no approval for unused aggregate");
        await ApproveTool("agreements_aggregate", true);
        Console.WriteLine("Isolating legacy/report presentation quota window."); await Task.Delay(TimeSpan.FromSeconds(61));
        // Seed the exact old JSON directly in the isolated fixture, without going through today's explicit-save upgrade.
        var initial = await Pages(p => p.ListAsync()); var pageId = Guid.NewGuid(); var revision = Guid.NewGuid();
        await Scope(async sp => { await sp.GetRequiredService<IPersonalPageData>().SaveAsync(account, admin, initial.Revision,
            new(revision, [..initial.Pages, new(pageId, account, admin, legacy)]), default); return true; });
        await using (var db = factory.CreateDbContext()) {
            var row = await db.Set<PersonalPageSet>().SingleAsync(x => x.AccountId == account && x.OwnerUserId == admin);
            var oldJson = JsonNode.Parse(row.DefinitionsJson)!.AsArray();
            var oldDefinition = oldJson.Single(x => x!["Id"]!.GetValue<Guid>() == pageId)!["Definition"]!.AsObject();
            oldDefinition.Remove("GroupBy"); oldDefinition.Remove("Columns"); row.DefinitionsJson = oldJson.ToJsonString(); await db.SaveChangesAsync();
        }
        // Future versions must fail even metadata reads and unrelated collection writes,
        // because today's serializer cannot preserve their unknown fields.
        await using (var db = factory.CreateDbContext()) {
            var row = await db.Set<PersonalPageSet>().SingleAsync(x => x.AccountId == account && x.OwnerUserId == admin);
            var originalJson = row.DefinitionsJson; var future = JsonNode.Parse(originalJson)!.AsArray();
            future.Single(x => x!["Id"]!.GetValue<Guid>() == pageId)!["Definition"]!["Version"] = 99;
            row.DefinitionsJson = future.ToJsonString(); await db.SaveChangesAsync();
            var futureRaw = await Raw();
            await PageFailure(() => Pages(p => p.ReadAsync(pageId)), "Version");
            await PageFailure(() => Pages(p => p.SaveAsync(null, revision, current)), "Version");
            await PageFailure(() => Pages(p => p.ChangeAsync(pageId, revision, 1)), "Version");
            Check(await Raw() == futureRaw, "9D unknown future definitions cannot be interpreted or lost through an unrelated collection mutation");
            row.DefinitionsJson = originalJson; await db.SaveChangesAsync();
        }
        var beforeRead = await Raw();
        var legacyHtml = await (await browser.Get("/my-pages/" + pageId)).Content.ReadAsStringAsync();
        Check(legacyHtml.Contains("name=\"showColumnStatus\"") && legacyHtml.Contains("name=\"groupBy\"") && legacyHtml.Contains("data-type-software-license"),
            "9D actual SSR editor exposes optional columns/grouping and correctly named localized enum labels");
        var savedLegacy = await Pages(p => p.ReadAsync(pageId));
        var legacyResult = await Scope(sp => sp.GetRequiredService<PersonalPageRunner>().RunAsync(savedLegacy.Definition, savedId: pageId));
        var opened = await Agent(a => a.BeginEditAsync(pageId, account, true)); var conversation = opened.Conversation;
        Check(savedLegacy.Definition.Version == 1 && legacyResult.Columns.SequenceEqual(PageDefinitions.LegacyColumns) &&
            opened.Report.Draft.Definition.Version == 2 && opened.Report.Draft.Edit!.Original.Version == 1 && AgentReportDrafts.Changes(opened.Report.Draft).Length == 0 && await Raw() == beforeRead,
            "9D reading/running/opening an old page upgrades only memory, retains exact conflict original and writes nothing");
        var intent = opened.Report.Draft.Intent with { Components = [PageComponent.BuildingChart], GroupBy = AgreementReportGrouping.Status, Columns = [] };
        var report = await Ask("Fjern avtaletabellen og nøkkeltallet. Grupper diagrammet etter status i stedet.");
        Check(report.Preview.Table is null && report.Preview.Aggregate!.GroupBy == "Status" && AgentReportDrafts.Changes(report.Draft).Select(c => c.Field).SequenceEqual(new[] { "Components", "GroupBy", "Columns" }),
            "9D editing legacy page changes optional components, grouping and deterministic backend diff");
        intent = intent with { Components = [PageComponent.BuildingChart, PageComponent.AgreementTable, PageComponent.Metric], Columns = [PageColumn.Title, PageColumn.Building, PageColumn.Status, PageColumn.EndDate] };
        report = await Ask("Vis tittel, bygg, status og sluttdato i tabellen. Flytt nøkkeltallet nederst.");
        Check(report.Draft.Definition.Components.Last() == PageComponent.Metric && report.Preview.Columns.SequenceEqual(intent.Columns!) && await Raw() == beforeRead,
            "9D follow-up presentation stays an unsaved, explicitly ordered declaration");
        var manual = await Pages(p => p.SaveAsync(pageId, revision, current with { Title = "Concurrent manual change" }));
        await AgentFailure(() => Agent(a => a.SaveReportAsync(conversation, report.Draft.Id, report.Draft.Revision)), "DraftConflict");
        memory.ClearUser(admin); opened = await Agent(a => a.BeginEditAsync(pageId, account, true)); conversation = opened.Conversation;
        intent = opened.Report.Draft.Intent with { Components = [PageComponent.BuildingChart], GroupBy = AgreementReportGrouping.Status, Columns = [] };
        report = await Ask("Fjern tabellen og nøkkeltallet. Grupper diagrammet etter status.");
        var updatedId = await Agent(a => a.SaveReportAsync(conversation, report.Draft.Id, report.Draft.Revision));
        Check(updatedId == pageId && (await Pages(p => p.ReadAsync(pageId))).Definition.Version == 2, "9D explicit update persists v2 with stable original identity");
        // Cover missing, past and distant end dates plus an archived agreement, without changing the relative-period fixtures.
        await using (var db = factory.CreateDbContext()) {
            var template = await db.Agreements.FirstAsync(a => a.AccountId == account);
            foreach (var (end, archived) in new[] { ((DateOnly?)null, false), ((DateOnly?)new DateOnly(2000,1,1), false), ((DateOnly?)new DateOnly(2090,1,1), false), ((DateOnly?)null, true) }) {
                var fixture = new Agreement { Id = Guid.NewGuid(), AccountId = account, OwnerUserId = admin, CounterpartyOrganizationId = template.CounterpartyOrganizationId,
                    Title = "9D all date fixture", Status = AgreementStatus.Active, Type = AgreementType.Supplier, IsArchived = archived,
                    StartDate = new(1990,1,1), CurrentPeriodStartDate = new(1990,1,1), EndDate = end,
                    CreatedByUserId = admin, UpdatedByUserId = admin, Revision = Guid.NewGuid() };
                ids.Add(fixture.Id); db.Add(fixture);
            }
            await db.SaveChangesAsync();
        }
        int expectedAll;
        await using (var db = factory.CreateDbContext()) expectedAll = await db.Agreements.CountAsync(a => a.AccountId == account && a.Status == AgreementStatus.Active && !a.IsArchived);
        memory.ClearUser(admin); conversation = await Agent(a => a.BeginAsync(source, account, true));
        intent = new("Aktive avtaler per bygg", new(PagePeriodKind.All, null), new(Status: AgreementStatus.Active), [PageComponent.BuildingChart], GroupBy: AgreementReportGrouping.Building, Columns: []);
        var beforeNew = await Pages(p => p.ListAsync());
        var beforeAll = PagesProtocol.Calls.Count;
        report = await Ask("Lag en side som heter «Aktive avtaler per bygg». Vis bare et stolpediagram med antall aktive avtaler fordelt på bygg. Ta med avtaler uavhengig av sluttdato, men utelat arkiverte avtaler. Ikke vis nøkkeltall eller avtaletabell.");
        var allCall = PagesProtocol.Calls.Skip(beforeAll).Single();
        Check(report.Preview.Aggregate!.TotalCount == expectedAll && report.Preview.Selection.Filter.EndDateFrom is null && report.Preview.Selection.Filter.EndDateTo is null &&
            !allCall.Args.TryGetProperty("endDateFrom", out _) && !allCall.Args.TryGetProperty("endDateTo", out _) && !allCall.Args.TryGetProperty("endsWithinMonths", out _),
            "all-date proposal uses one real aggregate, includes missing/past/distant EndDate and excludes archives without invented date bounds");
        Check(report.Preview.Table is null && report.Draft.Definition.Components.SequenceEqual(new[] { PageComponent.BuildingChart }) && (await Pages(p => p.ListAsync())).Revision == beforeNew.Revision,
            "9D new natural-language chart-only proposal produces real preview without saving");
        var created = await Agent(a => a.SaveReportAsync(conversation, report.Draft.Id, report.Draft.Revision));
        Console.WriteLine("Isolating unrestricted report-run quota window."); await Task.Delay(TimeSpan.FromSeconds(61));
        var allDefinition = (await Pages(p => p.ReadAsync(created))).Definition;
        Check(allDefinition.Period.Kind == PagePeriodKind.All && (await Run(allDefinition)).Aggregate!.TotalCount == expectedAll, "all-date selection survives explicit save and deterministic reopening");
        var allTable = await Run(allDefinition with { Components = [PageComponent.BuildingChart, PageComponent.AgreementTable], Columns = [PageColumn.Title] });
        var allTableOnly = await Run(allDefinition with { Components = [PageComponent.AgreementTable], Columns = [PageColumn.Title] });
        Check(allTableOnly.Aggregate is null && allTableOnly.Table!.TotalCount == expectedAll && allTableOnly.Selection.Filter.EndDateFrom is null,
            "table-only report retains All without adding dates or fetching aggregate");
        Check(allTable.Table!.Selection.Filter == allTable.Aggregate!.Selection.Filter && allTable.Selection.Filter.EndDateFrom is null, "combined all-date calls retain the same unrestricted date filter");
        Check((await Pages(p => p.ReadAsync(created))).Definition.Components.SequenceEqual(new[] { PageComponent.BuildingChart }), "9D explicit new-page save keeps the exact optional component set");
        // Bound unused versions must not be refreshed by a proposal that later adds the tool back.
        var bound = (await Pages(p => p.ReadAsync(created))).Definition;
        await using (var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()))
            await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == "agreements_query").ExecuteUpdateAsync(s => s.SetProperty(t => t.Version, "changed-9D"));
        memory.ClearUser(admin); opened = await Agent(a => a.BeginEditAsync(created, account, true)); conversation = opened.Conversation;
        intent = opened.Report.Draft.Intent with { Components = [PageComponent.AgreementTable], Columns = [PageColumn.Title] };
        await AgentFailure(() => Ask("Vis bare tabell med tittel."), "DraftSourceChanged");
        Check((await Pages(p => p.ReadAsync(created))).Definition.QueryVersion == bound.QueryVersion, "9D adding a component never upgrades its stored tool binding");
        await using (var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()))
            await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == "agreements_query").ExecuteUpdateAsync(s => s.SetProperty(t => t.Version, binding.QueryVersion));
        model.ReportArguments = null; memory.ClearUser(admin);
        foreach (var id in new[] { pageId, created }) { var all = await Pages(p => p.ListAsync()); await Pages(p => p.ChangeAsync(id, all.Revision, null)); }
        await using (var db = factory.CreateDbContext()) { await db.Agreements.Where(a => ids.Contains(a.Id)).ExecuteDeleteAsync(); }
        Console.WriteLine("Flexible reports real PostgreSQL/OAuth/MCP checks passed.");

        async Task<AgentReportPreview> Ask(string prompt) { model.ReportArguments = JsonSerializer.Serialize(intent, AgentReportDrafts.Json); return (await Agent(a => a.AskAsync(conversation, prompt))).Report!; }
        async Task ApproveTool(string name, bool approved) { await using var db = new OAuthDbContext(web.Services.GetRequiredService<DbContextOptions<OAuthDbContext>>()); await db.Set<McpDiscoveredTool>().Where(t => t.ConnectionId == source && t.Name == name).ExecuteUpdateAsync(s => s.SetProperty(t => t.Approved, approved)); }
        async Task<string> Raw() { await using var db = factory.CreateDbContext(); var set = await db.Set<PersonalPageSet>().AsNoTracking().SingleAsync(x => x.AccountId == account && x.OwnerUserId == admin); return set.Revision + ":" + set.DefinitionsJson; }
        Task<PersonalPageResult> Run(PageDefinition d) => Scope(sp => sp.GetRequiredService<PersonalPageRunner>().RunAsync(d));
        Task<T> Pages<T>(Func<PersonalPageService, Task<T>> action) => Scope(sp => action(sp.GetRequiredService<PersonalPageService>()));
        Task<T> Agent<T>(Func<ReadAgentService, Task<T>> action) => Scope(sp => action(sp.GetRequiredService<ReadAgentService>()));
        async Task<T> Scope<T>(Func<IServiceProvider, Task<T>> action) {
            await using var scope = web.Services.CreateAsyncScope(); var sp = scope.ServiceProvider; var accessor = sp.GetRequiredService<IHttpContextAccessor>(); var previous = accessor.HttpContext;
            accessor.HttpContext = new DefaultHttpContext { RequestServices = sp, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(TenantPlatformClaimTypes.UserId, admin.ToString()), new Claim(TenantPlatformClaimTypes.CurrentAccountId, account.ToString()) }, "trusted-fixture")) };
            try { return await action(sp); } finally { accessor.HttpContext = previous; }
        }
        static async Task PageFailure(Func<Task> action, string code) { try { await action(); } catch (PersonalPageException e) when(e.Code == code) { return; } throw new Exception("Expected page error " + code); }
        static async Task AgentFailure(Func<Task> action, string code) { try { await action(); } catch (ReadAgentException e) when(e.Code == code) { return; } throw new Exception("Expected agent error " + code); }
    }
}
