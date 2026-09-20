using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Web.Components.Pages.Agreements;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;

static class ComponentEditorChecks
{
    public static async Task Run(IAgreementService owner, IAgreementService admin, ICurrentUserContextService ownerContext,
        ICurrentUserContextService adminContext, Guid agreementId, Guid lineId, Guid indexId)
    {
        await Check(owner, ownerContext, typeof(AgreementLines), new() { ["AgreementId"] = agreementId }, async (component, html) =>
        {
            var line = (await owner.GetLinesAsync(ownerContext.Current.CurrentAccountId!.Value, agreementId)).Lines.Single(x => x.Line.Id == lineId);
            Call(component, "EditLine", line); Refresh(component);
            var rendered = html();
            foreach (var id in new[] { "line-start", "line-end", "line-payable", "line-frequency", "line-quantity", "line-price", "line-index" })
            {
                var tag = System.Text.RegularExpressions.Regex.Match(rendered, "<[^>]*id=\"" + id + "\"[^>]*>").Value;
                Require(tag.Length > 0 && !tag.Contains("disabled"), $"active editor enables {id}");
            }
            var request = (SaveAgreementLineRequest)Field(component,"editing")!;
            request.Name = "Rendered editor correction"; request.Quantity = 3; request.Reason = "Component UI check";
            await (Task)Call(component,"SaveLineAsync")!; Refresh(component);
            Require(html().Contains("Rendered editor correction") && Field(component,"editing") is null,"active editor saves and returns to line list");
            Require((await owner.GetLinesAsync(ownerContext.Current.CurrentAccountId!.Value,agreementId)).Lines.Single(x=>x.Line.Id==lineId).Prices[0].Quantity==3,"active editor persists economic correction");
        });
        await Check(admin, adminContext, typeof(AgreementIndices), new(), async (component, html) =>
        {
            var account=adminContext.Current.CurrentAccountId!.Value;
            var value=(await admin.GetIndicesAsync(account)).Values.First(x=>x.IndexId==indexId && !x.Superseded);
            Call(component,"EditValue",value);Refresh(component);
            Require(html().Contains("id=\"index-value\"") && (Guid?)Field(component,"editingValue")==value.Id,"period Edit opens populated editor");
            Set(component,"value",value.Value+1);Set(component,"reason","Component correction");
            await (Task)Call(component,"Record")!;Refresh(component);
            Require(Field(component,"editingValue") is null && Field(component,"error") is null,"period editor saves successfully");
            var rows=(await admin.GetIndicesAsync(account)).Values.Where(x=>x.IndexId==indexId && x.Period==value.Period).ToList();
            Require(rows[0].Value==value.Value+1 && rows.Any(x=>x.Id==value.Id && x.Value==value.Value),"period editor keeps prior revision");
            var index=(await admin.GetIndicesAsync(account)).Indices.Single(x=>x.Id==indexId);
            Call(component,"EditIndex",index);Set(component,"name","Rendered index name");
            await (Task)Call(component,"Create")!;Refresh(component);
            Require(html().Contains("Rendered index name"),"index metadata editor saves and renders new name");
        });
    }
    public static async Task Automatic(IAgreementService service, ICurrentUserContextService context, Guid agreementId)
    {
        await Check(service, context, typeof(AgreementLines), new() { ["AgreementId"] = agreementId }, async (component, html) =>
        {
            Set(component,"from",new DateOnly(2027,1,1)); Set(component,"to",new DateOnly(2027,1,31));
            await (Task)Call(component,"PreviewAsync")!; Refresh(component);
            var forecast=(AgreementForecastDto)Field(component,"forecast")!;
            Require(forecast.Periods.Count==30 && forecast.Periods.All(x=>x.UnitPrice==1049.50m),"forecast editor automatically calculates all thirty lines");
            Require(System.Net.WebUtility.HtmlDecode(html()).Contains(1049.50m.ToString("N2")),"forecast HTML includes calculated index price");
            Require(html().Contains("106") && html().Contains("101"),"forecast HTML includes index evidence");
        });
        await Check(service, context, typeof(AgreementAdjustments), new() { ["AgreementId"] = agreementId }, (component, html) =>
        {
            var rendered=html();
            Require(!rendered.Contains("adjust-period") && !rendered.Contains("decision-comment") && !rendered.Contains("btn-success"),"index page has no per-line proposal or approval actions");
            return Task.CompletedTask;
        });
    }
    static async Task Check(IAgreementService service, ICurrentUserContextService context, Type type, Dictionary<string,object?> parameters,
        Func<object,Func<string>,Task> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddLocalization(o=>o.ResourcesPath="Resources");
        services.AddSingleton(service);services.AddSingleton(context);services.AddSingleton<NavigationManager>(new TestNavigation());
        await using var provider=services.BuildServiceProvider();
        await using var renderer=new EditorRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async ()=>
        {
            var host=new EditorHostState(type,parameters);
            var root=renderer.BeginRenderingComponent(typeof(EditorHost), ParameterView.FromDictionary(new Dictionary<string,object?>{{"State",host}}));
            await root.QuiescenceTask;
            await check(host.Instance!,root.ToHtmlString);
        });
    }
    static object? Call(object instance,string name,params object[] args)=>instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,args);
    static object? Field(object instance,string name)=>instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance);
    static void Set(object instance,string name,object value)=>instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(instance,value);
    static void Refresh(object instance)=>typeof(ComponentBase).GetMethod("StateHasChanged",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(instance,null);
    static void Require(bool condition,string name){if(!condition)throw new Exception("FAIL UI: "+name);Console.WriteLine("PASS UI: "+name);}
    sealed class TestNavigation:NavigationManager{public TestNavigation()=>Initialize("http://localhost/","http://localhost/agreements");protected override void NavigateToCore(string uri,bool forceLoad)=>throw new Exception("Unexpected navigation: "+uri);}
}
sealed record EditorHostState(Type Type, Dictionary<string,object?> Parameters){public object? Instance{get;set;}}
sealed class EditorHost:ComponentBase
{
    [Parameter] public EditorHostState State{get;set;}=null!;
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenComponent(0,State.Type);b.AddMultipleAttributes(1,State.Parameters.Select(x => new KeyValuePair<string, object>(x.Key, x.Value!)));b.AddComponentReferenceCapture(2,instance=>State.Instance=instance);b.CloseComponent();
    }
}

// Runs interactive page components inside an in-process renderer for editor regression checks.
sealed class EditorRenderer(IServiceProvider services, ILoggerFactory logger) : Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure.StaticHtmlRenderer(services, logger)
{
    protected override IComponent ResolveComponentForRenderMode(Type type, int? parent, IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);
}
