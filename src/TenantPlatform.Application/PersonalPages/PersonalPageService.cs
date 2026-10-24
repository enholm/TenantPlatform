using System.Text.Json;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Mcp;
using TenantPlatform.Application.Security;
namespace TenantPlatform.Application.PersonalPages;

public sealed class PersonalPageService(ICurrentActor actor, IAuthorizationService authorization, IPersonalPageData data, McpRegistryService? registry)
{
    public const int MaxPages = 20;
    public async Task<ActorSnapshot> RequireAsync(CancellationToken ct)
    {
        var who = actor.GetSnapshot();
        if (!who.IsAuthenticated || who.AccountId is not Guid account || !await authorization.CanAsync(Permissions.McpConnections.Use, new(ResourceKind.Account, AccountId: account), ct)) throw new UnauthorizedAccessException();
        return who;
    }
    public async Task<PersonalPageCollection> ListAsync(CancellationToken ct = default)
    {
        var who = await RequireAsync(ct); var collection = await data.ReadAsync(who.AccountId!.Value, who.UserId, ct);
        RequireSupportedVersions(collection); return collection;
    }
    // The collection is persisted as one JSON document. Never round-trip an unknown future
    // definition through today's model, including when an operation changes another page.
    private static void RequireSupportedVersions(PersonalPageCollection collection)
    {
        if (collection.Pages.Any(p => p.Definition.Version is not (1 or 2))) throw new PersonalPageException("Version");
    }
    public async Task<PersonalPage> ReadAsync(Guid id, CancellationToken ct = default) =>
        (await ListAsync(ct)).Pages.SingleOrDefault(x => x.Id == id) ?? throw new UnauthorizedAccessException();
    public async Task<PageSource[]> SourcesAsync(CancellationToken ct = default)
    {
        await RequireAsync(ct);
        if (registry is null) return [];
        var page = await registry.ListAsync(ct); var available = (await registry.AvailableToolsAsync(ct)).Where(t => t.UseClass == TenantPlatform.Core.Identity.McpToolUse.AutomaticRead).ToArray();
        return page.Servers.Where(s => page.Profiles.Any(p => p.Id == s.ProfileId && p.Version == s.ProfileVersion && !p.Binding.External))
            .Select(s => { var q = available.SingleOrDefault(t => t.ConnectionId == s.Id && t.Name == "agreements_query"); var a = available.SingleOrDefault(t => t.ConnectionId == s.Id && t.Name == "agreements_aggregate");
                return q is null && a is null ? null : new PageSource(s.Id, s.Name, q?.Version ?? "", a?.Version ?? "", s.ProfileVersion); }).OfType<PageSource>().ToArray();
    }
    public static void Validate(PageDefinition d)
    {
        if (d.Version is not (1 or 2)) throw new PersonalPageException("Version");
        if (string.IsNullOrWhiteSpace(d.Title) || d.Title.Length > 100 || d.Title.Any(char.IsControl) || d.ConnectionId == Guid.Empty ||
            d.ProfileVersion.Length is < 1 or > 200 || d.QueryVersion.Length > 200 || d.AggregateVersion.Length > 200 ||
            PageDefinitions.NeedsQuery(d) && d.QueryVersion.Length == 0 || PageDefinitions.NeedsAggregate(d) && d.AggregateVersion.Length == 0 || d.Layout != "Stacked" ||
            d.Components.Length is < 1 or > 3 || d.Components.Distinct().Count() != d.Components.Length || d.Components.Any(x => !Enum.IsDefined(x)) ||
            JsonSerializer.SerializeToUtf8Bytes(d).Length > 16384) throw new PersonalPageException("Invalid");
        if (!Enum.IsDefined(d.GroupBy) ||
            (d.Version == 1 ? d.Components.Length != 3 || d.GroupBy != AgreementReportGrouping.Building || d.Columns is not null :
                d.Columns is null || d.Columns.Length > 8 || d.Columns.Distinct().Count() != d.Columns.Length || d.Columns.Any(c => !Enum.IsDefined(c)) ||
                PageDefinitions.NeedsQuery(d) && d.Columns.Length == 0)) throw new PersonalPageException("Invalid");
        try { AgreementReportFilterRules.Validate(d.Filter); } catch (ArgumentException) { throw new PersonalPageException("Invalid"); }
        if (d.Filter.EndDateFrom.HasValue || d.Filter.EndDateTo.HasValue || d.Filter.EndsWithinMonths.HasValue || !Enum.IsDefined(d.Period.Kind) ||
            (d.Period.Kind == PagePeriodKind.All ? d.Version < 2 || d.Period.Months.HasValue || d.Period.From.HasValue || d.Period.To.HasValue :
             d.Period.Kind == PagePeriodKind.NextMonths ? d.Period.Months is not (>= 1 and <= 12) || d.Period.From.HasValue || d.Period.To.HasValue :
                d.Period.Months.HasValue || !d.Period.From.HasValue || !d.Period.To.HasValue || d.Period.From > d.Period.To)) throw new PersonalPageException("Invalid");
    }
    public async Task ValidateSourceAsync(PageDefinition d, CancellationToken ct)
    {
        Validate(d);
        if (!(await SourcesAsync(ct)).Any(s => s.ConnectionId == d.ConnectionId && (!PageDefinitions.NeedsQuery(d) || s.QueryVersion == d.QueryVersion) && (!PageDefinitions.NeedsAggregate(d) || s.AggregateVersion == d.AggregateVersion) && s.ProfileVersion == d.ProfileVersion))
        {
            if (registry is null) throw new PersonalPageException("SourceUnavailable");
            var state = await registry.DescribeAsync(d.ConnectionId, ct);
            throw new PersonalPageException(state.Reason == McpAvailability.AuthorizationRequired ? "Authorization" : state.Reason == McpAvailability.AccessDenied ? "Access" : "SourceChanged");
        }
    }
    public async Task<PersonalPageCollection> SaveAsync(Guid? id, Guid revision, PageDefinition definition, CancellationToken ct = default)
        => await PersistAsync(id, revision, definition, null, ct);
    // Stable creation ID is generated by the server-side draft store, never supplied as caller identity or ownership.
    public async Task<Guid> CreateFromDraftAsync(Guid creationId, Guid revision, PageDefinition definition, CancellationToken ct = default)
    {
        definition = PageDefinitions.Current(definition);
        if (creationId == Guid.Empty) throw new PersonalPageException("Invalid");
        var current = await ListAsync(ct);
        await ValidateSourceAsync(definition, ct); // Idempotent retries still require current authority.
        if (current.Pages.SingleOrDefault(p => p.Id == creationId) is { } existing)
        {
            if (JsonSerializer.Serialize(existing.Definition) != JsonSerializer.Serialize(definition with { Title = definition.Title.Trim() })) throw new PersonalPageException("Conflict");
            return existing.Id;
        }
        await PersistAsync(null, revision, definition, creationId, ct);
        return creationId;
    }
    // The draft store supplies a stable commit revision. It proves a lost-response retry without accepting an unrelated identical edit.
    public async Task<Guid> UpdateFromDraftAsync(Guid id, Guid revision, Guid commitRevision, PageDefinition original, PageDefinition definition, CancellationToken ct = default)
    {
        definition = PageDefinitions.Current(definition);
        if (id == Guid.Empty || commitRevision == Guid.Empty || commitRevision == revision) throw new PersonalPageException("Invalid");
        var who = await RequireAsync(ct);
        var current = await ListAsync(ct);
        var row = current.Pages.SingleOrDefault(p => p.Id == id) ?? throw new UnauthorizedAccessException();
        await ValidateSourceAsync(definition, ct); // Even an idempotent retry requires fresh authority.
        if (await RequireAsync(ct) != who) throw new UnauthorizedAccessException();
        if (current.Revision == commitRevision && Same(row.Definition, definition)) return id;
        if (current.Revision != revision || !Same(row.Definition, original)) throw new PersonalPageException("Conflict");
        await PersistAsync(id, revision, definition, null, ct, commitRevision);
        return id;
    }
    private static bool Same(PageDefinition a, PageDefinition b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b with { Title = b.Title.Trim() });
    private async Task<PersonalPageCollection> PersistAsync(Guid? id, Guid revision, PageDefinition definition, Guid? creationId, CancellationToken ct, Guid? commitRevision = null)
    {
        var who = await RequireAsync(ct); definition = PageDefinitions.Current(definition);
        var current = await data.ReadAsync(who.AccountId!.Value, who.UserId, ct);
        RequireSupportedVersions(current);
        if (id.HasValue && !current.Pages.Any(p => p.Id == id)) throw new UnauthorizedAccessException();
        if (current.Revision != revision) throw new PersonalPageException("Conflict");
        await ValidateSourceAsync(definition, ct);
        if (!id.HasValue && current.Pages.Length >= MaxPages) throw new PersonalPageException("Limit");
        var row = new PersonalPage(id ?? creationId ?? Guid.NewGuid(), who.AccountId.Value, who.UserId, definition with { Title = definition.Title.Trim() });
        var pages = id.HasValue ? current.Pages.Select(p => p.Id == id ? row : p).ToArray() : [..current.Pages, row];
        var next = new PersonalPageCollection(commitRevision ?? Guid.NewGuid(), pages);
        if (await RequireAsync(ct) != who) throw new UnauthorizedAccessException();
        await data.SaveAsync(who.AccountId.Value, who.UserId, revision, next, ct); return next;
    }
    public async Task<PersonalPageCollection> ChangeAsync(Guid id, Guid revision, int? move, CancellationToken ct = default)
    {
        var who = await RequireAsync(ct); var current = await ListAsync(ct);
        var index = Array.FindIndex(current.Pages, p => p.Id == id); if (index < 0) throw new UnauthorizedAccessException();
        if (current.Revision != revision) throw new PersonalPageException("Conflict");
        var rows = current.Pages.ToList();
        if (move is null) rows.RemoveAt(index);
        else { if (move is not (-1 or 1) || index + move < 0 || index + move >= rows.Count) throw new PersonalPageException("Invalid"); (rows[index], rows[index + move.Value]) = (rows[index + move.Value], rows[index]); }
        var next = new PersonalPageCollection(Guid.NewGuid(), rows.ToArray());
        if (await RequireAsync(ct) != who) throw new UnauthorizedAccessException();
        await data.SaveAsync(who.AccountId!.Value, who.UserId, revision, next, ct); return next;
    }
}
