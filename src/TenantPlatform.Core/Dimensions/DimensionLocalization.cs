using TenantPlatform.Core.Localization;

namespace TenantPlatform.Core.Dimensions;

public static class DimensionLocalization
{
    public static string Name(Dimension dimension, string? language, string? accountLanguage = null) =>
        TranslationHelper.ResolveText(Translations(dimension), x => x.LanguageCode, x => x.Name,
            language, accountLanguage, dimension.Name, dimension.Code) ?? dimension.Code;

    public static string? Description(Dimension dimension, string? language, string? accountLanguage = null) =>
        TranslationHelper.ResolveText(Translations(dimension), x => x.LanguageCode, x => x.Description,
            language, accountLanguage, dimension.Description);

    private static IEnumerable<DimensionTranslation> Translations(Dimension dimension) =>
        dimension.Translations.Where(x => x.AccountId == dimension.AccountId && x.DimensionId == dimension.Id);
}
