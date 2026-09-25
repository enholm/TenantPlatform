using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class LeasingComponentChecks
{
    public static async Task Run(LeasingService service, Guid account, Guid user, Guid framework, Guid acquisition)
    {
        await Render(typeof(LeasingEditor), $"leasing/frameworks/{framework}/edit", framework, html =>
            Require(html.Contains("lease-from") && html.Contains("lease-to") && html.Contains("lease-limit") && html.Contains("Standardvilkår"), "framework editor has period, capacity and terms"));
        await Render(typeof(LeasingEditor), $"leasing/acquisitions/{acquisition}/edit", acquisition, html =>
            Require(html.Contains("item-description-0") && html.Contains("lease-financed") && html.Contains("lease-invoice-number"), "acquisition editor renders stable line controls and separate financing"));
        await Render(typeof(LeasingDetailsPage), $"leasing/frameworks/{framework}", framework, html =>
            Require(html.Contains("Tilgjengelig beløp") && html.Contains("2032") && html.Contains("Endringshistorikk"), "framework details show remaining limit, individual lease dates and history"));
        await Render(typeof(LeasingDetailsPage), $"leasing/acquisitions/{acquisition}", acquisition, html =>
            Require(html.Contains("invoice.pdf") && html.Contains("/download") && html.Contains("Kanseller anskaffelsen"), "acquisition details provide documents and cancellation"));

        async Task Render(Type type, string path, Guid id, Action<string> check)
        {
            var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
            services.AddSingleton(service); services.AddSingleton<ICurrentUserContextService>(new Context(user, account));
            services.AddSingleton<NavigationManager>(new TestNavigation(path)); services.AddSingleton<IJSRuntime>(new NoJs());
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new TestRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = renderer.BeginRenderingComponent(type, ParameterView.FromDictionary(new Dictionary<string, object?> { ["Id"] = id }));
                await root.QuiescenceTask;
                var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Require(!html.Contains("Kunne ikke fullføre"), "component loads without error"); check(html);
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
        protected override IComponent ResolveComponentForRenderMode(Type type, int? parent, IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);
    }
}
