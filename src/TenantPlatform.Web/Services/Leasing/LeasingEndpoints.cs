using TenantPlatform.Web.Security.CurrentUserContext;

namespace TenantPlatform.Web.Services.Leasing;

public static class LeasingEndpoints
{
    public static void MapLeasingEndpoints(this WebApplication app)
    {
        app.MapGet("/leasing/reports/export", async (int kind,string filter,string format,ICurrentUserContextService user,LeasingService service,Microsoft.Extensions.Localization.IStringLocalizer<TenantPlatform.Web.TenantPlatformResources> l,HttpContext http,CancellationToken ct) =>
        {
            if(user.Current.CurrentAccountId is not Guid account)return Results.NotFound();
            try
            {
                if(filter.Length>10000||format is not("csv" or "xlsx"))return Results.BadRequest();
                var input=System.Text.Json.JsonSerializer.Deserialize<LeasingReportFilter>(filter)??new();
                var report=await service.GetLeasingReportAsync(account,(LeasingReportKind)kind,input,export:true,ct:ct);
                var bytes=LeasingReportExport.Create(report,input,format=="xlsx",l);http.Response.Headers.CacheControl="no-store";http.Response.Headers.XContentTypeOptions="nosniff";
                return Results.File(bytes,format=="xlsx"?"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":"text/csv; charset=utf-8",$"leasing-{kind}.{format}");
            }
            catch(UnauthorizedAccessException){return Results.NotFound();}
            catch(System.Text.Json.JsonException){return Results.BadRequest();}
            catch(LeasingValidationException ex){return Results.BadRequest(l[ex.Message].Value);}
        }).RequireAuthorization();
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
