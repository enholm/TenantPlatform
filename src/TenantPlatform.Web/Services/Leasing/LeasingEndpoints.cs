using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.Leasing;

public static class LeasingEndpoints
{
    public static void MapLeasingEndpoints(this WebApplication app)
    {
        app.MapGet("/leasing/documents/{id:guid}/download", async (Guid id, HttpContext http,
            ICurrentUserContextService user, LeasingService service, CancellationToken ct) =>
        {
            if (user.Current.CurrentAccountId is not Guid account) return Results.NotFound();
            try
            {
                var file = await service.DownloadAsync(account, id, ct);
                http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(file.Content, file.MediaType, file.FileName);
            }
            catch (UnauthorizedAccessException) { return Results.NotFound(); }
            catch (IOException) { return Results.NotFound(); }
        }).RequireAuthorization();
        app.MapGet("/leasing/invoices/{id:guid}/original", async (Guid id, bool? download, HttpContext http,
            ICurrentUserContextService user, LeasingService service, CancellationToken ct) =>
        {
            if (user.Current.CurrentAccountId is not Guid account) return Results.NotFound();
            try
            {
                var file = await service.DownloadInvoiceAsync(account, id, ct);
                http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.XContentTypeOptions = "nosniff";
                if (download == true || file.MediaType == "application/xml") return Results.File(file.Content, file.MediaType, file.FileName);
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'self'";
                return Results.File(file.Content, file.MediaType);
            }
            catch (UnauthorizedAccessException) { return Results.NotFound(); }
            catch (IOException) { return Results.NotFound(); }
        }).RequireAuthorization();
        app.MapGet("/leasing/rental-invoices/{id:guid}/original", async (Guid id, bool? download, HttpContext http,
            ICurrentUserContextService user, LeasingService service, CancellationToken ct) =>
        {
            if (user.Current.CurrentAccountId is not Guid account) return Results.NotFound();
            try
            {
                var file = await service.DownloadRentalInvoiceAsync(account, id, ct);
                http.Response.Headers.CacheControl = "no-store"; http.Response.Headers.XContentTypeOptions = "nosniff";
                if (download == true || file.MediaType == "application/xml") return Results.File(file.Content, file.MediaType, file.FileName);
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'self'";
                return Results.File(file.Content, file.MediaType);
            }
            catch (UnauthorizedAccessException) { return Results.NotFound(); }
            catch (IOException) { return Results.NotFound(); }
        }).RequireAuthorization();
    }

}
