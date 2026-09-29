using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class LeasingNavigationChecks
{
    public static async Task Run(LeasingService service, ITenantAuthorizationService authorization, Guid account, Guid user)
    {
        var state = new RouteState();
        var services = new ServiceCollection().AddLogging().AddLocalization(x => x.ResourcesPath = "Resources");
        services.AddSingleton(service);
        services.AddSingleton(authorization);
        services.AddSingleton<ICurrentUserContextService>(new Context(user, account));
        var navigation = new TestNavigation();
        services.AddSingleton<NavigationManager>(navigation);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new Renderer(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = renderer.BeginRenderingComponent(typeof(RouteHost), ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = state }));
            await root.QuiescenceTask;
            // Reuse one RouteView, just as Router does for navigation and browser back/forward.
            foreach (var (path, kind, title) in new[]
            {
                ("/leasing/frameworks", "frameworks", "Rammeavtaler"),
                ("/leasing/orders", "orders", "Bestillingsleasing"),
                ("/leasing/frameworks", "frameworks", "Rammeavtaler"),
                ("/leasing", "all", "Oversikt"),
                ("/leasing/orders", "orders", "Bestillingsleasing")
            })
            {
                var type = typeof(LeasingList).Assembly.GetTypes().Single(t => t.GetCustomAttributes(typeof(RouteAttribute), false).Cast<RouteAttribute>().Any(r => r.Template == path));
                navigation.SetPath(path);
                await state.Show!(type);
                // HtmlRootComponent.QuiescenceTask covers the initial render only.
                // Subsequent route changes start new asynchronous page loads.
                for (var attempt = 0; attempt < 500 && root.ToHtmlString().Contains("role=\"status\""); attempt++)
                    await Task.Delay(20);
                var html = WebUtility.HtmlDecode(root.ToHtmlString());
                var expected = await service.ListAsync(account, null, kind, null, null, 1);
                var actualLinks = Regex.Matches(html, "href=\"(/leasing/(?:frameworks|acquisitions)/[a-f0-9-]{36})\"").Select(m => m.Groups[1].Value).ToArray();
                var expectedLinks = expected.Items.Select(r => $"/leasing/{(r.Framework ? "frameworks" : "acquisitions")}/{r.Id}").ToArray();
                if (!html.Contains($"<h1>{title}</h1>") || !actualLinks.SequenceEqual(expectedLinks) || html.Contains("Operativt dashboard") != (kind == "all") || html.Contains("Kunne ikke fullføre"))
                    throw new Exception($"Navigation failed for {path}: {html}");
                Console.WriteLine($"PASS UI: same RouteView navigation to {path} refreshes title, dashboard and {expected.Items.Count} rows");
            }
        });
    }
    public sealed class RouteState { public Func<Type, Task>? Show { get; set; } }
    public sealed class RouteHost : ComponentBase
    {
        [Parameter] public RouteState State { get; set; } = null!;
        private Type? page;
        protected override void OnInitialized() => State.Show = type => InvokeAsync(() => { page = type; StateHasChanged(); });
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (page is null) return;
            builder.OpenComponent<RouteView>(0);
            builder.AddAttribute(1, "RouteData", new RouteData(page, new Dictionary<string, object?>()));
            builder.CloseComponent();
        }
    }
    sealed class TestNavigation : NavigationManager
    {
        public TestNavigation() => Initialize("http://localhost/", "http://localhost/leasing/frameworks");
        public void SetPath(string path) { Uri = ToAbsoluteUri(path).ToString(); NotifyLocationChanged(false); }
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new Exception("Unexpected navigation: " + uri);
    }
    sealed class Renderer(IServiceProvider services, ILoggerFactory logger) : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, logger)
    {
        protected override IComponent ResolveComponentForRenderMode(Type type, int? parent, IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);
    }
}
