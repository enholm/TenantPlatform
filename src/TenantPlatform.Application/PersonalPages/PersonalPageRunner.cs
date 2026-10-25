using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TenantPlatform.Application.Agreements;
using TenantPlatform.Application.Mcp;
namespace TenantPlatform.Application.PersonalPages;

public sealed record PersonalPageResult(DateTimeOffset RetrievedUtc, AgreementReportSelection? Selection, AgreementAggregateResult? Aggregate, AgreementQueryResult? Table,
    AgreementReportGrouping GroupBy, PageColumn[] Columns, PageComponent[] Components, ReportArea Area = ReportArea.Agreements, ServiceRequestPageResult? Requests = null);
public sealed class PersonalPageRunner(PersonalPageService pages, McpRegistryService? registry, PersonalPageRuns runs, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public async Task<PersonalPageResult> RunAsync(PageDefinition definition, int page = 1, Guid? savedId = null, CancellationToken ct = default,
        Func<string, string, string, CancellationToken, Task<string>>? read = null)
    {
        var who = await pages.RequireAsync(ct);
        using var lease = runs.Start(who, ct, out var token);
        try
        {
            if (page is < 1 or > 10000) throw new PersonalPageException("Invalid");
            definition = PageDefinitions.Current(definition);
            await pages.ValidateSourceAsync(definition, token);
            if (savedId.HasValue) await CheckSaved();
            var result = await ReportAreas.Get(definition.Area).ReadAsync(definition, who.AccountId!.Value, page, Call, clock.GetUtcNow());
            await pages.ValidateSourceAsync(definition, token);
            if (await pages.RequireAsync(token) != who) throw new UnauthorizedAccessException();
            if (savedId.HasValue) await CheckSaved();
            token.ThrowIfCancellationRequested();
            return result with { RetrievedUtc = clock.GetUtcNow() };
        }
        catch (OperationCanceledException) { throw new PersonalPageException("Cancelled"); }
        catch (KeyNotFoundException) { throw new PersonalPageException("Contract"); }
        catch (JsonException) { throw new PersonalPageException("Contract"); }
        catch (McpClientException ex) { throw new PersonalPageException(ex.Code is "RESULT_TOO_LARGE" ? "Limit" : ex.Code is "INVALID_ARGUMENT" ? "Contract" : ex.Code is "CANCELLED" ? "Cancelled" : "SourceUnavailable"); }

        async Task CheckSaved()
        {
            var saved = PageDefinitions.Current((await pages.ReadAsync(savedId!.Value, token)).Definition);
            if (JsonSerializer.Serialize(saved, Json) != JsonSerializer.Serialize(definition, Json)) throw new PersonalPageException("Conflict");
        }
        async Task<JsonElement> Call(string name, string version, Dictionary<string, object?> args)
        {
            await pages.RequireAsync(token);
            if (registry is null) throw new PersonalPageException("SourceUnavailable");
            var arguments = JsonSerializer.Serialize(args, Json);
            var raw = read is null ? await registry.CallAsync(definition.ConnectionId, name, version, arguments, token) : await read(name, version, arguments, token);
            if (Encoding.UTF8.GetByteCount(raw) > 65536) throw new PersonalPageException("Limit");
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("structuredContent").Clone();
        }
    }
}
