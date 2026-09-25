using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TenantPlatform.Core.Accounts;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Identity;
using TenantPlatform.Core.Organizations;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Components.Pages.Dashboard;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.AccountSettings;
using TenantPlatform.Web.Services.Agreements;

CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
var today = new DateOnly(2026, 9, 25);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
Agreement A(string name, int? notice = null, int? end = null, int? renewal = null, AgreementStatus status = AgreementStatus.Active, bool archive = false) => new()
{
    Id = Guid.NewGuid(), Title = name, NoticeDeadline = notice.HasValue ? today.AddDays(notice.Value) : null,
    EndDate = end.HasValue ? today.AddDays(end.Value) : null, RenewalDate = renewal.HasValue ? today.AddDays(renewal.Value) : null,
    Status = status, IsArchived = archive, Type = AgreementType.Lease
};
var source = new[] { A("Today", 0, 0, 0), A("Day 90", 90, 120, 90), A("Day 91", 91, 121, 91),
    A("Yesterday", -1, -1, -1), A("Missing"), A("Draft", 0, -1, 0, AgreementStatus.Draft),
    A("Terminated", 0, -1, 0, AgreementStatus.Terminated), A("Archived", 0, -1, 0, archive: true),
    A("Expired", 0, -2, 0, AgreementStatus.Expired), A("Overlap", 1, -3, 2) }.AsQueryable();
Check(AgreementDashboardQueries.Attention(source, AgreementAttention.Notice, today).Count() == 3, "notice includes today and day 90, excludes day 91, missing dates and ineligible statuses");
Check(AgreementDashboardQueries.Attention(source, AgreementAttention.Renewal, today).Count() == 3, "renewal uses its own inclusive date range");
Check(AgreementDashboardQueries.Attention(source, AgreementAttention.Expired, today).Count() == 3, "expiry is strictly before today, active and expired statuses only");
var priority = new[] { A("Old", 2, -20), A("Zulu", 1, -1), A("Alpha", 1, -1), A("Future B", 0, 51),
    A("Future A", 0), A("Later", 90, 120), A("Excluded", 91, 120) }.AsQueryable();
var ordered = AgreementDashboardQueries.Prioritized(priority, today).ToList();
Check(ordered.Select(x => x.Title).SequenceEqual(new[] { "Alpha", "Zulu", "Old", "Future A", "Future B" }), "five contracts: latest expiry first, then nearest notice, title tie-break, no duplicates");
Check(AgreementReminderSchedule.Today(new DateTimeOffset(2026, 9, 24, 22, 30, 0, TimeSpan.Zero), "Europe/Oslo") == today, "calendar day uses account time zone near UTC midnight");
Check(AgreementReminderSchedule.Today(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), "Europe/Oslo") == new DateOnly(2026, 10, 25), "DST transition keeps correct calendar day");
var root = Guid.NewGuid(); var child = Guid.NewGuid(); var leaf = Guid.NewGuid(); var other = Guid.NewGuid();
var hierarchy = AccountRegisterHierarchy.Build(new[] { (root, (Guid?)null, "Asia"), (child, (Guid?)root, "Japan"), (leaf, (Guid?)child, "Tokyo"), (other, (Guid?)null, "Europe") });
Guid?[] assignments = [root, child, leaf, leaf, other, null];
var grouped = AgreementDashboardQueries.GroupHierarchy(assignments, hierarchy, null);
Check(grouped.Sum(x => x.Count) == 6 && grouped.Single(x => x.Key == root.ToString()).Count == 4 && grouped.Single(x => x.Key == "missing").Count == 1, "All groups by root and keeps unassigned agreements");
grouped = AgreementDashboardQueries.GroupHierarchy(assignments, hierarchy, root);
Check(grouped.Sum(x => x.Count) == 4 && grouped.Single(x => x.Direct).Count == 1 && grouped.Single(x => x.Key == child.ToString()).Count == 3, "subtree groups by nearest child and separates direct assignments");
Check(AgreementDashboardQueries.GroupHierarchy(assignments, hierarchy, leaf).Single() is { Count: 2, Direct: true }, "leaf selection shows its direct assignments");
Check(AgreementDashboardQueries.GroupHierarchy([], hierarchy, root).Count == 0, "empty subtree result");
try { AgreementDashboardQueries.GroupHierarchy(assignments, hierarchy, Guid.NewGuid()); throw new Exception("accepted foreign selection"); } catch (UnauthorizedAccessException) { }

