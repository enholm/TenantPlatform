using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Antiforgery;
using TenantPlatform.Application.PersonalPages;
using TenantPlatform.Web.Agent;
namespace TenantPlatform.Web.PersonalPages;

public static class PersonalPagesEndpoints
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    public sealed record Command(Guid? Id, Guid Revision, PageDefinition? Definition, int Page = 1, int? Move = null);
    public static void MapPersonalPages(this WebApplication app)
    {
        app.MapGet("/my-pages/context", async (HttpContext http) => { var identity = await ReadAgentEndpoints.Identity(http, http.RequestAborted); http.Response.Headers.CacheControl = "no-store"; return identity is null ? Results.Unauthorized() : Results.Json(new { context = identity.Value.Key }); });
        app.MapPost("/my-pages/action/{action}", async (string action, HttpContext http, IAntiforgery csrf, PersonalPageService service, PersonalPageRunner runner, PersonalPageRuns runs) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try
            {
                if (await ReadAgentEndpoints.Identity(http, http.RequestAborted) is null) return Results.Unauthorized();
                if (http.Request.ContentLength > 32768) return Results.StatusCode(413);
                http.Request.EnableBuffering(32768, 32768);
                await csrf.ValidateRequestAsync(http);
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted); budget.CancelAfter(TimeSpan.FromSeconds(45)); var ct = budget.Token;
                var command = await JsonSerializer.DeserializeAsync<Command>(http.Request.Body, Json, ct) ?? throw new PersonalPageException("Invalid");
                var writeToken = ct;
                using var mutation = action is "save" or "delete" or "move" ? runs.Start(await service.RequireAsync(ct), ct, out writeToken) : null;
                if (mutation is not null) ct = writeToken;
                object result;
                switch (action)
                {
                    case "save": result = await service.SaveAsync(command.Id, command.Revision, command.Definition ?? throw new PersonalPageException("Invalid"), ct); break;
                    case "delete": result = await service.ChangeAsync(command.Id ?? throw new PersonalPageException("Invalid"), command.Revision, null, ct); break;
                    case "move": result = await service.ChangeAsync(command.Id ?? throw new PersonalPageException("Invalid"), command.Revision, command.Move ?? throw new PersonalPageException("Invalid"), ct); break;
                    case "preview": result = await runner.RunAsync(command.Definition ?? throw new PersonalPageException("Invalid"), command.Page, ct: ct); break;
                    case "run":
                        var row = await service.ReadAsync(command.Id ?? throw new PersonalPageException("Invalid"), ct);
                        result = await runner.RunAsync(row.Definition, command.Page, row.Id, ct); break;
                    default: return Results.BadRequest();
                }
                if (result is PersonalPageCollection collection) result = new { collection.Revision, Pages = collection.Pages.Select(p => new { p.Id, p.Definition.Title }).ToArray() };
                if (JsonSerializer.SerializeToUtf8Bytes(result, Json).Length > 131072) throw new PersonalPageException("Limit");
                return Results.Json(result, Json);
            }
            catch (UnauthorizedAccessException) { return Failure(http, "Access", 403); }
            catch (PersonalPageException ex) { return Failure(http, ex.Code, ex.Code == "Conflict" ? 409 : 400); }
            catch (OperationCanceledException) { return Failure(http, "Cancelled", 408); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            catch (JsonException) { return Failure(http, "Invalid", 400); }
            catch (Exception) { return Failure(http, "Unavailable", 503); }
        });
    }
    private static IResult Failure(HttpContext http, string code, int status)
    {
        var l = http.RequestServices.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<TenantPlatformResources>>();
        var key = "PagesError" + code; var message = l[key];
        return Results.Json(new { error = message.ResourceNotFound ? l["PagesErrorUnavailable"].Value : message.Value }, statusCode: status);
    }
}
