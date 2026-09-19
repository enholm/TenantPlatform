namespace TenantPlatform.Web.Services.Agreements;

/// <summary>Runtime tzdata/ICU catalog, shared by the selector and server-side validation.</summary>
public static class AgreementTimeZones
{
    private static readonly Lazy<IReadOnlyList<string>> Catalog = new(BuildCatalog);
    public static IReadOnlyList<string> Ids => Catalog.Value;

    public static bool IsSupported(string? id)
    {
        if (!CanResolveIana(id)) return false;
        // ICU recognizes legacy IANA links omitted from the standard system catalog.
        // Do not accept arbitrary OS-local tzfiles such as "localtime" as IANA IDs.
        return id == "UTC" || Ids.Contains(id, StringComparer.Ordinal) ||
            TimeZoneInfo.TryConvertIanaIdToWindowsId(id!, out _);
    }

    private static bool CanResolveIana(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || id != id.Trim()) return false;
        try
        {
            var zone = AgreementReminderSchedule.Zone(id);
            // UTC is both an IANA link and the special cross-platform runtime UTC identifier.
            // HasIanaId distinguishes IANA lookups from Windows IDs even on Windows.
            return id == "UTC" || zone.HasIanaId;
        }
        catch (AgreementValidationException) { return false; }
    }

    public static string Validate(string? id) => IsSupported(id)
        ? id! // Preserve supported aliases exactly; never persist the runtime's canonical/Windows ID.
        : throw new AgreementValidationException("FollowupInvalidTimeZone");

    public static IReadOnlyList<string> Options(string? savedId)
    {
        if (!IsSupported(savedId) || Ids.Contains(savedId, StringComparer.Ordinal)) return Ids;
        return Ids.Append(savedId!).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> BuildCatalog()
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var zone in TimeZoneInfo.GetSystemTimeZones())
        {
            var id = zone.HasIanaId ? zone.Id :
                TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;
            if (CanResolveIana(id)) ids.Add(id!);
        }
        ids.Add("UTC");
        // Keep the existing default selectable on Windows, whose system list may map
        // a group of IANA locations to just one representative ID.
        var defaultId = new SaveAgreementReminderSettings().TimeZoneId;
        if (CanResolveIana(defaultId)) ids.Add(defaultId);
        return ids.ToList().AsReadOnly();
    }
}