await using (var sql = new TenantPlatformDbContext(new DbContextOptionsBuilder<TenantPlatformDbContext>()
    .UseNpgsql("Host=localhost;Database=translation_only").Options))
{
    var translated = AgreementDashboardQueries.Prioritized(sql.Agreements.Where(x => x.AccountId == root), today).ToQueryString();
    Check(translated.Contains("LIMIT") && translated.Contains("UNION"), "priority query translates to PostgreSQL with bounded result and deduplication");
}

var options = new DbContextOptionsBuilder<TenantPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
var factory = new Factory(options);
var account = Guid.NewGuid(); var user = Guid.NewGuid(); var stranger = Guid.NewGuid(); var foreignAccount = Guid.NewGuid();
var context = new Context(user, account);
var authorization = new TenantAuthorizationService(factory, context);
var clock = new Clock(new DateTimeOffset(2026, 9, 24, 22, 30, 0, TimeSpan.Zero));
var service = new AgreementService(factory, context, authorization, null!, NullLogger<AgreementService>.Instance, clock);
var granted = A("Shared", 0, 51, 90); var owned = A("Owned", 90, -1); var hidden = A("Hidden", 0, -1); var foreign = A("Foreign", 0, -1);
await using (var db = factory.CreateDbContext())
{
    db.Accounts.AddRange(new Account { Id = account, Name = "A" }, new Account { Id = foreignAccount, Name = "B" });
    db.Users.AddRange(new User { Id = user, FirstName = "Anna", LastName = "Andersen", Email = "a@example.test", IsActive = true }, new User { Id = stranger, FirstName = "Bjørn", LastName = "Berg", Email = "b@example.test", IsActive = true });
    db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = user });
    db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), AccountId = account, UserId = stranger });
    db.GeographicAreas.AddRange(hierarchy.Select(x => new GeographicArea { Id = x.Id, AccountId = account, ParentId = x.Ancestors.LastOrDefault() is var p && p != Guid.Empty ? p : null, Name = x.Path.Split(" / ").Last() }));
    db.OrganizationElements.AddRange(hierarchy.Select(x => new OrganizationElement { Id = x.Id, AccountId = account, ParentId = x.Ancestors.LastOrDefault() is var p && p != Guid.Empty ? p : null, Name = x.Path.Split(" / ").Last() }));
    var party = new Organization { Id = Guid.NewGuid(), AccountId = account, Name = "Partner" }; db.Organizations.Add(party);
    foreach (var a in new[] { owned, granted, hidden, foreign })
    {
        a.AccountId = a == foreign ? foreignAccount : account; a.OwnerUserId = a == owned ? user : stranger;
        a.GeographicAreaId = a == hidden ? other : leaf; a.OrganizationElementId = a.GeographicAreaId; a.CounterpartyOrganizationId = party.Id;
        db.Agreements.Add(a);
    }
    db.AgreementAccess.Add(new AgreementAccess { Id = Guid.NewGuid(), AccountId = account, AgreementId = granted.Id, UserId = user, Level = AgreementAccessLevel.Read });
    await db.SaveChangesAsync();
}
var attention = await service.GetAttentionAsync(account);
Check(attention is { Notice: 2, Renewal: 1, Expired: 1 } && attention.Today == today, "service counts only owned/shared agreements in selected account");
Check((await service.GetDashboardContractsAsync(account)).Items.Select(x => x.Id).SequenceEqual(new[] { owned.Id, granted.Id }), "service priority list respects access");
foreach (var dimension in Enum.GetValues<AgreementInsightDimension>())
{
    var data = await service.GetInsightsAsync(account, dimension);
    Check(data.Total == 2 && data.Groups.Sum(x => x.Count) == 2, $"{dimension} aggregation respects access without duplication");
    if (dimension == AgreementInsightDimension.Owner)
        Check(data.Groups.Count == 2 && data.Groups.All(x => x.Count == 1) && data.Groups.Any(x => x.Name == "Anna Andersen"), "owners grouped by identity with names, inaccessible agreements excluded");
    if (dimension is AgreementInsightDimension.Geography or AgreementInsightDimension.Organization)
    {
        Check(data.Options.Count == 3 && data.Options.All(x => x.Id != other), "hierarchy options expose only accessible branches and ancestors");
        Check((await service.GetInsightsAsync(account, dimension, child)).Total == 2, "selected child includes descendants");
        try { await service.GetInsightsAsync(account, dimension, other); throw new Exception("accepted hidden branch"); } catch (UnauthorizedAccessException) { }
    }
}
Check((await service.ListAsync(account, new AgreementFilter { Attention = AgreementAttention.Notice })).TotalCount == 2, "click-through filter matches attention count");
try { await service.GetAttentionAsync(foreignAccount); throw new Exception("accepted foreign account"); } catch (UnauthorizedAccessException) { }
await using (var db = factory.CreateDbContext())
{
    var member = await db.UserAccounts.SingleAsync(x => x.UserId == user);
    db.UserAccountRoles.Add(new UserAccountRole { Id = Guid.NewGuid(), UserAccountId = member.Id, Role = UserRole.AccountAdmin });
    await db.SaveChangesAsync();
}
Check((await service.GetInsightsAsync(account, AgreementInsightDimension.Type)).Total == 3, "account admin sees account agreements, never foreign account");

