using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Core.Reports;
using TenantPlatform.Web.Components.Pages.Reports;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Reports;

static class ReportEditorChecks
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
        services.AddSingleton<IReportCatalogService>(new EditorCatalog());
        services.AddSingleton<IReportDefinitionCompatibilityValidator, ReportDefinitionCompatibilityValidator>();
        services.AddSingleton<IJSRuntime>(new EditorNoopJs());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var state = new EditorState { Card = Card() };
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<EditorHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = state })));
        await WaitUntilAsync(() => renderer.Dispatcher.InvokeAsync(() =>
            root.ToHtmlString().Contains("report-card-panel-visualization", StringComparison.Ordinal)).GetAwaiter().GetResult());

        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        var basics = html.IndexOf("report-editor-basics", StringComparison.Ordinal);
        var tabs = html.IndexOf("role=\"tablist\"", StringComparison.Ordinal);
        var title = html.IndexOf("report-card-name", basics, StringComparison.Ordinal);
        var titlePlacement = html.IndexOf("report-card-title-placement", basics, StringComparison.Ordinal);
        var module = html.IndexOf("report-card-module", basics, StringComparison.Ordinal);
        var subtitle = html.IndexOf("report-card-subtitle", module, StringComparison.Ordinal);
        var visualPanel = html.IndexOf("id=\"report-card-panel-visualization\"", StringComparison.Ordinal);
        var filterPanel = html.IndexOf("id=\"report-card-panel-filter\"", StringComparison.Ordinal);
        Check.That(basics >= 0 && title < titlePlacement && titlePlacement < module && module < subtitle && subtitle < tabs &&
            html.Contains("col-md-5", StringComparison.Ordinal) && html.Contains("col-md-4", StringComparison.Ordinal) &&
            html.Contains("col-md-3", StringComparison.Ordinal) && html.Contains(">Tittel</label>", StringComparison.Ordinal) &&
            html.Contains(">Undertittel</label>", StringComparison.Ordinal) && html.Contains("maxlength=\"200\"", StringComparison.Ordinal),
            "title row and optional subtitle row render responsively above the editor tabs");
        Check.That(GetModelProperty<ReportTitlePlacement>(state.Component!, "TitlePlacement") == ReportTitlePlacement.Left &&
            GetModelProperty<string>(state.Component!, "Subtitle") == "",
            "editor defaults an existing card without display settings to left title placement and empty subtitle");
        Check.That(html.Contains("id=\"report-card-tab-visualization\" class=\"nav-link active\"", StringComparison.Ordinal) &&
            html.Contains("role=\"tab\"", StringComparison.Ordinal) && html.Contains("role=\"tabpanel\"", StringComparison.Ordinal),
            "visualisation is the default accessible editor tab");
        Check.That(visualPanel >= 0 && filterPanel > visualPanel &&
            Between(html, visualPanel, filterPanel, "report-card-visualization") &&
            Between(html, visualPanel, filterPanel, "report-card-metric") &&
            Between(html, visualPanel, filterPanel, "report-card-aggregation") &&
            Between(html, visualPanel, filterPanel, "report-card-grouping") &&
            Between(html, visualPanel, filterPanel, "report-card-date-basis") &&
            Between(html, visualPanel, filterPanel, "report-card-period") &&
            html.IndexOf("report-filter-0", filterPanel, StringComparison.Ordinal) > filterPanel,
            "date, period, presentation and module-filter fields render in their intended editor panels");
        Check.That(html.Contains(">Utvalg</button>", StringComparison.Ordinal) &&
            !html[(filterPanel)..].Contains("<legend", StringComparison.Ordinal),
            "selection tab uses its localized name without a nested filter heading");

        var component = state.Component!;
        SetModelProperty(component, "Subtitle", new string('x', 201));
        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "ApplyAsync"));
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(state.Applied is null && html.Contains("alert-danger", StringComparison.Ordinal) &&
            html.Contains("200", StringComparison.Ordinal),
            "editor rejects a subtitle beyond the shared maximum before applying the working copy");

        SetModelProperty(component, "Name", "Input kept across tabs");
        SetModelProperty(component, "TitlePlacement", ReportTitlePlacement.Right);
        SetModelProperty(component, "Subtitle", "Subtitle kept across tabs");
        SetPrivateField(component, "issues", new List<ReportValidationIssue> { new("metric", "ReportDefinitionMetricInvalid") });
        SetPrivateField(component, "normalizationFields", new List<string> { "grouping" });
        await renderer.Dispatcher.InvokeAsync(() => InvokeTabAsync(component, "Filter"));
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(html.Contains("value=\"Input kept across tabs\"", StringComparison.Ordinal) &&
            html.Contains("value=\"Subtitle kept across tabs\"", StringComparison.Ordinal) &&
            html.Contains("id=\"report-card-tab-filter\" class=\"nav-link active\"", StringComparison.Ordinal) &&
            !html.Contains("alert-danger", StringComparison.Ordinal) && !html.Contains("alert-info", StringComparison.Ordinal),
            "tab changes preserve input and clear only editor-owned temporary banners");

        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "ModuleChangedAsync",
            new ChangeEventArgs { Value = ((int)ReportModule.Leasing).ToString() }));
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(html.Contains("value=\"Input kept across tabs\"", StringComparison.Ordinal) &&
            html.Contains("id=\"report-card-tab-filter\" class=\"nav-link active\"", StringComparison.Ordinal) &&
            GetModelProperty<ReportTitlePlacement>(component, "TitlePlacement") == ReportTitlePlacement.Right &&
            GetModelProperty<string>(component, "Subtitle") == "Subtitle kept across tabs",
            "module normalization preserves the title, subtitle, title placement and active editor tab");

        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "HandleTabKeyDownAsync",
            new KeyboardEventArgs { Key = "ArrowLeft" }));
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(html.Contains("id=\"report-card-tab-visualization\" class=\"nav-link active\"", StringComparison.Ordinal),
            "arrow-key tab navigation activates the adjacent editor tab");
        SetModelProperty(component, "PeriodRule", ReportPeriodRule.Custom);
        SetModelProperty(component, "CustomFrom", null);
        SetModelProperty(component, "CustomTo", null);
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        visualPanel = html.IndexOf("id=\"report-card-panel-visualization\"", StringComparison.Ordinal);
        filterPanel = html.IndexOf("id=\"report-card-panel-filter\"", StringComparison.Ordinal);
        Check.That(Between(html, visualPanel, filterPanel, "report-card-from") &&
            Between(html, visualPanel, filterPanel, "report-card-to"),
            "custom period dates render with date basis and period in the visualisation panel");
        await renderer.Dispatcher.InvokeAsync(() => InvokeTabAsync(component, "Filter"));
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "ApplyAsync"));
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(state.Applied is null && html.Contains("alert-danger", StringComparison.Ordinal) &&
            html.Contains("id=\"report-card-tab-visualization\" class=\"nav-link active\"", StringComparison.Ordinal),
            "invalid hidden period input keeps the dialog open, shows a fresh error and activates the visualisation tab");

        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "CancelAsync"));
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(!state.IsOpen && !html.Contains("report-card-editor-title", StringComparison.Ordinal),
            "Cancel closes the editor through the shared cleanup path");
        state.IsOpen = true;
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        await WaitUntilAsync(() => renderer.Dispatcher.InvokeAsync(() =>
            root.ToHtmlString().Contains("report-card-panel-visualization", StringComparison.Ordinal)).GetAwaiter().GetResult());
        html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(!html.Contains("alert-danger", StringComparison.Ordinal) && !html.Contains("alert-info", StringComparison.Ordinal) &&
            html.Contains("id=\"report-card-tab-visualization\" class=\"nav-link active\"", StringComparison.Ordinal),
            "reopening the same card starts on visualisation without stale editor banners");

        component = state.Component!;
        SetPrivateField(component, "issues", new List<ReportValidationIssue> { new("metric", "ReportDefinitionMetricInvalid") });
        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "HandleDialogKeyDownAsync",
            new KeyboardEventArgs { Key = "Escape" }));
        Check.That(!state.IsOpen, "Escape closes the editor through the same cleanup path");
        state.IsOpen = true;
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        await WaitUntilAsync(() => renderer.Dispatcher.InvokeAsync(() =>
            root.ToHtmlString().Contains("report-card-panel-visualization", StringComparison.Ordinal)).GetAwaiter().GetResult());
        component = state.Component!;
        SetModelProperty(component, "TitlePlacement", ReportTitlePlacement.Center);
        SetModelProperty(component, "Subtitle", "Applied subtitle");
        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(component, "ApplyAsync"));
        Check.That(state.Applied?.Definition.TitlePlacement == ReportTitlePlacement.Center &&
            state.Applied.Definition.Subtitle == "Applied subtitle" && !state.IsOpen,
            "successful apply returns the selected title placement and subtitle and closes through shared cleanup");

        await VerifyLateCatalogMessageAsync();
    }

    private static async Task VerifyLateCatalogMessageAsync()
    {
        var catalog = new DelayedEditorCatalog();
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
        services.AddSingleton<IReportCatalogService>(catalog);
        services.AddSingleton<IReportDefinitionCompatibilityValidator, ReportDefinitionCompatibilityValidator>();
        services.AddSingleton<IJSRuntime>(new EditorNoopJs());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        var state = new EditorState { Card = Card() };
        var root = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent<EditorHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = state })));
        await WaitUntilAsync(() => state.Component is not null && catalog.Calls == 1);
        await renderer.Dispatcher.InvokeAsync(() => InvokePrivateAsync(state.Component!, "CancelAsync"));
        catalog.Fail(0, new UnauthorizedAccessException());
        await Task.Delay(20);

        state.IsOpen = true;
        await renderer.Dispatcher.InvokeAsync(state.Host!.Refresh);
        await WaitUntilAsync(() => catalog.Calls == 2);
        catalog.Complete(1, EditorCatalog.Create(ReportModule.Agreements));
        await WaitUntilAsync(() => renderer.Dispatcher.InvokeAsync(() =>
            root.ToHtmlString().Contains("report-card-panel-visualization", StringComparison.Ordinal)).GetAwaiter().GetResult());
        var html = await renderer.Dispatcher.InvokeAsync(root.ToHtmlString);
        Check.That(!html.Contains("alert-danger", StringComparison.Ordinal) && !html.Contains("ReportCardUnavailable", StringComparison.Ordinal),
            "a late catalog failure from a closed editor cannot restore a stale banner after reopening");
    }

    private static bool Between(string value, int start, int end, string expected)
    {
        var position = value.IndexOf(expected, start, StringComparison.Ordinal);
        return position >= start && position < end;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(5);
        if (!predicate()) throw new TimeoutException("Timed out waiting for report editor state.");
    }

    private static async Task InvokeTabAsync(ReportCardEditorDialog component, string tab)
    {
        var type = component.GetType();
        var tabType = type.GetNestedType("EditorTab", BindingFlags.NonPublic)!;
        await InvokePrivateAsync(component, "SelectTabAsync", Enum.Parse(tabType, tab), false);
    }

    private static async Task InvokePrivateAsync(object instance, string method, params object?[] args)
    {
        var candidate = instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(instance.GetType().Name, method);
        if (candidate.Invoke(instance, args) is Task task) await task;
    }

    private static void SetModelProperty(ReportCardEditorDialog component, string property, object? value)
    {
        var model = component.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        model.GetType().GetProperty(property)!.SetValue(model, value);
    }

    private static T GetModelProperty<T>(ReportCardEditorDialog component, string property)
    {
        var model = component.GetType().GetField("model", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component)!;
        return (T)model.GetType().GetProperty(property)!.GetValue(model)!;
    }

    private static void SetPrivateField<T>(ReportCardEditorDialog component, string field, T value) =>
        component.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private static ReportCardDraft Card() => new()
    {
        ClientId = Guid.NewGuid(),
        Name = "Editor card",
        Definition = new(ReportModule.Agreements, 1,
            [new ReportValueFilterSelection(new AgreementReportFilterKey(AgreementReportFilter.Search), new ReportTextValue("before"))],
            new(ReportPeriodRule.CurrentYear), ReportDateBasis.AgreementPeriodOverlap,
            ReportMetric.AgreementCount, ReportAggregation.CountDistinct,
            new AgreementReportGroupingKey(AgreementReportGrouping.Type), null, ReportVisualization.Bar),
        GridColumn = 1,
        GridRow = 1,
        GridWidth = 6,
        GridHeight = 4
    };

    private sealed class EditorCatalog : IReportCatalogService
    {
        public Task<ReportModuleCatalog> GetAsync(Guid accountId, ReportModule module, CancellationToken ct = default)
            => Task.FromResult(Create(module));

        public static ReportModuleCatalog Create(ReportModule module)
        {
            var search = module == ReportModule.Agreements
                ? (ReportFilterKey)new AgreementReportFilterKey(AgreementReportFilter.Search)
                : new LeasingReportFilterKey(LeasingCatalogFilter.Search);
            var grouping = module == ReportModule.Agreements
                ? (ReportGroupingKey)new AgreementReportGroupingKey(AgreementReportGrouping.Type)
                : new LeasingReportGroupingKey(LeasingReportGrouping.Kind);
            return new ReportModuleCatalog(module,
                [new(search, ReportCatalogLabel.Resource("Search"), ReportFilterValueKind.Text, [])],
                new DimensionListFilterCatalog([]),
                module == ReportModule.Agreements ? ReportDimensionScope.Agreement : ReportDimensionScope.LeasingAgreement,
                [new(grouping, ReportCatalogLabel.Dynamic("Grouping"))], [], ReportCompatibilityMatrix.For(module));
        }

        public Task ValidateAsync(Guid accountId, ReportModule module,
            IReadOnlyCollection<ReportFilterSelection> selections, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class DelayedEditorCatalog : IReportCatalogService
    {
        private readonly List<TaskCompletionSource<ReportModuleCatalog>> pending = [];
        public int Calls { get { lock (pending) return pending.Count; } }

        public Task<ReportModuleCatalog> GetAsync(Guid accountId, ReportModule module, CancellationToken ct = default)
        {
            var completion = new TaskCompletionSource<ReportModuleCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (pending) pending.Add(completion);
            return completion.Task;
        }

        public Task ValidateAsync(Guid accountId, ReportModule module,
            IReadOnlyCollection<ReportFilterSelection> selections, CancellationToken ct = default) => Task.CompletedTask;

        public void Complete(int index, ReportModuleCatalog result)
        {
            lock (pending) pending[index].TrySetResult(result);
        }

        public void Fail(int index, Exception error)
        {
            lock (pending) pending[index].TrySetException(error);
        }
    }

    private sealed class EditorNoopJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new InvalidOperationException("Static renderer has no JavaScript runtime."));
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}

