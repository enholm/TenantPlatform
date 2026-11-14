using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Core.Agreements;
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
        var treeService = new UiTree();
        services.AddSingleton<IReportExecutionService>(execution);
        services.AddSingleton<IReportTreeService>(treeService);
        services.AddSingleton<IReportPreviewSession>(new UiPreview(execution, treeService));
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
                    ["AccountId"] = Guid.NewGuid(), ["SessionId"] = Guid.NewGuid(), ["Card"] = Card()
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
            cardState.Card = cardState.Card with { Name = "Renamed without data change" };
            cardState.Host.Refresh();
            Check.That(execution.Calls == before + 1,
                "card name changes update presentation without executing the report again");
            cardState.Card = cardState.Card with
            {
                Definition = cardState.Card.Definition with
                {
                    Filters = [new ReportValueFilterSelection(
                        new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("changed"))]
                }
            };
            cardState.Host.Refresh();
            Check.That(execution.Calls == before + 2,
                "a run-relevant working-copy definition change executes a new preview");

            var treeBefore = execution.Calls;
            var tree = await renderer.RenderComponentAsync<ReportCardView>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["AccountId"] = Guid.NewGuid(), ["SessionId"] = Guid.NewGuid(), ["Card"] = TreeCard() }));
            var treeHtml = tree.ToHtmlString();
            Check.That(treeHtml.Contains("role=\"tree\"") && treeHtml.Contains("role=\"treeitem\"") &&
                treeHtml.Contains("report-tree-chart") && Occurrences(treeHtml, "type=\"button\"") >= 3 &&
                execution.Calls == treeBefore && treeService.RootCalls == 1 && treeService.ChildrenCalls == 1,
                "relationship-tree card reuses its first-level preview in the interactive tree without aggregate or duplicate tree execution");

            RenderFragment<ReportGridItem> template = item => builder => builder.AddContent(0, item.ClientId.ToString("N"));
            var item = new ReportGridItem(Guid.NewGuid(), 1, 1, 6, 4, 0);
            var readOnly = await renderer.RenderComponentAsync<ReportGrid>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Items"] = new List<ReportGridItem> { item }, ["ItemTemplate"] = template, ["Editable"] = false
            }));
            var callbackCount = 0;
            Guid? selectedId = null;
            var gridState = new ReportGridTestState
            {
                Items = [item], Template = template, Editable = true, Generation = 9,
                SelectedClientId = item.ClientId,
                Changed = EventCallback.Factory.Create<ReportGridLayoutChange>(new object(), _ => callbackCount++),
                SelectionChanged = EventCallback.Factory.Create<Guid?>(new object(), id => selectedId = id)
            };
            var editable = await renderer.RenderComponentAsync<ReportGridTestHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["State"] = gridState }));
            var readOnlyHtml = readOnly.ToHtmlString();
            var editableHtml = editable.ToHtmlString();
            Check.That(!readOnlyHtml.Contains("report-resize-handle") && !readOnlyHtml.Contains("aria-current=\"true\"") &&
                !editableHtml.Contains("report-drag-handle") && editableHtml.Contains("report-resize-handle") &&
                editableHtml.Contains("aria-current=\"true\"") && editableHtml.Contains("tabindex=\"0\"") &&
                editableHtml.Contains("type=\"button\"") && editableHtml.Contains("aria-label"),
                "edit grid exposes one selected keyboard-focusable card and resize only for that card");
            await gridState.Component!.CommitLayout(item.ClientId.ToString("N"), 2, 2, 6, 4, 8);
            Check.That(callbackCount == 0, "stale layout callback is rejected before it can mutate a newer workspace");
            await gridState.Component.SelectCard(item.ClientId.ToString("N"), 8);
            Check.That(selectedId is null, "stale selection callback is rejected before it can select in a newer workspace");
            await gridState.Component.SelectCard(item.ClientId.ToString("N"), 9);
            Check.That(selectedId == item.ClientId, "selection callback preserves the stable card id");

            var keyboard = await renderer.RenderComponentAsync<ReportLayoutKeyboardControls>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { ["Name"] = "Card" }));
            var keyboardHtml = keyboard.ToHtmlString();
            Check.That(keyboardHtml.Contains("role=\"group\"") &&
                Occurrences(keyboardHtml, "type=\"button\"") == 4 && Occurrences(keyboardHtml, "aria-label") >= 5,
                "keyboard resize alternative renders four labelled non-submit controls");

            var palette = await renderer.RenderComponentAsync<ReportLayoutPalette>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Name"] = "Card", ["Disabled"] = true,
                    ["TargetPages"] = new List<ReportPageSummaryDto>
                    {
                        new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Guid.NewGuid(), "Allowed target",
                            Guid.NewGuid(), 0, DateTimeOffset.UtcNow)
                    }
                }));
            var paletteHtml = palette.ToHtmlString();
            Check.That(paletteHtml.Contains("report-layout-palette") && paletteHtml.Contains("report-palette-drag-handle") &&
                paletteHtml.Contains("Allowed target") && paletteHtml.Contains("value=\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"") &&
                Occurrences(paletteHtml, "disabled") == 11 && Occurrences(paletteHtml, "type=\"button\"") == 12,
                "shared palette renders all card actions, allowed target pages and disabled commands without a selection");

            var passiveTree = await renderer.RenderComponentAsync<ReportCardView>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    ["AccountId"] = Guid.NewGuid(), ["SessionId"] = Guid.NewGuid(),
                    ["Card"] = TreeCard(), ["Passive"] = true
                }));
            var passiveTreeHtml = passiveTree.ToHtmlString();
            Check.That(passiveTreeHtml.Contains("data-report-preview-passive=\"true\"") &&
                passiveTreeHtml.Contains("report-tree-preview") &&
                !passiveTreeHtml.Contains("<button") && !passiveTreeHtml.Contains("<a "),
                "passive relationship preview uses real tree results without focusable tree controls or links");

            var passiveTable = await renderer.RenderComponentAsync<ReportCardView>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    ["AccountId"] = Guid.NewGuid(), ["SessionId"] = Guid.NewGuid(),
                    ["Card"] = TableCard(), ["Passive"] = true
                }));
            var activeTable = await renderer.RenderComponentAsync<ReportCardView>(ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    ["AccountId"] = Guid.NewGuid(), ["SessionId"] = Guid.NewGuid(), ["Card"] = TableCard()
                }));
            Check.That(passiveTable.ToHtmlString().Contains("Agreement row") &&
                !passiveTable.ToHtmlString().Contains("href=\"/agreements/") &&
                activeTable.ToHtmlString().Contains("href=\"/agreements/"),
                "passive table preserves real row text but removes record navigation only in edit mode");
        });
        await VerifyLateCardResponsesAsync();
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
    }

    private static async Task VerifyLateCardResponsesAsync()
    {
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
        var preview = new DelayedPreview();
        services.AddSingleton<IReportPreviewSession>(preview);
        services.AddSingleton<IReportTreeService>(new UiTree());
        services.AddSingleton<IJSRuntime>(new NoopJsRuntime());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var state = new ReportCardTestState { AccountId = Guid.NewGuid(), Card = Card() };
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<ReportCardTestHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = state })));
        await WaitUntilAsync(() => preview.Calls == 1);

        state.Card = state.Card with
        {
            Definition = state.Card.Definition with
            {
                Filters = [new ReportValueFilterSelection(
                    new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("new"))]
            }
        };
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        await WaitUntilAsync(() => preview.Calls == 2);
        preview.Complete(1, UiResult(99));
        await WaitUntilAsync(() => renderer.Dispatcher.InvokeAsync(() => root.ToHtmlString().Contains("99,00") ||
            root.ToHtmlString().Contains("99.00")).GetAwaiter().GetResult());
        preview.Complete(0, UiResult(1));
        await Task.Delay(20);
        var afterLateResult = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That((afterLateResult.Contains("99,00") || afterLateResult.Contains("99.00")) &&
            !afterLateResult.Contains("1,00") && !afterLateResult.Contains("1.00"),
            "a late result for an older working-copy definition cannot replace the current card preview");

        state.Card = state.Card with
        {
            Definition = state.Card.Definition with
            {
                Filters = [new ReportValueFilterSelection(
                    new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("third"))]
            }
        };
        await renderer.Dispatcher.InvokeAsync(state.Host.Refresh);
        await WaitUntilAsync(() => preview.Calls == 3);
        state.Card = state.Card with
        {
            Definition = state.Card.Definition with
            {
                Filters = [new ReportValueFilterSelection(
                    new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("fourth"))]
            }
        };
        await renderer.Dispatcher.InvokeAsync(state.Host.Refresh);
        await WaitUntilAsync(() => preview.Calls == 4);
        preview.Complete(3, UiResult(77));
        await WaitUntilAsync(() => renderer.Dispatcher.InvokeAsync(() => root.ToHtmlString().Contains("77,00") ||
            root.ToHtmlString().Contains("77.00")).GetAwaiter().GetResult());
        preview.Fail(2, new ReportExecutionException("ReportCardLoadFailed"));
        await Task.Delay(20);
        var afterLateError = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That((afterLateError.Contains("77,00") || afterLateError.Contains("77.00")) &&
            !afterLateError.Contains("alert-warning"),
            "a late error for an older definition neither clears the current result nor becomes visible");
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(5);
        if (!predicate()) throw new TimeoutException("Timed out waiting for report component state.");
    }

    private static ReportExecutionResult UiResult(decimal value) => new(ReportModule.Agreements,
        ReportMetric.AgreementCount, new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)),
        new(2026, 11, 13), "Europe/Oslo", [new(value)],
        [new("type:1", ReportExecutionLabel.Dynamic("Type"), [new(value)], 1)], [], null,
        new(ReportGrain.Agreement, ReportValueUnit.Count, ReportCurrencyRule.None,
            ReportAdditivity.UniqueBeforeGroupExpansion, true, true, false, false, 1, 1, "test", []));

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

    private static ReportCardDto TableCard() => new(Guid.NewGuid(), "Table",
        new(ReportModule.Agreements, 1, [], new(ReportPeriodRule.CurrentYear),
            ReportDateBasis.AgreementPeriodOverlap, ReportMetric.AgreementCount,
            ReportAggregation.CountDistinct, Visualization: ReportVisualization.Table),
        1, 1, 12, 6, 0);

    private sealed class UiExecution : IReportExecutionService
    {
        public int Calls { get; private set; }
        public Task<ReportExecutionResult> ExecuteAsync(Guid accountId, ReportExecutionRequest request, CancellationToken ct = default) =>
            Execute(request);
        private Task<ReportExecutionResult> Execute(ReportExecutionRequest request)
        {
            Calls++;
            return Task.FromResult(new ReportExecutionResult(ReportModule.Agreements, ReportMetric.AgreementCount,
                new(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)), new(2026, 11, 13), "Europe/Oslo",
                [new(2)], [new("type:1", ReportExecutionLabel.Dynamic("Type"), [new(2)], 2)], [],
                request.Definition.Visualization == ReportVisualization.Table
                    ? new ReportTableResult(1, 50, 1,
                        [new ReportAgreementTableRow("row", Guid.NewGuid(), "Agreement row", "Counterparty", "Owner", AgreementType.Lease, AgreementStatus.Active)])
                    : null,
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
        public int RootCalls { get; private set; }
        public int ChildrenCalls { get; private set; }

        public Task<ReportTreeNode> GetRootAsync(Guid accountId, ReportCardDefinition definition, CancellationToken ct = default)
        {
            RootCalls++;
            return Task.FromResult(Root);
        }
        public Task<ReportTreeNodePage> GetChildrenAsync(Guid accountId, ReportCardDefinition definition,
            ReportTreeNodeId parentId, int offset = 0, int pageSize = 50, CancellationToken ct = default)
        {
            ChildrenCalls++;
            return Task.FromResult(new ReportTreeNodePage(parentId == Root.Id ? [Child] : [], offset,
                parentId == Root.Id ? 1 : 0, null));
        }
        public Task<IReadOnlyList<ReportTreeSearchHit>> SearchAsync(Guid accountId, ReportCardDefinition definition,
            string query, int limit = 25, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReportTreeSearchHit>>([]);
        public Task<ReportTreeNodeDetails> GetNodeAsync(Guid accountId, ReportCardDefinition definition,
            ReportTreeNodeId nodeId, CancellationToken ct = default) => Task.FromResult(new ReportTreeNodeDetails(
                nodeId == Root.Id ? Root : Child, nodeId == Root.Id ? [] : [new(Root.Id, Root.Name)], [],
                nodeId == Child.Id ? $"/agreements/{Agreement}" : null));
    }

    private sealed class UiPreview(UiExecution execution, UiTree trees) : IReportPreviewSession
    {
        public ReportPreviewSessionSnapshot Snapshot => new(0, 0, 0);
        public Task<ReportExecutionResult> ExecuteAsync(Guid sessionId, Guid accountId,
            ReportExecutionRequest request, bool forceRefresh = false, CancellationToken ct = default) =>
            execution.ExecuteAsync(accountId, request, ct);
        public async Task<ReportTreePreviewResult> LoadTreeAsync(Guid sessionId, Guid accountId,
            ReportCardDefinition definition, bool forceRefresh = false, CancellationToken ct = default)
        {
            var root = await trees.GetRootAsync(accountId, definition, ct);
            var children = await trees.GetChildrenAsync(accountId, definition, root.Id, ct: ct);
            return new(root, children.Items, children.NextOffset);
        }
        public void EndSession(Guid sessionId) { }
    }

    private sealed class DelayedPreview : IReportPreviewSession
    {
        private readonly List<TaskCompletionSource<ReportExecutionResult>> pending = [];
        public int Calls { get { lock (pending) return pending.Count; } }
        public ReportPreviewSessionSnapshot Snapshot => new(0, 0, 0);
        public Task<ReportExecutionResult> ExecuteAsync(Guid sessionId, Guid accountId,
            ReportExecutionRequest request, bool forceRefresh = false, CancellationToken ct = default)
        {
            var completion = new TaskCompletionSource<ReportExecutionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pending) pending.Add(completion);
            return completion.Task;
        }
        public Task<ReportTreePreviewResult> LoadTreeAsync(Guid sessionId, Guid accountId,
            ReportCardDefinition definition, bool forceRefresh = false, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public void EndSession(Guid sessionId) { }
        public void Complete(int index, ReportExecutionResult result)
        {
            lock (pending) pending[index].TrySetResult(result);
        }
        public void Fail(int index, Exception error)
        {
            lock (pending) pending[index].TrySetException(error);
        }
    }
}

sealed class ReportCardTestState
{
    public Guid AccountId { get; init; }
    public Guid SessionId { get; init; } = Guid.NewGuid();
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
        builder.AddAttribute(2, "SessionId", State.SessionId);
        builder.AddAttribute(3, "Card", State.Card);
        builder.CloseComponent();
    }
}

sealed class ReportGridTestState
{
    public IReadOnlyList<ReportGridItem> Items { get; init; } = [];
    public RenderFragment<ReportGridItem> Template { get; init; } = null!;
    public bool Editable { get; init; }
    public long Generation { get; init; }
    public Guid? SelectedClientId { get; init; }
    public EventCallback<ReportGridLayoutChange> Changed { get; init; }
    public EventCallback<Guid?> SelectionChanged { get; init; }
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
        builder.AddAttribute(6, "SelectedClientId", State.SelectedClientId);
        builder.AddAttribute(7, "OnSelectionChanged", State.SelectionChanged);
        builder.AddComponentReferenceCapture(8, value => State.Component = (ReportGrid)value);
        builder.CloseComponent();
    }
}
