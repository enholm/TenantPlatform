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
using TenantPlatform.Web.Security.Authorization;

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
    }
    public static async Task BulkNavigation(IAgreementService service, ICurrentUserContextService context, ITenantAuthorizationService authorization)
    {
        await Check(service, context, typeof(Agreements), new(), (component, html) =>
        {
            Require(html().Contains("href=\"/agreements/bases/generate\""), "agreement overview links to bulk generation");
            return Task.CompletedTask;
        }, authorization);
    }
    public static async Task Bulk(IAgreementService service, ICurrentUserContextService context, Guid nok, Guid eur, DateOnly from, DateOnly to)
    {
        await Check(service, context, typeof(AgreementBulkBasisGenerate), new(), async (component, html) =>
        {
            Require((DateOnly)Field(component, "from")! == new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1), "bulk page defaults to current month");
            Set(component, "from", from); Set(component, "to", to);
            await (Task)Call(component, "Preview")!; Refresh(component);
            var rows = (List<AgreementBulkBasisPreview>)Field(component, "preview")!;
            Require(rows.Any(x => x.AgreementId == nok && x.CanGenerate) && rows.Any(x => x.AgreementId == eur && x.CanGenerate), "bulk page previews multiple eligible agreements");
            Call(component, "SelectAll"); Refresh(component);
            var selected = (HashSet<Guid>)Field(component, "selected")!;
            Require(rows.Where(x => x.CanGenerate).All(x => selected.Contains(x.AgreementId)), "select all covers entire filtered result");
            Call(component, "ClearSelection"); Call(component, "Toggle", nok, true); Call(component, "Toggle", eur, true); Refresh(component);
            Require(selected.SetEquals(new[] { nok, eur }), "bulk page supports explicit subset");
            Require(html().Contains("NOK") && html().Contains("EUR") && html().Contains("<details>") && html().Contains("1000"), "bulk page renders separate currencies and expandable calculation details");
            Set(component, "to", to.AddDays(1)); Refresh(component);
            Require(System.Text.RegularExpressions.Regex.IsMatch(html(), "id=\"bulk-generate\"[^>]*disabled"), "changing filters disables generation until new preview");
            Set(component, "to", to);
            await (Task)Call(component, "Generate")!; Refresh(component);
            var results = (Dictionary<Guid, AgreementBulkBasisResult>)Field(component, "results")!;
            Require(results.Count == 2 && results.Values.All(x => x.Created.Count == 1), "bulk UI generates selected drafts");
            Require(html().Contains("/agreements/bases/") && html().Contains("href=\"/agreements/bases\""), "bulk UI links to created bases and the overview");
            Require(selected.SetEquals(new[] { nok, eur }) && (DateOnly)Field(component, "from")! == from, "bulk UI preserves selection and filters");
            await (Task)Call(component, "Generate")!;
            Require(results.Values.Sum(x => x.Created.Count) == 2, "second click does not process completed selection again");
        });
    }
    public static async Task Approvals(IAgreementService service, ICurrentUserContextService context, Guid party, Guid agreement,
        Guid euroBasis, Guid costBasis, Func<Task<Guid>> createLater)
    {
        await Check(service, context, typeof(AgreementBases), new(), (component, html) =>
        {
            Require(html().Contains("href=\"/agreements/bases/approve\""), "basis overview links to collective approval");
            Require(html().Contains("/agreements/bases/"), "basis overview retains individual detail links");
            return Task.CompletedTask;
        });
        await Check(service, context, typeof(AgreementBasisApproval), new(), async (component, html) =>
        {
            Set(component, "counterpartyId", party); Set(component, "agreementId", agreement);
            await (Task)Call(component, "FiltersChanged")!; Refresh(component);
            var selected = (Dictionary<Guid, int>)Field(component, "selected")!;
            var rows = (List<AgreementBasisApprovalItem>)Field(component, "items")!;
            Require(rows.Count == 30, "approval list includes pending rows beyond the first page");
            Require(System.Text.RegularExpressions.Regex.IsMatch(html(), "id=\"approval-submit\"[^>]*disabled"), "approve button disabled with empty selection");
            Call(component, "SelectAll"); Call(component, "Next"); Refresh(component);
            Require(selected.Count == 30 && (int)Field(component, "page")! == 2, "select all spans pages and survives page navigation");
            var later = await createLater(); await (Task)Call(component, "Refresh")!; Refresh(component);
            Require(selected.Count == 30 && !selected.ContainsKey(later), "new drafts do not silently join an existing selection");
            var reviewed = selected.First(); Call(component, "OpenDetails", reviewed.Key); Refresh(component);
            Require((Guid?)Field(component, "reviewId") == reviewed.Key && !html().Contains("id=\"approval-party\""), "full individual details open inside the approval page");
            await service.RegenerateBasisAsync(context.Current.CurrentAccountId!.Value, reviewed.Key, reviewed.Value, "Changed during review");
            await (Task)Call(component, "ReturnFromDetails")!; Refresh(component);
            Require(selected.Count == 29 && !selected.ContainsKey(reviewed.Key) && (int)Field(component, "page")! == 2, "return preserves page and selection while removing changed revisions");
            Set(component, "agreementId", null!); await (Task)Call(component, "FiltersChanged")!; Refresh(component);
            Require(selected.Count == 0 && (int)Field(component, "page")! == 1, "changing filters clears all selections and resets page");
            Call(component, "SelectAll"); Refresh(component);
            Require(System.Text.RegularExpressions.Regex.Matches(html(), "class=\"approval-total\"").Count == 3, "selected totals separate both currencies and income versus cost");
            Call(component, "ClearSelection");
            rows = (List<AgreementBasisApprovalItem>)Field(component, "items")!;
            var blocked = rows.First(x => !x.CanApprove); Call(component, "Toggle", blocked, true);
            Require(selected.Count == 0, "blocked draft cannot be selected");
            Call(component, "Toggle", rows.Single(x => x.BasisId == euroBasis), true);
            Call(component, "Toggle", rows.Single(x => x.BasisId == costBasis), true);
            Require(selected.Count == 2, "explicit subset selects definite bases");
            Set(component, "busy", true); await (Task)Call(component, "Approve")!;
            Require(selected.Count == 2, "busy guard prevents repeated approval clicks"); Set(component, "busy", false);
            await (Task)Call(component, "Approve")!; Refresh(component);
            var results = (List<AgreementBasisApprovalResult>)Field(component, "results")!;
            Require(results.Count == 2 && results.All(x => x.Outcome == AgreementBasisApprovalOutcome.Approved), "UI approves and locks selected income and cost bases");
            rows = (List<AgreementBasisApprovalItem>)Field(component, "items")!;
            Require(!rows.Any(x => x.BasisId == euroBasis || x.BasisId == costBasis) && selected.Count == 0,
                "successful approvals leave pending list and selection");
            Require((Guid?)Field(component, "counterpartyId") == party && html().Contains("href=\"/agreements/bases\""), "approval result retains filters and overview navigation");
            var changed = rows.First(x => x.CanApprove); Call(component, "Toggle", changed, true);
            await service.RegenerateBasisAsync(context.Current.CurrentAccountId!.Value, changed.BasisId, changed.Revision, "Changed after selection");
            await (Task)Call(component, "Approve")!; Refresh(component);
            rows = (List<AgreementBasisApprovalItem>)Field(component, "items")!;
            Require(!rows.Single(x => x.BasisId == changed.BasisId).CanApprove && selected.Count == 0,
                "failed changed draft requires explicit new review before retry");
            await (Task)Call(component, "Refresh")!;
            rows = (List<AgreementBasisApprovalItem>)Field(component, "items")!;
            var rechecked = rows.Single(x => x.BasisId == changed.BasisId);
            Require(rechecked.CanApprove && rechecked.Revision == changed.Revision + 1, "refresh shows the revised draft for a new explicit selection");
            Call(component, "Toggle", rechecked, true); await (Task)Call(component, "Approve")!;
            Require(((List<AgreementBasisApprovalResult>)Field(component, "results")!).Single().Outcome == AgreementBasisApprovalOutcome.Approved, "single selected draft can be approved after renewed review");
            Set(component, "agreementId", agreement); await (Task)Call(component, "FiltersChanged")!;
            rows = (List<AgreementBasisApprovalItem>)Field(component, "items")!;
            var remaining = rows.Count; Call(component, "SelectAll"); await (Task)Call(component, "Approve")!;
            results = (List<AgreementBasisApprovalResult>)Field(component, "results")!;
            Require(remaining > 25 && results.Count == remaining && results.All(x => x.Outcome == AgreementBasisApprovalOutcome.Approved), "all selected bases across pages can be approved in one action");
        });
    }
    static async Task Check(IAgreementService service, ICurrentUserContextService context, Type type, Dictionary<string,object?> parameters,
        Func<object,Func<string>,Task> check, ITenantAuthorizationService? authorization = null)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddLocalization(o=>o.ResourcesPath="Resources");
        if (authorization is not null) services.AddSingleton(authorization);
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
