using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Components.Pages.Reports;
using TenantPlatform.Web.Services.Reports;

static class ReportUiChecks
{
    public static async Task RunAsync()
    {
        var routes = typeof(TenantPlatform.Web.Components.Pages.Reports.ReportPage).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Select(x => x.Template).ToList();
        Check.That(routes.Contains("/reports/{ReportPageId:guid}"), "report detail component exposes the stable typed direct URL");
        Check.That(typeof(ReportsOverview).GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>()
            .Any(x => x.Template == "/reports"), "report overview component exposes the stable overview URL");

        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
        var execution = new UiExecution();
        services.AddSingleton<IReportExecutionService>(execution);
        services.AddSingleton<IReportTreeService>(new UiTree());
        services.AddSingleton<IJSRuntime>(new NoopJsRuntime());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        foreach (var culture in new[] { "nb-NO", "en-GB", "sv-SE" })
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<ReportCardView>(ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["AccountId"] = Guid.NewGuid(), ["Card"] = Card()
                }));
                var html = System.Net.WebUtility.HtmlDecode(output.ToHtmlString());
                Check.That(html.Contains("<article") && html.Contains("report-chart") && html.Contains("role=\"img\"") &&
                    html.Contains("<table") && html.Contains("<caption") &&
                    !html.Contains(">Edit<") && !html.Contains(">Rediger<") && !html.Contains(">Redigera<"),
                    "view mode renders an accessible tabular chart fallback without edit controls: " + culture);
            });
        }
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var card = Card();
            var before = execution.Calls;
            var cardState = new ReportCardTestState { AccountId = Guid.NewGuid(), Card = card };
            _ = await renderer.RenderComponentAsync<ReportCardTestHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["State"] = cardState }));
            cardState.Card = card with { GridWidth = 6, GridHeight = 8 };
            cardState.Host!.Refresh();
            Check.That(execution.Calls == before + 1,
                "layout-only card rerender reuses the report result and does not execute the query again");

            var treeBefore = execution.Calls;
            var tree = await renderer.RenderComponentAsync<ReportCardView>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["AccountId"] = Guid.NewGuid(), ["Card"] = TreeCard() }));
            var treeHtml = tree.ToHtmlString();
            Check.That(treeHtml.Contains("role=\"tree\"") && treeHtml.Contains("role=\"treeitem\"") &&
                treeHtml.Contains("report-tree-chart") && Occurrences(treeHtml, "type=\"button\"") >= 3 &&
                execution.Calls == treeBefore,
                "relationship-tree card renders separate accessible expand/detail controls without aggregate execution");

            RenderFragment<ReportGridItem> template = item => builder => builder.AddContent(0, item.ClientId.ToString("N"));
            var item = new ReportGridItem(Guid.NewGuid(), 1, 1, 6, 4, 0);
            var readOnly = await renderer.RenderComponentAsync<ReportGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Items"] = new List<ReportGridItem> { item }, ["ItemTemplate"] = template, ["Editable"] = false
            }));
            var callbackCount = 0;
            var gridState = new ReportGridTestState
            {
                Items = [item], Template = template, Editable = true, Generation = 9,
                Changed = EventCallback.Factory.Create<ReportGridLayoutChange>(new object(), _ => callbackCount++)
            };
            var editable = await renderer.RenderComponentAsync<ReportGridTestHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["State"] = gridState }));
            var readOnlyHtml = readOnly.ToHtmlString();
            var editableHtml = editable.ToHtmlString();
            Check.That(!readOnlyHtml.Contains("report-drag-handle") && !readOnlyHtml.Contains("report-resize-handle") &&
                editableHtml.Contains("report-drag-handle") && editableHtml.Contains("report-resize-handle") &&
                editableHtml.Contains("type=\"button\"") && editableHtml.Contains("aria-label"),
                "grid renders pointer handles only in edit mode with keyboard-focusable accessible buttons");
            await gridState.Component!.CommitLayout(item.ClientId.ToString("N"), 2, 2, 6, 4, 8);
            Check.That(callbackCount == 0, "stale layout callback is rejected before it can mutate a newer workspace");

            var keyboard = await renderer.RenderComponentAsync<ReportLayoutKeyboardControls>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Name"] = "Card" }));
            var keyboardHtml = keyboard.ToHtmlString();
            Check.That(keyboardHtml.Contains("role=\"group\"") &&
                Occurrences(keyboardHtml, "type=\"button\"") == 4 && Occurrences(keyboardHtml, "aria-label") >= 5,
                "keyboard resize alternative renders four labelled non-submit controls");
        });
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
    }

    private static int Occurrences(string value, string search) =>
        (value.Length - value.Replace(search, "", StringComparison.Ordinal).Length) / search.Length;

    private sealed class NoopJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new InvalidOperationException("Static renderer has no JavaScript runtime."));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private static ReportCardDto Card() => new(Guid.NewGuid(), "Count",
        new(ReportModule.Agreements, 1, [], new(ReportPeriodRule.CurrentYear),
            ReportDateBasis.AgreementPeriodOverlap, ReportMetric.AgreementCount,
            ReportAggregation.CountDistinct, Grouping: new AgreementReportGroupingKey(AgreementReportGrouping.Type),
            Visualization: ReportVisualization.Bar), 1, 1, 12, 4, 0);

    private static ReportCardDto TreeCard() => new(Guid.NewGuid(), "Relationships",
        new(ReportModule.Agreements, 1, [], new(ReportPeriodRule.CurrentYear),
            ReportDateBasis.AgreementPeriodOverlap, Visualization: ReportVisualization.RelationshipTree),
        1, 1, 12, 6, 0);

    private sealed class UiExecution : IReportExecutionService
    {
        public int Calls { get; private set; }
        public Task<ReportExecutionResult> ExecuteAsync(Guid accountId, ReportExecutionRequest request, CancellationToken ct = default) =>
            Execute();
        private Task<ReportExecutionResult> Execute()
        {
            Calls++;
            return Task.FromResult(new ReportExecutionResult(ReportModule.Agreements, ReportMetric.AgreementCount,
                new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), new(2026, 11, 13), "Europe/Oslo",
                [new(2)], [new("type:1", ReportExecutionLabel.Dynamic("Type"), [new(2)], 2)], [], null,
                new(ReportGrain.Agreement, ReportValueUnit.Count, ReportCurrencyRule.None,
                    ReportAdditivity.UniqueBeforeGroupExpansion, true, true, false, false, 2, 2, "test", [])));
        }
    }

    private sealed class UiTree : IReportTreeService
    {
        private static readonly Guid Account = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly Guid Agreement = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        private static readonly ReportTreeNode Root = new(
            new(ReportModule.Agreements, ReportTreeNodeType.Account, Account), "Account", null, true, null);
        private static readonly ReportTreeNode Child = new(
            new(ReportModule.Agreements, ReportTreeNodeType.Agreement, Agreement), "Agreement", "Context", false, Root.Id);

        public Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition, CancellationToken ct = default) =>
            Task.FromResult(Root);
        public Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition,
            ReportTreeNodeId parentId, int offset = 0, int pageSize = 50, CancellationToken ct = default) =>
            Task.FromResult(new ReportTreeNodePage(parentId == Root.Id ? [Child] : [], offset,
                parentId == Root.Id ? 1 : 0, null));
        public Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId, ReportCardDefinition definition,
            string query, int limit = 25, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportTreeSearchHit>>([]);
        public Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition,
            ReportTreeNodeId nodeId, CancellationToken ct = default) => Task.FromResult(new ReportTreeNodeDetails(
                nodeId == Root.Id ? Root : Child, nodeId == Root.Id ? [] : [new(Root.Id, Root.Name)], [],
                nodeId == Child.Id ? $"/agreements/{Agreement}" : null));
    }
}

