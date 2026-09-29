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
    public static async Task Run(LeasingService service, Guid account, Guid user, Guid acquisition, Guid editableAcquisition)
    {
        await Render(typeof(LeasingLifecyclePage), $"leasing/acquisitions/{acquisition}/lifecycle", new(){["Id"]=acquisition}, "Avslutningskontroll");
        await Render(typeof(LeasingEquipmentPage), $"leasing/equipment?acquisition={editableAcquisition}", new(), "Utstyr");
        await Render(typeof(TenantPlatform.Web.Components.Pages.Leasing.LeasingFollowupPage), "leasing/followup", new(), "Oppfølging");
        foreach (var kind in Enum.GetValues<LeasingReportKind>())
            await Render(typeof(LeasingReportPage), "leasing/reports", new(), "Rapporter", kind);
        await Render(typeof(LeasingDetailsPage), $"leasing/acquisitions/{acquisition}", new(){["Id"]=acquisition}, "Dokumenter");
        await Render(typeof(LeasingDashboard), "leasing", new(), "Operativt dashboard");
        async Task Render(Type type, string path, Dictionary<string,object?> parameters, string expected, LeasingReportKind? reportKind = null)
        {
            var services=new ServiceCollection().AddLogging().AddLocalization(x=>x.ResourcesPath="Resources");
            services.AddSingleton(service);services.AddSingleton<ICurrentUserContextService>(new Context(user,account));
            var navigation = new Navigation(path); services.AddSingleton<NavigationManager>(navigation);services.AddSingleton<IJSRuntime>(new NoJs());
            await using var provider=services.BuildServiceProvider();
            await using var renderer=new Renderer(provider,provider.GetRequiredService<ILoggerFactory>(),editableAcquisition,reportKind);
            await renderer.Dispatcher.InvokeAsync(async()=>
            {
                var root=renderer.BeginRenderingComponent(type,ParameterView.FromDictionary(parameters));await root.QuiescenceTask;
                UiPreview.Write(type.Name + reportKind + (path.EndsWith("create") ? "Create" : ""), root.ToHtmlString());
                var html=System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                if(!html.Contains(expected)||html.Contains("Kunne ikke fullføre"))throw new Exception("Order component failed: "+type.Name+" "+html);
                if(navigation.Destination != null) throw new Exception("Unexpected navigation: " + navigation.Destination);
                Console.WriteLine("PASS UI: "+type.Name+" renders "+expected);
                if(type == typeof(LeasingEquipmentPage))
                {
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var page = renderer.EquipmentPage!;
                    var input = (LeasingEquipment)typeof(LeasingEquipmentPage).GetField("equipment", flags)!.GetValue(page)!;
                    typeof(LeasingEquipmentPage).GetField("duplicateConfirmed", flags)!.SetValue(page, true);
                    typeof(LeasingEquipmentPage).GetField("reason", flags)!.SetValue(page, "Reviewed possible serial duplicates");
                    var before = await service.GetLifecycleAsync(account, editableAcquisition);
                    async Task Save()
                    {
                        await (Task)typeof(LeasingEquipmentPage).GetMethod("Save", flags)!.Invoke(page, null)!;
                        typeof(ComponentBase).GetMethod("StateHasChanged", flags)!.Invoke(page, null);
                    }
                    await Save();
                    var invalid = System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
                    if(navigation.Destination != null || !invalid.Contains("Velg en tellbar varelinje") ||
                        !(bool)typeof(LeasingEquipmentPage).GetField("duplicateConfirmed", flags)!.GetValue(page)! ||
                        (string)typeof(LeasingEquipmentPage).GetField("reason", flags)!.GetValue(page)! != "Reviewed possible serial duplicates")
                        throw new Exception("Missing item must show validation and retain input, not redirect to AccessDenied.");
                    if((await service.GetLifecycleAsync(account, editableAcquisition)).Equipment.Count != before.Equipment.Count)
                        throw new Exception("Invalid submission must not create equipment.");
                    Console.WriteLine("PASS UI: admin confirms duplicates without an item; validation retains input and prevents AccessDenied");
                    input.ItemId = before.Acquisition.Items.Single(x=>x.CountableEquipment).Id;
                    input.Description = "Equipment saved through the form";
                    input.SerialNumber = before.Equipment.First().SerialNumber;
                    await Save();
                    var after = await service.GetLifecycleAsync(account, editableAcquisition);
                    if(navigation.Destination != null || after.Equipment.Count != before.Equipment.Count + 1 ||
                        !after.Equipment.Any(x=>x.Description==input.Description && x.SerialNumber==input.SerialNumber))
                        throw new Exception("Admin must save successfully after selecting an item with confirmed duplicate serial.");
                    Console.WriteLine("PASS UI: admin corrects item selection and saves equipment with confirmed duplicate serial");
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
    sealed class Renderer(IServiceProvider services,ILoggerFactory logger,Guid acquisition, LeasingReportKind? reportKind):Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services,logger)
    { public LeasingEquipmentPage? EquipmentPage {get;private set;}
      protected override IComponent ResolveComponentForRenderMode(Type type,int? parent,IComponentActivator activator,IComponentRenderMode mode){var component=activator.CreateInstance(type);if(component is LeasingReportPage report && reportKind.HasValue)report.QueryKind=(int)reportKind.Value;if(component is LeasingEquipmentPage page){page.Acquisition=acquisition;EquipmentPage=page;}return component;} }
}
