using System.Reflection;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Web.Components.Pages.Dimensions;
using TenantPlatform.Web.Services.Dimensions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class LeasingComponentChecks
{
    public static async Task Run(LeasingService service, Guid account, Guid user, Guid framework, Guid acquisition, TenantPlatform.Web.Services.Dimensions.DimensionService? dimensions = null)
    {
        await Render(typeof(LeasingEditor), $"leasing/frameworks/{framework}/edit", framework, html =>
            Require(html.Contains("lease-from") && html.Contains("lease-to") && html.Contains("lease-limit") && html.Contains("Standardvilkår"), "framework editor has period, capacity and terms"));
        await Render(typeof(LeasingEditor), $"leasing/acquisitions/{acquisition}/edit", acquisition, html =>
            Require(html.Contains("item-description-0") && html.Contains("lease-financed") && html.Contains("lease-invoice-number"), "acquisition editor renders stable line controls and separate financing"));
        await Render(typeof(LeasingDetailsPage), $"leasing/frameworks/{framework}", framework, html =>
            Require(html.Contains("Tilgjengelig beløp") && html.Contains("2032") && html.Contains("Endringshistorikk"), "framework details show remaining limit, individual lease dates and history"));
        await Render(typeof(LeasingDetailsPage), $"leasing/acquisitions/{acquisition}", acquisition, html =>
            Require(html.Contains("invoice.pdf") && html.Contains("/download") && html.Contains("Kanseller anskaffelsen"), "acquisition details provide documents and cancellation"));

        if (dimensions != null)
        {
            var dimension = await dimensions.SaveDimensionAsync(null, new() { Name = "Hierarchy test", Code = "HIERARCHY" });
            var network = await dimensions.SaveValueAsync(dimension, null, new() { DimensionId = dimension, Name = "Network", Code = "NET" });
            var router = await dimensions.SaveValueAsync(dimension, null, new() { DimensionId = dimension, ParentId = network, Name = "Router", Code = "RTR" });
            await dimensions.SaveValueAsync(dimension, null, new() { DimensionId = dimension, ParentId = router, Name = "Router type", Code = "TYPE" });
            await Render(typeof(TenantPlatform.Web.Components.Pages.Dimensions.DimensionsPage), "settings/dimensions", Guid.Empty, html =>
                Require(html.Contains("Dimensjoner") && html.Contains("Department") && html.Contains("form-control") && html.Contains("col-lg-4"), "shared dimension register renders with responsive cards and labelled controls"));
            await Render(typeof(LeasingEditor), $"leasing/acquisitions/{acquisition}/edit", acquisition, html =>
                Require(html.Contains("Klassifisering og kostnadsfordeling") && html.Contains("Technology") && html.Contains("type=\"search\"") && html.Contains("<summary>"), "classification editor renders searchable paths and keyboard-accessible expandable controls"));
        }
        async Task Render(Type type, string path, Guid id, Action<string> check)
        {
            var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
            if (dimensions != null) services.AddSingleton(dimensions);
            services.AddSingleton(service); services.AddSingleton<ICurrentUserContextService>(new Context(user, account));
            services.AddSingleton<NavigationManager>(new TestNavigation(path)); services.AddSingleton<IJSRuntime>(new NoJs());
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new TestRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = renderer.BeginRenderingComponent(type, ParameterView.FromDictionary(id == Guid.Empty ? new Dictionary<string, object?>() : new Dictionary<string, object?> { ["Id"] = id }));
                await root.QuiescenceTask;
                var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Require(!html.Contains("Kunne ikke fullføre"), "component loads without error"); check(html);
                if (dimensions != null && type == typeof(LeasingEditor) && path.Contains("/acquisitions/"))
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var editor = renderer.LeasingPage!;
                    var purchase = (TenantPlatform.Core.Leasing.LeasingAcquisition)typeof(LeasingEditor).GetField("a", flags)!.GetValue(editor)!;
                    var input = (LeasingClassificationInput)typeof(LeasingEditor).GetMethod("State", flags)!.Invoke(editor, [purchase.Items[0]])!;
                    input.Mode = TenantPlatform.Core.Leasing.LeasingAllocationMode.Percent;
                    input.Rows = [new() { InputValue = 50 }, new() { InputValue = 50 }];
                    typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(editor, null);
                    await Task.Yield();
                    Require(System.Net.WebUtility.HtmlDecode(root.ToHtmlString()).Contains("Velg minst én dimensjon"), "50/50 editor explains missing allocation dimension even when sum is 100");
                    var department = (await dimensions.GetAsync()).Dimensions.Single(x => x.Code == "DEP");
                    input.VaryingDimensions = [department.Id]; input.Common.Clear();
                    typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(editor, null);
                    await Task.Yield();
                    var allocationHtml = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                    Require(allocationHtml.Contains("Fordelingsrad 1") && allocationHtml.Contains("Fordelingsrad 2") && allocationHtml.Contains("Velg verdi for: Department"), "each allocation row identifies the missing dimension by name");
                }
                if (type == typeof(DimensionsPage))
                {
                    var page = renderer.DimensionPage!;
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    async Task Act(string name, params object[] args)
                    {
                        var result = typeof(DimensionsPage).GetMethod(name, flags)!.Invoke(page, args);
                        if (result is Task task) await task;
                        typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(page, null);
                        await Task.Yield();
                    }
                    var register = await dimensions!.GetAsync();
                    var dimension = register.Dimensions.Single(x => x.Code == "HIERARCHY");
                    var network = register.Values.Single(x => x.DimensionId == dimension.Id && x.Code == "NET");
                    var router = register.Values.Single(x => x.DimensionId == dimension.Id && x.Code == "RTR");
                    var routerType = register.Values.Single(x => x.DimensionId == dimension.Id && x.Code == "TYPE");
                    await Act("EditDimension", dimension);
                    var treeHtml = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                    Require(treeHtml.Contains("padding-inline-start: 40px") && treeHtml.Contains("Network / Router / Router type") && treeHtml.Contains("Legg til underelement"), "dimension values render three indented levels and add-child actions");
                    await Act("NewChild", routerType.Id);
                    var input = (DimensionValue)typeof(DimensionsPage).GetField("valueInput", flags)!.GetValue(page)!;
                    Require(input.ParentId == routerType.Id, "add-child preselects the clicked value at any depth");
                    input.Name = "Fourth level"; input.Code = "FOUR";
                    await Act("SaveValue");
                    register = await dimensions.GetAsync();
                    var paths = DimensionHierarchy.Build(dimension, register.Values);
                    Require(paths.Single(x => x.Code == "FOUR").Depth == 3 && paths.Single(x => x.Code == "FOUR").Path == "Network / Router / Router type / Fourth level", "saving through the editor persists a fourth hierarchy level");
                    await Act("EditValue", network.Id);
                    var parents = (IEnumerable<DimensionValueOption>)typeof(DimensionsPage).GetProperty("ParentOptions", flags)!.GetValue(page)!;
                    Require(!parents.Any(), "parent picker excludes self and all descendants");
                    await Act("EditValue", routerType.Id);
                    parents = (IEnumerable<DimensionValueOption>)typeof(DimensionsPage).GetProperty("ParentOptions", flags)!.GetValue(page)!;
                    Require(parents.Select(x => x.Id).SequenceEqual(new[] { network.Id, router.Id }), "parent picker keeps ancestors and excludes the edited subtree");
                    input = (DimensionValue)typeof(DimensionsPage).GetField("valueInput", flags)!.GetValue(page)!;
                    input.ParentId = null;
                    await Act("SaveValue");
                    register = await dimensions.GetAsync(); paths = DimensionHierarchy.Build(dimension, register.Values);
                    Require(paths.Single(x => x.Id == routerType.Id).Depth == 0 && paths.Single(x => x.Code == "FOUR").Path == "Router type / Fourth level", "moving a value to root preserves its descendants");
                    await Act("NewValue"); await Act("CancelValue"); await Act("NewDimension");
                    await Act("EditDimension", register.Dimensions.Single(x => x.Code == "DEP"));
                    await Act("EditDimension", dimension);
                    Require(root.ToHtmlString().Contains("Fourth level"), "switching dimensions and create/edit forms rerenders without EditContext errors");
                }
            });
        }
    }
    static void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL UI: " + name); Console.WriteLine("PASS UI: " + name); }
    sealed class TestNavigation : NavigationManager
    { public TestNavigation(string path) => Initialize("http://localhost/", "http://localhost/" + path); protected override void NavigateToCore(string uri, bool forceLoad) => throw new Exception("Unexpected navigation: " + uri); }
    sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
    }
    sealed class TestRenderer(IServiceProvider services, ILoggerFactory logger) : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, logger)
    {
        public DimensionsPage? DimensionPage { get; private set; }
        public LeasingEditor? LeasingPage { get; private set; }
        protected override IComponent ResolveComponentForRenderMode(Type type, int? parent, IComponentActivator activator, IComponentRenderMode mode)
        {
            var component = activator.CreateInstance(type);
            if (component is DimensionsPage page) DimensionPage = page;
            if (component is LeasingEditor editor) LeasingPage = editor;
            return component;
        }
    }
}
