using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using TenantPlatform.Infrastructure.Persistence;
using TenantPlatform.Web.Security.Authorization;
using TenantPlatform.Web.Security.CurrentUserContext;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;
using TenantPlatform.Web.Services.Leasing;

// No HTTP host, Npgsql context, SQL, or application startup. Unrelated panels use explicit stubs.
sealed class UiHarness : IAsyncDisposable
{
    public ServiceProvider Provider { get; }
    public UiRenderer Renderer { get; }
    public UiNavigation Navigation { get; } = new();
    public UiHarness(Fixture f, IAgreementService agreements, Guid? user = null, IAgreementAnalysisService? analysis = null)
    {
        var context = new UiContext(user ?? f.Admin, f.Account);
        var factory = new UiFactory(f);
        var auth = new TenantAuthorizationService(factory, context);
        var services = new ServiceCollection().AddLogging().AddLocalization(o => o.ResourcesPath = "Resources");
        services.AddSingleton(agreements).AddSingleton<ICurrentUserContextService>(context)
            .AddSingleton<NavigationManager>(Navigation).AddSingleton<IJSRuntime>(new UiNoJs());
        services.AddSingleton(new DimensionService(factory, context, auth, TimeProvider.System));
        services.AddSingleton(new LeasingService(factory, context, auth, null!, TimeProvider.System, NullLogger<LeasingService>.Instance));
        services.AddSingleton(analysis ?? UiProxy.Make<IAgreementAnalysisService>((m, a) => m.Name switch
        {
            "GetAnalysesAsync" => Task.FromResult(new List<TenantPlatform.Core.Agreements.AgreementAnalysis>()),
            "DiscardAnalysisAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(m.Name)
        }));
        services.AddSingleton(UiProxy.Make<IAgreementFollowupService>((m, a) => m.Name == "GetFollowupAsync"
            ? Task.FromResult(new AgreementFollowupDetails(Guid.Empty, false, false, false, [], [], default, [], false, [], "Europe/Oslo", new(8, 0), "Disabled"))
            : throw new NotSupportedException(m.Name)));
        Provider = services.BuildServiceProvider();
        Renderer = new UiRenderer(Provider, Provider.GetRequiredService<ILoggerFactory>());
    }
    public async Task<UiMount> Mount<T>(params (string Key, object? Value)[] parameters) where T : IComponent
    {
        var mount = new UiMount(typeof(T));
        foreach (var p in parameters) mount.Parameters[p.Key] = p.Value;
        mount.Output = Renderer.BeginRenderingComponent(typeof(UiHost), ParameterView.FromDictionary(new Dictionary<string, object?> { ["State"] = mount }));
        await mount.Output.QuiescenceTask;
        return mount;
    }
    #pragma warning disable BL0006 // StaticHtmlRenderer is used only by this offline test harness.
    public async ValueTask DisposeAsync() { await Renderer.DisposeAsync(); await Provider.DisposeAsync(); }
#pragma warning restore BL0006
}
sealed class UiMount(Type type)
{
    public Type Type { get; } = type;
    public Dictionary<string, object?> Parameters { get; } = [];
    public object Component { get; set; } = null!;
    public UiHost Host { get; set; } = null!;
    public HtmlRootComponent Output { get; set; }
    public string Html => System.Net.WebUtility.HtmlDecode(Output.ToHtmlString());
    public T Field<T>(string name) => (T)Type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Component)!;
    public void SetField(string name, object value) => Type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Component, value);
    public async Task Act(string name, params object?[] args)
    {
        var method = Type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Single(m => m.Name == name && m.GetParameters().Length == args.Length);
        if (UiProxy.Call(method, Component, args) is Task task) await task;
        Refresh();
        await Output.QuiescenceTask;
    }
    public void Refresh() => typeof(ComponentBase).GetMethod("StateHasChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Component, null);
}
sealed class UiHost : ComponentBase
{
    [Parameter] public UiMount State { get; set; } = null!;
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
sealed class UiRenderer(IServiceProvider provider, ILoggerFactory logger) : StaticHtmlRenderer(provider, logger)
{
    protected override IComponent ResolveComponentForRenderMode(Type type, int? parent, IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);
}
sealed class UiNavigation : NavigationManager
{
    public string? Destination { get; private set; }
    public UiNavigation() => Initialize("http://localhost/", "http://localhost/agreements");
    protected override void NavigateToCore(string uri, bool forceLoad) => Destination = uri;
}
sealed class UiFactory(Fixture f) : IDbContextFactory<TenantPlatformDbContext>
{ public TenantPlatformDbContext CreateDbContext() => f.Open(); }
sealed class UiContext(Guid user, Guid account) : ICurrentUserContextService
{ public CurrentUserContext Current { get; } = new() { IsAuthenticated = true, UserId = user, CurrentAccountId = account }; }
sealed class UiNoJs : IJSRuntime
{
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args) => ValueTask.FromResult(default(T)!);
}
public class UiProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? []);
    public static T Make<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = Create<T, UiProxy>(); ((UiProxy)(object)proxy).Handler = handler; return proxy; }
    public static object? Call(MethodInfo method, object target, object?[] args)
    {
        try { return method.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
}
