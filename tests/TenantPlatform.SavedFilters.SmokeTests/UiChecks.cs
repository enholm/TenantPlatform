using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;
using TenantPlatform.Core.Identity;
using TenantPlatform.Web.Components.Shared;
using TenantPlatform.Web.Services.SavedFilters;

static class UiChecks
{
    public static async Task RunAsync()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddLocalization(options => options.ResourcesPath = "Resources");
        var savedFilters = new UiSavedFilters();
        services.AddSingleton<ISavedFilterService>(savedFilters);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        foreach (var page in Enum.GetValues<SavedFilterPage>())
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                RenderFragment content = builder => builder.AddMarkupContent(0,
                    "<label for=\"synthetic-filter\">Synthetic</label><input id=\"synthetic-filter\">");
                var output = await renderer.RenderComponentAsync<SavedFilterTabs>(ParameterView.FromDictionary(
                    new Dictionary<string, object?>
                    {
                        ["AccountId"] = Guid.NewGuid(),
                        ["Page"] = page,
                        ["FilterContent"] = content,
                        ["CaptureCriteria"] = new Func<string>(() => "{}"),
                        ["OnApplyCriteria"] = EventCallback.Factory.Create<string>(new object(), _ => { }),
                        ["OnEditCriteria"] = EventCallback.Factory.Create<string>(new object(), _ => { }),
                        ["OnSearch"] = EventCallback.Factory.Create(new object(), () => { }),
                        ["OnReset"] = EventCallback.Factory.Create(new object(), () => { })
                    }));
                var html = output.ToHtmlString();
                Check.That(html.Contains("role=\"tablist\"") && html.Contains("role=\"tab\"") &&
                    html.Contains("aria-controls=") && html.Contains("Lagrede filter") && html.Contains("Synthetic") &&
                    html.Contains("type=\"button\"") && html.Contains(">Lagre</button>") &&
                    !html.Contains("saved-filter-overwrite"),
                    $"{page} renders the shared filter tab as default with non-submit actions");
            });
        }


        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var state = new UiState();
            _ = await renderer.RenderComponentAsync<UiHost>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["State"] = state }));
            var component = state.Component!;
            Set(component, "activeTab", "Saved");
            Set(component, "selectedId", savedFilters.Original.Id);
            await InvokeAsync(component, "ApplySelectedAsync");
            Check.That(state.Applied == savedFilters.Original.CriteriaJson && Get(component, "activeTab")?.ToString() == "Saved",
                "selecting a saved filter applies it immediately and keeps the saved-filter tab open");

            await InvokeAsync(component, "EditSelectedAsync");
            Check.That(state.Edited == savedFilters.Original.CriteriaJson && Get(component, "activeTab")?.ToString() == "Filter" &&
                savedFilters.Original.CriteriaJson == "{\"search\":\"saved\"}",
                "edit fills criteria, opens the filter tab and leaves persisted criteria unchanged");
            Invoke(component, "OpenSave");
            Check.That((string?)Get(component, "saveMode") == "overwrite" &&
                (Guid?)Get(component, "overwriteId") == savedFilters.Original.Id,
                "edit preselects explicit overwrite by saved-filter identity");

            await InvokeAsync(component, "SaveAsync");
            Check.That(savedFilters.LastOverwritten is { } overwritten && overwritten.Id == savedFilters.Original.Id &&
                overwritten.Criteria == "{\"captured\":true}",
                "overwrite updates only the explicitly selected saved-filter identity");

            Invoke(component, "OpenSave");
            Set(component, "saveMode", "new");
            Set(component, "newName", "New filter");
            await InvokeAsync(component, "SaveAsync");
            Check.That(savedFilters.LastCreated is { Name: "New filter", Criteria: "{\"captured\":true}" },
                "new save captures the current unsent filter state");
        });
    }

    private static object? Get(SavedFilterTabs component, string name) =>
        typeof(SavedFilterTabs).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(component);

    private static void Set(SavedFilterTabs component, string name, object? value)
    {
        var field = typeof(SavedFilterTabs).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(component, field.FieldType.IsEnum ? Enum.Parse(field.FieldType, (string)value!) : value);
    }

    private static void Invoke(SavedFilterTabs component, string name) =>
        typeof(SavedFilterTabs).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);

    private static Task InvokeAsync(SavedFilterTabs component, string name) =>
        (Task)typeof(SavedFilterTabs).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null)!;

    private sealed class UiSavedFilters : ISavedFilterService
    {
        private readonly List<SavedFilterDto> items;
        public UiSavedFilters()
        {
            Original = new(Guid.NewGuid(), "Saved", "{\"search\":\"saved\"}", DateTimeOffset.UtcNow);
            items = [Original];
        }
        public SavedFilterDto Original { get; }
        public (string Name, string Criteria)? LastCreated { get; private set; }
        public (Guid Id, string Criteria)? LastOverwritten { get; private set; }
        public Task<IReadOnlyList<SavedFilterDto>> ListAsync(Guid accountId, SavedFilterPage page,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SavedFilterDto>>(items.ToList());
        public Task<SavedFilterDto> CreateAsync(Guid accountId, SavedFilterPage page, string name,
            string criteriaJson, CancellationToken ct = default)
        {
            LastCreated = (name, criteriaJson);
            var item = new SavedFilterDto(Guid.NewGuid(), name, criteriaJson, DateTimeOffset.UtcNow);
            items.Add(item);
            return Task.FromResult(item);
        }
        public Task<SavedFilterDto> OverwriteAsync(Guid accountId, SavedFilterPage page, Guid filterId,
            string criteriaJson, CancellationToken ct = default)
        {
            LastOverwritten = (filterId, criteriaJson);
            var index = items.FindIndex(x => x.Id == filterId);
            var item = items[index] with { CriteriaJson = criteriaJson, UpdatedUtc = DateTimeOffset.UtcNow };
            items[index] = item;
            return Task.FromResult(item);
        }
    }

    private sealed class UiState
    {
        public SavedFilterTabs? Component { get; set; }
        public string? Applied { get; set; }
        public string? Edited { get; set; }
    }

    private sealed class UiHost : ComponentBase
    {
        [Parameter] public UiState State { get; set; } = null!;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            RenderFragment content = contentBuilder => contentBuilder.AddContent(0, "Filter fields");
            builder.OpenComponent<SavedFilterTabs>(0);
            builder.AddAttribute(1, "AccountId", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            builder.AddAttribute(2, "Page", SavedFilterPage.Agreements);
            builder.AddAttribute(3, "FilterContent", content);
            builder.AddAttribute(4, "CaptureCriteria", new Func<string>(() => "{\"captured\":true}"));
            builder.AddAttribute(5, "OnApplyCriteria", EventCallback.Factory.Create<string>(this, value => State.Applied = value));
            builder.AddAttribute(6, "OnEditCriteria", EventCallback.Factory.Create<string>(this, value => State.Edited = value));
            builder.AddAttribute(7, "OnSearch", EventCallback.Factory.Create(this, () => { }));
            builder.AddComponentReferenceCapture(8, value => State.Component = (SavedFilterTabs)value);
            builder.CloseComponent();
        }
    }
}