await using (var db = factory.CreateDbContext())
{
    (await db.Agreements.SingleAsync(x => x.Id == hidden.Id)).Status = AgreementStatus.Draft;
    await db.SaveChangesAsync();
}
var statusGroups = await service.GetInsightsAsync(account, AgreementInsightDimension.Status);
Check(statusGroups.Groups.Single(x => x.Status == AgreementStatus.Active).Count == 2 &&
    statusGroups.Groups.Single(x => x.Status == AgreementStatus.Draft).Count == 1, "status counts distinguish active and draft agreements");
var ownerGroups = await service.GetInsightsAsync(account, AgreementInsightDimension.Owner);
Check(ownerGroups.Groups.Single(x => x.Key == stranger.ToString()).Count == 2 && ownerGroups.Total == 3,
    "owner aggregation counts multiple agreements once each within account");

var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources");
services.AddSingleton<IAgreementService>(service);
await using var provider = services.BuildServiceProvider();
await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
var htmlParts = new List<string>();
await renderer.Dispatcher.InvokeAsync(async () =>
{
    foreach (var type in new[] { typeof(AttentionWidget), typeof(ContractsWidget), typeof(InsightsWidget) })
    {
        var host = new HostState(type, account);
        var output = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = host }));
        var html = System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        htmlParts.Add(html);
        Check(!html.Contains("DashboardLoadError") && !html.Contains("Kunne ikke laste"), $"{type.Name} renders successful state");
        if (type == typeof(ContractsWidget))
        {
            Check(html.Contains("Utløper 15.11.2026 · om 51 dager") && html.Contains("/agreements/"), "contract end date is shown independently of notice deadline, whole row is a link");
            var text = type.GetMethod("ExpiryText", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Check((string)text.Invoke(host.Instance, new object?[] { null, today })! == "Utløpsdato ikke angitt", "missing expiry has no relative date");
            Check((string)text.Invoke(host.Instance, new object?[] { today, today })! == "Utløper i dag", "expiry today text");
        }
        if (type == typeof(InsightsWidget))
        {
            Check(html.Contains("<table") && html.Contains("scope=\"row\"") && html.Contains("100,0") && html.Contains("for=\"dashboard-dimension\""), "chart has accessible figures and labelled native keyboard selector");
            Check(html.Contains("Avtaleansvarlig") && html.Contains("value=\"Status\""), "status and agreement owner are available in dimension selector");
            foreach (var dimension in new[] { AgreementInsightDimension.Status, AgreementInsightDimension.Owner })
            {
                type.GetField("dimension", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host.Instance, dimension);
                await (Task)type.GetMethod("DimensionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null)!;
                typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null);
                var changed = System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
                Check(changed.Contains(dimension == AgreementInsightDimension.Status ? "Utkast" : "Bjørn Berg") &&
                    changed.Contains("66,7") && !changed.Contains("id=\"dashboard-structure\""), "status/owner selection updates labels, percentages and chart without hierarchy filter");
            }
            type.GetField("dimension", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host.Instance, AgreementInsightDimension.Geography);
            await (Task)type.GetMethod("DimensionChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null);
            html = System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
            Check(html.Contains("Asia / Japan / Tokyo") && html.Contains("dashboard-structure"), "dimension change renders hierarchical selector and updated data");
            type.GetField("selected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(host.Instance, root);
            await (Task)type.GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null)!;
            typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null);
            Check(output.ToHtmlString().Contains("Japan"), "hierarchy filter updates legend");
        }
    }
});
var preview = Environment.GetEnvironmentVariable("DASHBOARD_PREVIEW_PATH");
if (!string.IsNullOrEmpty(preview)) await File.WriteAllTextAsync(preview, "<!doctype html><html lang=\"nb\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><link rel=\"stylesheet\" href=\"/bootstrap/bootstrap.min.css\"><link rel=\"stylesheet\" href=\"/app.css\"><main style=\"padding:24px\"><h1>Dashboard</h1><div class=\"dashboard-grid\">" + string.Join("", htmlParts) + "</div></main></html>");
foreach (var type in new[] { typeof(AttentionWidget), typeof(ContractsWidget), typeof(InsightsWidget) })
{
    var stub = DispatchProxy.Create<IAgreementService, WidgetProxy>();
    var proxy = (WidgetProxy)(object)stub;
    var dependencies = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources");
    dependencies.AddSingleton(stub);
    await using var sp = dependencies.BuildServiceProvider();
    await using var view = new HtmlRenderer(sp, sp.GetRequiredService<ILoggerFactory>());
    await view.Dispatcher.InvokeAsync(async () =>
    {
        var host = new HostState(type, account);
        var output = view.BeginRenderingComponent<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = host }));
        Check(output.ToHtmlString().Contains("role=\"status\""), $"{type.Name} has independent loading status");
        proxy.Fail();
        await output.QuiescenceTask;
        Check(System.Net.WebUtility.HtmlDecode(output.ToHtmlString()).Contains("Prøv igjen"), $"{type.Name} exposes error and retry");
        proxy.Empty(today);
        await (Task)type.GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null)!;
        typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Instance, null);
        var html = System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
        Check(!html.Contains("Kunne ikke laste") && html.Contains("Ingen"), $"{type.Name} retry recovers to distinct empty state");
    });
}
Console.WriteLine("Dashboard smoke checks passed (in-memory; no application database or migrations used).");

