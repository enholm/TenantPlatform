using System.Reflection;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Web.Components.Pages.Dashboard;
using TenantPlatform.Web.Services.Agreements;

static class DynamicWidgetChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(object widget, string name) => (T)widget.GetType().GetField(name, Flags)!.GetValue(widget)!;
    static void Key(object widget, string value) => widget.GetType().GetProperty("GroupingKey", Flags)!.SetValue(widget, value);
    static Task Act(object widget, string name, params object?[] args) => (Task)widget.GetType().GetMethod(name, Flags)!.Invoke(widget, args)!;
    static void Refresh(object widget) => typeof(ComponentBase).GetMethod("StateHasChanged", Flags)!.Invoke(widget, null);
    static string Decode(string html) => System.Net.WebUtility.HtmlDecode(html);
    public static async Task Run(Action<bool, string> check)
    {
        var f = new InsightFixture(); await f.Seed();
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources").AddSingleton<IAgreementService>(f.Service());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var state = new HostState(typeof(InsightsWidget), f.Account);
            var output = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = state }));
            var widget = (InsightsWidget)state.Instance!;
            async Task Action(string name, params object?[] args) { await Act(widget, name, args); Refresh(widget); }
            Key(widget, "dimension:" + f.Dimension); await Action("DimensionChanged");
            var html = Decode(output.ToHtmlString());
            check(html.Contains("Norsk dimensjon") && html.Contains("Unike synlige avtaler: 4") && html.Contains("prosentgrunnlag): 5") && html.Contains("60,0") && html.Contains("flere viste grupper"), "widget renders dynamic labels, distinct totals and 60% from group sum five instead of unique total four");
            await Action("NavigateAsync", (Guid?)f.Root); await Action("NavigateAsync", (Guid?)f.Parent);
            html = Decode(output.ToHtmlString());
            check(Field<AgreementInsightsDto>(widget, "data").SelectedNodeId == f.Parent && html.Contains("Tilbake ett nivå") && html.Contains("Direkte") && html.Contains("Historisk (inaktiv)") && html.Contains("40,0"), "drill handler renders path/back/direct/historical context and correct overlapping percentages");
            await Action("NavigateAsync", (Guid?)f.Branch); await Action("NavigateAsync", (Guid?)f.Leaf);
            check(Field<AgreementInsightsDto>(widget, "data").Groups.Single().Kind == AgreementInsightGroupKind.Direct, "leaf navigation remains a direct-only context");
            await Action("BackAsync"); check(Field<AgreementInsightsDto>(widget, "data").SelectedNodeId == f.Branch, "back handler returns exactly one parent level");
            await Action("NavigateAsync", (Guid?)f.Leaf);
            typeof(InsightsWidget).GetProperty("RefreshVersion")!.SetValue(widget, 1L); await Action("OnParametersSetAsync");
            check(Field<AgreementInsightsDto>(widget, "data").SelectedNodeId == f.Leaf, "refresh keeps an authorized valid drill context");
            await using (var db = f.Factory.CreateDbContext())
            { db.AgreementDimensionAssignments.RemoveRange(await db.AgreementDimensionAssignments.Where(x => x.ValueId == f.Leaf).ToListAsync()); await db.SaveChangesAsync(); }
            typeof(InsightsWidget).GetProperty("RefreshVersion")!.SetValue(widget, 2L); await Action("OnParametersSetAsync");
            check(Field<AgreementInsightsDto>(widget, "data").SelectedNodeId is null && Field<bool>(widget, "contextReset") && !Decode(output.ToHtmlString()).Contains("Root / Parent / Branch / Leaf"), "refresh with newly hidden context returns to authorized top level without stale node names");
            var second = Guid.NewGuid(); var secondNode = Guid.NewGuid();
            await using (var db = f.Factory.CreateDbContext())
            {
                db.Dimensions.Add(new Dimension { Id = second, AccountId = f.Account, Name = "Second dimension" });
                db.AgreementDimensionRules.Add(new AgreementDimensionRule { AccountId = f.Account, DimensionId = second, IsEnabled = true });
                db.DimensionValues.Add(new DimensionValue { Id = secondNode, AccountId = f.Account, DimensionId = second, Name = "Second root" });
                var agreement = await db.Agreements.FirstAsync(x => x.OwnerUserId == f.User && !x.IsArchived);
                db.AgreementDimensionAssignments.Add(new AgreementDimensionAssignment { Id = Guid.NewGuid(), AccountId = f.Account, AgreementId = agreement.Id, DimensionId = second, ValueId = secondNode });
                await db.SaveChangesAsync();
            }
            await Action("NavigateAsync", (Guid?)f.Parent);
            Key(widget, "dimension:" + second); await Action("DimensionChanged");
            check(Field<AgreementInsightsDto>(widget, "data").SelectedNodeId is null && Field<AgreementInsightsDto>(widget, "data").Options.All(x => x.Id != f.Parent), "dimension change resets node context and cannot reuse another dimension's node IDs");
            await using (var db = f.Factory.CreateDbContext())
            { (await db.AgreementDimensionRules.SingleAsync(x => x.DimensionId == second)).IsEnabled = false; await db.SaveChangesAsync(); }
            await Action("LoadAsync");
            check(Field<AgreementInsightGrouping>(widget, "grouping").Fixed == AgreementInsightDimension.Type && Field<bool>(widget, "contextReset"), "disabled dimension selection returns to a currently available fixed grouping");
        });
        foreach (var (language, label, totalLabel) in new[] { ("nb-NO", "Norsk dimensjon", "Unike synlige avtaler"), ("en-GB", "English dimension", "Unique visible agreements"), ("sv-SE", "Svensk dimension", "Unika synliga avtal") })
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                var state = new HostState(typeof(InsightsWidget), f.Account);
                var output = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = state }));
                Key(state.Instance!, "dimension:" + f.Dimension); await Act(state.Instance!, "DimensionChanged"); Refresh(state.Instance!);
                var html = Decode(output.ToHtmlString());
                check(html.Contains(label) && html.Contains(totalLabel) && html.Contains("dimension:" + f.Dimension), "widget localization keeps dimension ID and localized total semantics: " + language);
            });
        }
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        foreach (var race in new[] { "account", "dimension", "refresh", "late-catalog", "late-failure" }) await Race(race, check);
        await LoadingClear(check);
    }
    static AgreementInsightsDto Data(string name, int count = 2) => new([new("fixed", name, null, count)], [], count, count);
    static async Task Race(string race, Action<bool, string> check)
    {
        var firstAccount = Guid.NewGuid(); var secondAccount = Guid.NewGuid(); var dimension = Guid.NewGuid();
        var oldData = new TaskCompletionSource<AgreementInsightsDto>();
        var oldCatalog = new TaskCompletionSource<List<AgreementInsightDimensionOption>>();
        var calls = 0; var catalogs = 0;
        var proxy = DispatchProxy.Create<IAgreementService, InsightWidgetProxy>();
        ((InsightWidgetProxy)(object)proxy).Handle = (method, args) =>
        {
            if (method.Name == "GetInsightDimensionsAsync") return ++catalogs == 1 && race == "late-catalog" ? oldCatalog.Task : Task.FromResult(new List<AgreementInsightDimensionOption> { new(dimension, "Race dimension") });
            if (method.Name == "GetInsightsAsync") return ++calls == 1 && race != "late-catalog" ? oldData.Task : Task.FromResult(Data("Latest response"));
            throw new NotSupportedException(method.Name);
        };
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources").AddSingleton(proxy);
        await using var provider = services.BuildServiceProvider(); await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var host = new HostState(typeof(InsightsWidget), firstAccount);
            var output = renderer.BeginRenderingComponent<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = host }));
            var widget = (InsightsWidget)host.Instance!;
            if (race == "account")
            { typeof(InsightsWidget).GetProperty("AccountId")!.SetValue(widget, secondAccount); await Act(widget, "OnParametersSetAsync"); }
            else if (race == "dimension") { Key(widget, "dimension:" + dimension); await Act(widget, "DimensionChanged"); }
            else { typeof(InsightsWidget).GetProperty("RefreshVersion")!.SetValue(widget, 1L); await Act(widget, "OnParametersSetAsync"); }
            Refresh(widget);
            check(Decode(output.ToHtmlString()).Contains("Latest response"), "new request overtakes pending request: " + race);
            // Stub deliberately ignores cancellation, so generation checks must protect publication.
            if (race == "late-catalog") oldCatalog.SetResult([]);
            else if (race == "late-failure") oldData.SetException(new InvalidOperationException("Old failure"));
            else oldData.SetResult(Data("Stale response", 999));
            await output.QuiescenceTask; Refresh(widget);
            var html = Decode(output.ToHtmlString());
            check(html.Contains("Latest response") && !html.Contains("Stale response") && !Field<bool>(widget, "error") && !Field<bool>(widget, "loading") && (race != "late-catalog" || calls == 1), "late response/error cannot overwrite current account/group/filter/catalog state: " + race);
        });
    }
    static async Task LoadingClear(Action<bool, string> check)
    {
        var pending = new TaskCompletionSource<AgreementInsightsDto>(); var calls = 0;
        var proxy = DispatchProxy.Create<IAgreementService, InsightWidgetProxy>();
        ((InsightWidgetProxy)(object)proxy).Handle = (method, args) => method.Name switch
        {
            "GetInsightDimensionsAsync" => Task.FromResult(new List<AgreementInsightDimensionOption>()),
            "GetInsightsAsync" => ++calls == 1 ? Task.FromResult(Data("Previous counts")) : pending.Task,
            _ => throw new NotSupportedException(method.Name)
        };
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources").AddSingleton(proxy);
        await using var provider = services.BuildServiceProvider(); await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var host = new HostState(typeof(InsightsWidget), Guid.NewGuid());
            var output = await renderer.RenderComponentAsync<Host>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = host }));
            var operation = Act(host.Instance!, "LoadAsync"); Refresh(host.Instance!);
            check(!Decode(output.ToHtmlString()).Contains("Previous counts") && Field<AgreementInsightsDto?>(host.Instance!, "data") is null, "old counts cleared immediately when a newer request takes over");
            pending.SetResult(Data("Empty", 0)); await operation; Refresh(host.Instance!);
            check(Decode(output.ToHtmlString()).Contains("Ingen") && !Decode(output.ToHtmlString()).Contains("NaN") && !Decode(output.ToHtmlString()).Contains("conic-gradient"), "zero denominator renders empty state without invalid percentages or chart");
        });
    }
}
public class InsightWidgetProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Handle { get; set; } = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handle(method!, args ?? []);
}
