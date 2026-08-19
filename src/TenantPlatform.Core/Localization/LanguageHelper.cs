namespace TenantPlatform.Core.Localization;

public static class LanguageHelper
{
    public static string Normalize(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return SupportedLanguages.EnGb;
        }

        var value = languageCode.Trim();

        var exactMatch = SupportedLanguages.All
            .FirstOrDefault(x =>
                string.Equals(
                    x,
                    value,
                    StringComparison.OrdinalIgnoreCase));

        if (exactMatch is not null)
        {
            return exactMatch;
        }

        if (value.StartsWith(
            "nb",
            StringComparison.OrdinalIgnoreCase))
        {
            return SupportedLanguages.NbNo;
        }

        if (value.StartsWith(
            "no",
            StringComparison.OrdinalIgnoreCase))
        {
            return SupportedLanguages.NbNo;
        }

        if (value.StartsWith(
            "sv",
            StringComparison.OrdinalIgnoreCase))
        {
            return SupportedLanguages.SvSe;
        }

        if (value.StartsWith(
            "en",
            StringComparison.OrdinalIgnoreCase))
        {
            return SupportedLanguages.EnGb;
        }

        return SupportedLanguages.EnGb;
    }

    public static bool IsSupported(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        return SupportedLanguages.All.Any(x =>
            string.Equals(
                x,
                languageCode,
                StringComparison.OrdinalIgnoreCase));
    }
}
