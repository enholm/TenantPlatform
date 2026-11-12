using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Leasing;
using TenantPlatform.Web.Components.Pages.Dimensions;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

static class ComponentChecks
{
    public static async Task Run()
    {
        var dimension = LocalizationChecks.Example();
        dimension.Revision = Guid.NewGuid();
        var value = new DimensionValue { Id = Guid.NewGuid(), AccountId = dimension.AccountId, DimensionId = dimension.Id, Name = "Untranslated leaf", Code = "LEAF" };
        var option = new LeasingDimensionOption(dimension, new LeasingDimensionRule { DimensionId = dimension.Id, AccountId = dimension.AccountId, IsEnabled = true, IsRequired = true, AllowAllocation = true }, DimensionHierarchy.Build(dimension, [value]), "nb-NO");
        var catalog = new LeasingClassificationCatalog([option], false);
        var item = new LeasingItem { Id = Guid.NewGuid(), AccountId = dimension.AccountId, Description = "Equipment", Quantity = 3, UnitPrice = 33.335m, VatPercent = 25,
            DimensionSelections = [new() { Id = Guid.NewGuid(), AccountId = dimension.AccountId, DimensionId = dimension.Id, ValueId = value.Id,
                DimensionName = "Historical recorded name", DimensionCode = dimension.Code, ValueName = value.Name, ValuePath = "Historical / leaf", ValueCode = value.Code }] };
        var storedSnapshot = JsonSerializer.Serialize(item);
        var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources");
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        foreach (var (language, name, description) in new[] { ("nb-NO", "Norsk navn", "Norsk beskrivelse"), ("en-GB", "English name", "English description"), ("sv-SE", "Svenskt namn", "Svensk beskrivning") })
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var overview = await renderer.RenderComponentAsync<DimensionOverview>(Parameters(("Dimensions", new List<Dimension> { dimension }), ("DefaultLanguage", "nb-NO")));
                var html = Decode(overview.ToHtmlString());
                Verify.That(html.Contains(name) && html.Contains(description) && html.Contains("STABLE"), "administration overview renders localized name and description: " + language);
                var choices = await renderer.RenderComponentAsync<DimensionChoices>(Parameters(("Option", option), ("Selected", new List<LeasingDimensionChoice>())));
                Verify.That(Decode(choices.ToHtmlString()).Contains(name) && Decode(choices.ToHtmlString()).Contains("Untranslated leaf"), "Leasing picker localizes dimension, preserves values: " + language);
                var current = await renderer.RenderComponentAsync<LeasingClassificationView>(Parameters(("Item", item), ("Catalog", catalog)));
                Verify.That(Decode(current.ToHtmlString()).Contains(name) && Decode(current.ToHtmlString()).Contains("Historical / leaf"), "Leasing current details localize dimension and retain historical value path: " + language);
                var history = await renderer.RenderComponentAsync<LeasingClassificationView>(Parameters(("Item", item)));
                Verify.That(Decode(history.ToHtmlString()).Contains("Historical recorded name"), "Leasing history without catalog retains recorded name: " + language);
            });
        }
        Verify.That(JsonSerializer.Serialize(item) == storedSnapshot, "rendering and language switches never rewrite Leasing snapshots");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var original = JsonSerializer.Serialize(dimension);
            Dimension? submitted = null;
            var state = new ComponentState(typeof(DimensionEditorDialog));
            state.Parameters["IsOpen"] = true; state.Parameters["Dimension"] = dimension;
            state.Parameters["OnSave"] = EventCallback.Factory.Create<Dimension>(state, (Dimension input) => { submitted = input; state.Parameters["Error"] = "DimensionInvalidInput"; state.Host!.Refresh(); });
            var output = await renderer.RenderComponentAsync<ComponentHost>(Parameters(("State", state)));
            var html = Decode(output.ToHtmlString());
            Verify.That(new[] { "dimension-tab-nb-NO", "dimension-tab-en-GB", "dimension-tab-sv-SE", "type=\"button\"", "modal-lg", "dimension-editor-form", "dimension-editor-fieldset" }.All(html.Contains) &&
                html.Contains("dimension-name-nb-NO") && !html.Contains("dimension-name-en-GB") && !html.Contains("dimension-name-sv-SE"),
                "dimension modal renders three non-submit language tabs, one active panel and the bounded scrolling layout");
            var editor = (DimensionEditorDialog)state.Component!;
            var model = (Dimension)typeof(DimensionEditorDialog).GetField("editModel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;
            model.Translations.Single(x => x.LanguageCode == "en-GB").Name = "Unsaved English";
            model.Translations.Single(x => x.LanguageCode == "sv-SE").Description = "Unsaved Swedish description";
            typeof(DimensionEditorDialog).GetMethod("SelectLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, ["sv-SE"]);
            state.Host!.Refresh();
            html = Decode(output.ToHtmlString());
            Verify.That(html.Contains("dimension-name-sv-SE") && html.Contains("Unsaved Swedish description") && !html.Contains("dimension-name-en-GB") && submitted is null &&
                (string)typeof(DimensionEditorDialog).GetField("activeLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)! == "sv-SE",
                "language tab switch preserves all input and does not submit the form");
            await (Task)typeof(DimensionEditorDialog).GetMethod("SubmitAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(editor, null)!;
            html = Decode(output.ToHtmlString());
            Verify.That(html.Contains("Unsaved Swedish description") && !html.Contains("Unsaved English") && html.Contains("role=\"alert\"") &&
                (string)typeof(DimensionEditorDialog).GetField("activeLanguage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)! == "sv-SE",
                "validation error preserves entered translations and the active language tab in the open modal");
            model.Translations.Single(x => x.LanguageCode == "en-GB").Name = new string('x', 201);
            state.Host.Refresh();
            html = Decode(output.ToHtmlString());
            Verify.That(html.Contains("id=\"dimension-tab-en-GB\"") && html.Contains("text-danger") && html.Contains("dimension-name-sv-SE"),
                "a validation error in a hidden language marks the tab without replacing the active panel");
            Verify.That(submitted?.Translations.Single(x => x.LanguageCode == "en-GB").Name == "Unsaved English" && submitted.Revision == dimension.Revision &&
                submitted.Translations.Single(x => x.LanguageCode == "en-GB").Id == dimension.Translations.Single(x => x.LanguageCode == "en-GB").Id, "modal submits a deep copy with stable translation IDs and parent revision");
            Verify.That(JsonSerializer.Serialize(dimension) == original, "unsaved translation edits never mutate register/read model");
            var history = await renderer.RenderComponentAsync<DimensionHistoryView>(Parameters(("Json", original)));
            Verify.That(Decode(history.ToHtmlString()).Contains("English name") && Decode(history.ToHtmlString()).Contains("sv-SE"), "dimension history renders translation content and language");
            var oldHistory = await renderer.RenderComponentAsync<DimensionHistoryView>(Parameters(("Json", "{\"Name\":\"Old fallback\",\"Code\":\"OLD\"}")));
            Verify.That(Decode(oldHistory.ToHtmlString()).Contains("Old fallback"), "old history without translations still renders");
            var stateNew = new ComponentState(typeof(DimensionEditorDialog)); stateNew.Parameters["IsOpen"] = true;
            var create = await renderer.RenderComponentAsync<ComponentHost>(Parameters(("State", stateNew)));
            Verify.That(Decode(create.ToHtmlString()).Contains("dimension-tab-en-GB") && Decode(create.ToHtmlString()).Contains("dimension-name-nb-NO"),
                "new-dimension modal initializes all language tabs and the default language panel without existing translations");
        });
        foreach (var (mode, inputs) in new[] { (LeasingAllocationMode.Percent, new[] { 33.3333m, 66.6667m }), (LeasingAllocationMode.NetAmount, new[] { 25m, 75.01m }), (LeasingAllocationMode.Quantity, new[] { 1m, 2m }) })
        {
            var split = JsonSerializer.Deserialize<LeasingItem>(storedSnapshot)!; split.AllocationMode = mode;
            split.AllocationRows = inputs.Select((x, i) => new LeasingAllocationRow { Id = Guid.NewGuid(), InputValue = x, Position = i }).ToList();
            var result = LeasingAllocationCalculator.Calculate(split);
            Verify.That(result.Complete && result.Rows.Sum(x => x.Net) == 100.01m && result.Rows.Sum(x => x.Vat) == 25m && result.Rows.Sum(x => x.Quantity) == 3m,
                "Leasing allocation reconciles unchanged net, VAT and quantity: " + mode);
        }
        var unsplit = LeasingAllocationCalculator.Calculate(item);
        Verify.That(unsplit.Complete && unsplit.Rows.Single().Net == 100.01m && unsplit.Rows.Single().Vat == 25m, "unsplit calculation remains unchanged");
    }
    private static ParameterView Parameters(params (string Key, object? Value)[] items) => ParameterView.FromDictionary(items.ToDictionary(x => x.Key, x => x.Value));
    private static string Decode(string html) => System.Net.WebUtility.HtmlDecode(html);
}

sealed class ComponentState(Type type)
{
    public Type Type { get; } = type;
    public Dictionary<string, object?> Parameters { get; } = [];
    public object? Component { get; set; }
    public ComponentHost? Host { get; set; }
}
sealed class ComponentHost : ComponentBase
{
    [Parameter] public ComponentState State { get; set; } = null!;
    public void Refresh() => StateHasChanged();
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        State.Host = this;
        builder.OpenComponent(0, State.Type);
        builder.AddMultipleAttributes(1, State.Parameters.Select(x => new KeyValuePair<string, object>(x.Key, x.Value!)));
        builder.AddComponentReferenceCapture(2, component => State.Component = component);
        builder.CloseComponent();
    }
}
