using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.Agreements;

public static class AgreementEndpoints
{
    public static void MapAgreementEndpoints(this WebApplication app)
    {
        app.MapGet("/agreements/documents/{documentId:guid}/download", async (
            Guid documentId, HttpContext http, ICurrentUserContextService userContext,
            IAgreementService service, CancellationToken cancellationToken) =>
        {
            if (userContext.Current.CurrentAccountId is not Guid accountId) return Results.NotFound();
            try
            {
                var download = await service.DownloadAsync(accountId, documentId, cancellationToken);
                http.Response.Headers.CacheControl = "no-store";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(download.Content, download.MediaType, download.FileName);
            }
            catch (UnauthorizedAccessException) { return Results.NotFound(); }
            catch (FileNotFoundException) { return Results.NotFound(); }
            catch (DirectoryNotFoundException) { return Results.NotFound(); }
        }).RequireAuthorization();
    }
}
