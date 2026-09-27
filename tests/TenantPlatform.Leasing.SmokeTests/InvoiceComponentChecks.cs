using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class InvoiceComponentChecks
{
    public static async Task Run(LeasingService service, Guid account, Guid user, Guid pending, Guid approved)
    {
        await Render(typeof(InvoiceImportPage), "leasing/invoices", null, html => Require(html.Contains("invoice-upload") && html.Contains("Fakturaimport"), "import upload/list renders"));
        await Render(typeof(InvoiceReviewPage), $"leasing/invoices/{pending}", pending, html => Require(html.Contains("col-xl-5") && html.Contains("col-xl-7") && html.Contains("Kontrollerte data") && html.Contains("Lagre uferdig kontroll") && html.Contains("Prisgrunnlag"), "review surface renders responsive original and editable financial controls"));
        await Render(typeof(InvoiceReviewPage), $"leasing/invoices/{approved}", approved, html => Require(html.Contains("Reverser godkjenning") && html.Contains("fieldset disabled") && html.Contains("Data lest fra dokumentet"), "approved document is read-only with explicit reversal and source data"));
        async Task Render(Type type, string path, Guid? id, Action<string> check)
        {
            var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
            services.AddSingleton(service); services.AddSingleton<ICurrentUserContextService>(new Context(user, account));
            services.AddSingleton<NavigationManager>(new Nav(path)); services.AddSingleton<IJSRuntime>(new Js());
            await using var provider = services.BuildServiceProvider(); await using var renderer = new Renderer(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var root = renderer.BeginRenderingComponent(type, ParameterView.FromDictionary(id.HasValue ? new Dictionary<string,object?> { ["Id"] = id.Value } : new()));
                await root.QuiescenceTask; var html = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                Require(!html.Contains("Behandlingen mislyktes"), "invoice component loads without error"); check(html);
            });
        }
    }
    static void Require(bool condition, string name) { if (!condition) throw new Exception("FAIL invoice UI: " + name); Console.WriteLine("PASS INVOICE UI: " + name); }
    sealed class Nav : NavigationManager { public Nav(string path) => Initialize("http://localhost/", "http://localhost/" + path); protected override void NavigateToCore(string uri, bool forceLoad) => throw new Exception("Unexpected navigation " + uri); }
    sealed class Js : IJSRuntime { public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => ValueTask.FromResult(default(T)!); public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!); }
    sealed class Renderer(IServiceProvider services, ILoggerFactory logger) : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, logger)
    { protected override IComponent ResolveComponentForRenderMode(Type type, int? parent, IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type); }
}
