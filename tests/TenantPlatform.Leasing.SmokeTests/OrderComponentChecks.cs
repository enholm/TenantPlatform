using System.Reflection;
using TenantPlatform.Core.Leasing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using TenantPlatform.Web.Components.Pages.Leasing;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Leasing;

static class OrderComponentChecks
{
    public static async Task Run(LeasingService service, Guid account, Guid user, Guid order, Guid framework)
    {
        await Render(typeof(OrderList), "leasing/purchase-orders", new(), "Oppfyllelsesgrad");
        await Render(typeof(OrderDetailsPage), $"leasing/purchase-orders/{order}", new() { ["Id"] = order }, "Fakturadokumentert beløp");
        await Render(typeof(OrderEditor), $"leasing/purchase-orders/{order}/edit", new() { ["Id"] = (Guid?)order }, "Realiseringsmetode");
        await Render(typeof(OrderEditor), "leasing/purchase-orders/create", new(), "Forventet kjøps-/leveringsdato");
        await Render(typeof(LeasingEditor), $"leasing/acquisitions/create?framework={framework}&order={order}", new(), "Reservasjon som frigis");
        async Task Render(Type type, string path, Dictionary<string,object?> parameters, string expected)
        {
            var services=new ServiceCollection().AddLogging().AddLocalization(x=>x.ResourcesPath="Resources");
            services.AddSingleton(service);services.AddSingleton<ICurrentUserContextService>(new Context(user,account));
            var navigation = new Navigation(path); services.AddSingleton<NavigationManager>(navigation);services.AddSingleton<IJSRuntime>(new NoJs());
            await using var provider=services.BuildServiceProvider();
            await using var renderer=new Renderer(provider,provider.GetRequiredService<ILoggerFactory>(),order,framework);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                var root=renderer.BeginRenderingComponent(type,ParameterView.FromDictionary(parameters));await root.QuiescenceTask;
                UiPreview.Write(type.Name + (path.EndsWith("create") ? "Create" : ""), root.ToHtmlString());
                var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                if(!html.Contains(expected)||html.Contains("Kunne ikke fullføre"))throw new Exception("Order component failed: "+type.Name+" "+html);
                if(navigation.Destination != null) throw new Exception("Unexpected navigation: " + navigation.Destination);
                Console.WriteLine("PASS UI: "+type.Name+" renders "+expected);
                if(type == typeof(OrderEditor) && path == "leasing/purchase-orders/create")
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var editor = renderer.OrderEditor!;
                    var input = (LeasingOrder)typeof(OrderEditor).GetField("order", flags)!.GetValue(editor)!;
                    var existing = await service.GetOrderAsync(account, order);
                    input.SupplierOrganizationId = existing.Order.SupplierOrganizationId;
                    input.Lines[0].Description = "Retain entered order"; input.Lines[0].UnitPrice = 1;
                    input.FrameworkId = Guid.Empty;
                    async Task Save()
                    {
                        await (Task)typeof(OrderEditor).GetMethod("Save", flags)!.Invoke(editor, null)!;
                        typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(editor, null);
                    }
                    await Save();
                    var invalidHtml = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                    if(navigation.Destination != null || !invalidHtml.Contains("Velg en rammeavtale før du lagrer") || input.Lines[0].Description != "Retain entered order")
                        throw new Exception("Missing framework must retain the form and display validation, without AccessDenied.");
                    Console.WriteLine("PASS UI: saving without framework retains input and shows validation, not AccessDenied");
                    input.FrameworkId = framework; input.Currency = existing.Order.Currency;
                    await Save();
                    var destination = navigation.Destination;
                    if(destination == null || !destination.StartsWith("/leasing/purchase-orders/") || !Guid.TryParse(destination.Split('/').Last(), out var created))
                        throw new Exception("Account admin should navigate to the saved order: " + destination);
                    var saved = await service.GetOrderAsync(account, created);
                    if(saved.Order.FrameworkId != framework || saved.Order.Lines.Single().Description != "Retain entered order") throw new Exception("Saved order must retain form values.");
                    Console.WriteLine("PASS UI: account admin saves successfully after selecting a framework");
                }
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
    sealed class Renderer(IServiceProvider services,ILoggerFactory logger,Guid order,Guid framework):Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services,logger)
    { public OrderEditor? OrderEditor { get; private set; }
      protected override IComponent ResolveComponentForRenderMode(Type type,int? parent,IComponentActivator activator,IComponentRenderMode mode){ var component=activator.CreateInstance(type); if(component is OrderEditor orderEditor) OrderEditor=orderEditor; if(component is LeasingEditor editor) { editor.OrderId=order;editor.FrameworkId=framework; } return component; } }
}