sealed class ReportCardTestState
{
    public Guid AccountId { get; init; }
    public required ReportCardDto Card { get; set; }
    public ReportCardTestHost? Host { get; set; }
}

sealed class ReportCardTestHost : ComponentBase
{
    [Parameter] public ReportCardTestState State { get; set; } = null!;
    public void Refresh() => StateHasChanged();
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        State.Host = this;
        builder.OpenComponent<ReportCardView>(0);
        builder.SetKey(State.Card.Id);
        builder.AddAttribute(1, "AccountId", State.AccountId);
        builder.AddAttribute(2, "Card", State.Card);
        builder.CloseComponent();
    }
}

sealed class ReportGridTestState
{
    public IReadOnlyList<ReportGridItem> Items { get; init; } = [];
    public RenderFragment<ReportGridItem> Template { get; init; } = null!;
    public bool Editable { get; init; }
    public long Generation { get; init; }
    public EventCallback<ReportGridLayoutChange> Changed { get; init; }
    public ReportGrid? Component { get; set; }
}

sealed class ReportGridTestHost : ComponentBase
{
    [Parameter] public ReportGridTestState State { get; set; } = null!;
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<ReportGrid>(0);
        builder.AddAttribute(1, "Items", State.Items);
        builder.AddAttribute(2, "ItemTemplate", State.Template);
        builder.AddAttribute(3, "Editable", State.Editable);
        builder.AddAttribute(4, "Generation", State.Generation);
        builder.AddAttribute(5, "OnLayoutChanged", State.Changed);
        builder.AddComponentReferenceCapture(6, value => State.Component = (ReportGrid)value);
        builder.CloseComponent();
    }
}
