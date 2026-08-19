namespace TenantPlatform.Core.Localization;

public static class TranslationHelper
{
    public static TTranslation? Select<TTranslation>(
        IEnumerable<TTranslation> translations,
        Func<TTranslation, string> languageSelector,
        string? requestedLanguage,
        string? defaultLanguage = null)
    {
        var items = translations.ToList();

        if (items.Count == 0)
        {
            return default;
        }

        var requested =
            LanguageHelper.Normalize(requestedLanguage);

        var accountDefault =
            LanguageHelper.Normalize(defaultLanguage);

        var match = Find(
            items,
            languageSelector,
            requested);

        if (match is not null)
        {
            return match;
        }

        if (!string.Equals(
            requested,
            accountDefault,
            StringComparison.OrdinalIgnoreCase))
        {
            match = Find(
                items,
                languageSelector,
                accountDefault);

            if (match is not null)
            {
                return match;
            }
        }

        if (!string.Equals(
            requested,
            SupportedLanguages.EnGb,
            StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                accountDefault,
                SupportedLanguages.EnGb,
                StringComparison.OrdinalIgnoreCase))
        {
            match = Find(
                items,
                languageSelector,
                SupportedLanguages.EnGb);

            if (match is not null)
            {
                return match;
            }
        }

        return items.FirstOrDefault();
    }

    private static TTranslation? Find<TTranslation>(
        IEnumerable<TTranslation> translations,
        Func<TTranslation, string> languageSelector,
        string languageCode)
    {
        return translations.FirstOrDefault(x =>
            string.Equals(
                languageSelector(x),
                languageCode,
                StringComparison.OrdinalIgnoreCase));
    }
}

