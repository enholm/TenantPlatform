using Microsoft.Extensions.Localization;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.PersonalPages;

// Only known enum labels are exposed; the renderer never interprets arbitrary field names.
public static class ReportLabels
{
    public static string ComponentKey(TenantPlatform.Application.PersonalPages.ReportArea area, string component) =>
        area == TenantPlatform.Application.PersonalPages.ReportArea.ServiceRequests && component == "AgreementTable" ? "RequestReportTable" : "PagesComponent" + component;
    private static string Name(string prefix, Enum value) => "data-" + prefix + string.Concat(value.ToString().Select(c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));
    public static Dictionary<string, object> Attributes(IStringLocalizer<TenantPlatformResources> l)
    {
        var result = new Dictionary<string, object>();
        foreach (var status in Enum.GetValues<AgreementStatus>()) result[Name("status", status)] = l["AgreementStatus" + status].Value;
        foreach (var type in Enum.GetValues<AgreementType>()) result[Name("type", type)] = l["AgreementType" + type].Value;
        foreach (var status in Enum.GetValues<TenantPlatform.Core.Services.ServiceRequestStatus>()) result[Name("request-status", status)] = l["ServiceRequestStatus_" + status].Value;
        foreach (var key in new[] { "CreatedAt", "SubmittedAt", "CompletedAt", "Unit" }) result[Name("request", Enum.Parse<TenantPlatform.Application.PersonalPages.PageColumn>(key))] = l["PagesColumn" + key].Value;
        result["data-request-total"] = l["RequestReportTotal"].Value;
        result["data-request-all"] = l["RequestReportAll"].Value;
        return result;
    }
}
