namespace TenantPlatform.Core.Localization;

public static class SupportedLanguages
{
    public const string NbNo = "nb-NO";
    public const string EnGb = "en-GB";
    public const string SvSe = "sv-SE";

    public static readonly IReadOnlyList<SupportedLanguage> All =
    [
        new (NbNo, "Norsk"),
        new (EnGb, "English"),
        new (SvSe, "Svenska")
    ];
}

public record SupportedLanguage(
    string Code,
    string DisplayName);