sealed class EditorState
{
    public Guid AccountId { get; } = Guid.NewGuid();
    public required ReportCardDraft Card { get; init; }
    public bool IsOpen { get; set; } = true;
    public ReportCardDraft? Applied { get; set; }
    public ReportCardEditorDialog? Component { get; set; }
    public EditorHost? Host { get; set; }
}

sealed class EditorHost : ComponentBase
{
    [Parameter] public EditorState State { get; set; } = null!;
    public void Refresh() => StateHasChanged();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        State.Host = this;
        builder.OpenComponent<ReportCardEditorDialog>(0);
        builder.AddAttribute(1, "AccountId", State.AccountId);
        builder.AddAttribute(2, "IsOpen", State.IsOpen);
        builder.AddAttribute(3, "Card", State.Card);
        builder.AddAttribute(4, "OnApply", EventCallback.Factory.Create<ReportCardDraft>(this, card =>
        {
            State.Applied = card;
            State.IsOpen = false;
            StateHasChanged();
        }));
        builder.AddAttribute(5, "OnCancel", EventCallback.Factory.Create(this, () =>
        {
            State.IsOpen = false;
            StateHasChanged();
        }));
        builder.AddComponentReferenceCapture(6, value => State.Component = (ReportCardEditorDialog)value);
        builder.CloseComponent();
    }
}
