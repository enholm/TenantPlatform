using System.Reflection;
using TenantPlatform.Core.Leasing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class LifecycleComponentChecks
{
    public static async Task Run(LeasingService service, Guid account, Guid user, Guid acquisition)
    {
        await Render(typeof(LeasingLifecyclePage), $"leasing/acquisitions/{acquisition}/lifecycle", new(){["Id"]=acquisition}, "Avslutningskontroll");
        await Render(typeof(LeasingEquipmentPage), $"leasing/equipment?acquisition={acquisition}", new(), "Utstyr");
        await Render(typeof(TenantPlatform.Web.Components.Pages.Leasing.LeasingFollowupPage), "leasing/followup", new(), "Oppfølging");
        await Render(typeof(LeasingReportPage), "leasing/reports", new(), "Rapporter");
        await Render(typeof(LeasingDashboard), "leasing", new(), "Operativt dashboard");
        async Task Render(Type type, string path, Dictionary<string,object?> parameters, string expected)
        {
            var services=new ServiceCollection().AddLogging().AddLocalization(x=>x.ResourcesPath="Resources");
            services.AddSingleton(service);services.AddSingleton<ICurrentUserContextService>(new Context(user,account));
            var navigation = new Navigation(path); services.AddSingleton<NavigationManager>(navigation);services.AddSingleton<IJSRuntime>(new NoJs());
            await using var provider=services.BuildServiceProvider();
            await using var renderer=new Renderer(provider,provider.GetRequiredService<ILoggerFactory>(),acquisition);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                var root=renderer.BeginRenderingComponent(type,ParameterView.FromDictionary(parameters));await root.QuiescenceTask;
                var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                if(!html.Contains(expected)||html.Contains("Kunne ikke fullføre"))throw new Exception("Order component failed: "+type.Name+" "+html);
                if(navigation.Destination != null) throw new Exception("Unexpected navigation: " + navigation.Destination);
                Console.WriteLine("PASS UI: "+type.Name+" renders "+expected);
            });
        }
    }
    sealed class Navigation:NavigationManager
    {
        public Navigation(string path)=>Initialize("http://localhost/","http://localhost/"+path);
        public string? Destination { get; private set; }
        protected override void NavigateToCore(string uri,bool forceLoad)=>Destination = uri;
    }
    sealed class NoJs:IJSRuntime
    { public ValueTask<T> InvokeAsync<T>(string id,object?[]? args)=>ValueTask.FromResult(default(T)!); public ValueTask<T> InvokeAsync<T>(string id,CancellationToken ct,object?[]? args)=>ValueTask.FromResult(default(T)!); }
    sealed class Renderer(IServiceProvider services,ILoggerFactory logger,Guid acquisition):Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services,logger)
    { protected override IComponent ResolveComponentForRenderMode(Type type,int? parent,IComponentActivator activator,IComponentRenderMode mode){var component=activator.CreateInstance(type);if(component is LeasingEquipmentPage page)page.Acquisition=acquisition;return component;} }
}
