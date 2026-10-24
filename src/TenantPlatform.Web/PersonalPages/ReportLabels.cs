using Microsoft.Extensions.Localization;
using TenantPlatform.Core.Agreements;

namespace TenantPlatform.Web.PersonalPages;

// Only known enum labels are exposed; the renderer never interprets arbitrary field names.
public static class ReportLabels
{
    private static string Name(string prefix, Enum value) => "data-" + prefix + string.Concat(value.ToString().Select(c => char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : c.ToString()));
    public static Dictionary<string, object> Attributes(IStringLocalizer<TenantPlatformResources> l)
    {
        var result = new Dictionary<string, object>();
        foreach (var status in Enum.GetValues<AgreementStatus>()) result[Name("status", status)] = l["AgreementStatus" + status].Value;
        foreach (var type in Enum.GetValues<AgreementType>()) result[Name("type", type)] = l["AgreementType" + type].Value;
        return result;
    }
}
