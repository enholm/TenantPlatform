using TenantPlatform.Core.Dimensions;
using TenantPlatform.Core.Localization;

static class LocalizationChecks
{
    public static Dimension Example()
    {
        var dimension = new Dimension { Id = Guid.NewGuid(), AccountId = Guid.NewGuid(), Name = "Stored name", Code = "STABLE", Description = "Stored description" };
        dimension.Translations = new[] { ("nb-NO", "Norsk navn", "Norsk beskrivelse"), ("en-GB", "English name", "English description"), ("sv-SE", "Svenskt namn", "Svensk beskrivning") }
            .Select(x => new DimensionTranslation { Id = Guid.NewGuid(), AccountId = dimension.AccountId, DimensionId = dimension.Id,
                LanguageCode = x.Item1, Name = x.Item2, Description = x.Item3 }).ToList();
        return dimension;
    }
    public static void Run()
    {
        var d = Example();
        foreach (var (language, name, description) in new[] {
            ("nb-NO", "Norsk navn", "Norsk beskrivelse"), ("nb", "Norsk navn", "Norsk beskrivelse"), ("NO", "Norsk navn", "Norsk beskrivelse"), ("no-NO", "Norsk navn", "Norsk beskrivelse"),
            ("en-GB", "English name", "English description"), ("en-US", "English name", "English description"), ("en", "English name", "English description"),
            ("sv-SE", "Svenskt namn", "Svensk beskrivning"), ("sv", "Svenskt namn", "Svensk beskrivning"), (" SV-fi ", "Svenskt namn", "Svensk beskrivning") })
        {
            Verify.That(DimensionLocalization.Name(d, language, "en-GB") == name && DimensionLocalization.Description(d, language, "en-GB") == description,
                "requested language/alias resolves both fields: " + language);
        }
        d.Translations.Single(x => x.LanguageCode == "sv-SE").Name = " \t";
        d.Translations.Single(x => x.LanguageCode == "nb-NO").Description = " ";
        Verify.That(DimensionLocalization.Name(d, "sv-SE", "nb-NO") == "Norsk navn", "empty requested name uses account default");
        Verify.That(DimensionLocalization.Description(d, "nb-NO", "en-GB") == "English description", "name-only translation does not block description fallback");
        Verify.That(DimensionLocalization.Description(d, "sv-SE", "nb-NO") == "Svensk beskrivning", "description-only requested translation is used independently of name");
        Verify.That(DimensionLocalization.Name(d, "sv-SE", null) == "Stored name", "missing account language uses stored fallback, never implicit English");
        Verify.That(DimensionLocalization.Name(d, "sv-SE", "sv-SE") == "Stored name", "missing account translation uses stored fallback, never first row");
        Verify.That(DimensionLocalization.Name(d, "de-DE", "no") == "Norsk navn", "unknown requested language uses supported account alias");
        Verify.That(DimensionLocalization.Name(d, null, "nb") == "Norsk navn", "missing requested language does not imply English");
        Verify.That(DimensionLocalization.Name(d, "de-DE", "xx-XX") == "Stored name", "unknown languages do not accidentally resolve to English");
        var sparse = Example();
        sparse.Translations = sparse.Translations.Where(x => x.LanguageCode == "en-GB").ToList();
        Verify.That(DimensionLocalization.Name(sparse, "nb-NO", "en-GB") == "English name", "absent requested translation row uses account translation");
        Verify.That(DimensionLocalization.Name(sparse, "nb-NO", "sv-SE") == "Stored name" &&
            DimensionLocalization.Description(sparse, "nb-NO", "sv-SE") == "Stored description", "absent requested and account rows use stored text without English/first-row fallback");
        d.Name = " "; d.Description = " ";
        Verify.That(DimensionLocalization.Name(d, "sv-SE", null) == d.Code && DimensionLocalization.Description(d, "nb-NO", null) is null,
            "final name fallback is stable code; description may be empty");
        var wrongOwner = new DimensionTranslation { AccountId = Guid.NewGuid(), DimensionId = d.Id, LanguageCode = "nb-NO", Name = "FOREIGN" };
        d.Translations = [wrongOwner];
        Verify.That(DimensionLocalization.Name(d, "nb", "en") == "STABLE", "foreign-account translations are not displayed");
        wrongOwner.AccountId = d.AccountId; wrongOwner.DimensionId = Guid.NewGuid();
        Verify.That(DimensionLocalization.Name(d, "nb", "en") == "STABLE", "translations from another dimension are not displayed");
        var legacy = new[] { new LegacyTranslation("sv-SE", "Swedish first"), new LegacyTranslation("en-GB", "English") };
        Verify.That(TranslationHelper.Select(legacy, x => x.Language, "nb", "nb")?.Text == "English", "legacy Select keeps English fallback");
        Verify.That(TranslationHelper.Select(legacy.Take(1), x => x.Language, "nb", "nb")?.Text == "Swedish first", "legacy Select keeps first-row fallback");
        Verify.That(LanguageHelper.Normalize(null) == "en-GB" && LanguageHelper.Normalize("unknown") == "en-GB", "legacy language normalization remains unchanged");
    }
    private sealed record LegacyTranslation(string Language, string Text);
}