sealed class Factory(DbContextOptions<TenantPlatformDbContext> options) : IDbContextFactory<TenantPlatformDbContext>
{ public TenantPlatformDbContext CreateDbContext() => new(options); }
sealed class Context(Guid user, Guid account) : ICurrentUserContextService
{ public CurrentUserContext Current { get; } = new() { UserId = user, CurrentAccountId = account, IsAuthenticated = true }; }
sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed record HostState(Type Type, Guid AccountId) { public object? Instance { get; set; } }
sealed class Host : ComponentBase
{
    [Parameter] public HostState State { get; set; } = null!;
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent(0, State.Type); b.AddAttribute(1, "AccountId", State.AccountId);
        b.AddComponentReferenceCapture(2, instance => State.Instance = instance); b.CloseComponent();
    }
}

public class WidgetProxy : DispatchProxy
{
    private TaskCompletionSource<AgreementAttentionDto> attention = new();
    private TaskCompletionSource<AgreementDashboardList> contracts = new();
    private TaskCompletionSource<AgreementInsightsDto> insights = new();
    protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
    {
        nameof(IAgreementService.GetAttentionAsync) => attention.Task,
        nameof(IAgreementService.GetDashboardContractsAsync) => contracts.Task,
        nameof(IAgreementService.GetInsightsAsync) => insights.Task,
        _ => throw new NotSupportedException()
    };
    public void Fail()
    {
        attention.TrySetException(new Exception("Simulated failure"));
        contracts.TrySetException(new Exception("Simulated failure"));
        insights.TrySetException(new Exception("Simulated failure"));
    }
    public void Empty(DateOnly today)
    {
        attention = new(); contracts = new(); insights = new();
        attention.SetResult(new(today, 0, 0, 0)); contracts.SetResult(new(today, [])); insights.SetResult(new([], []));
    }
}
