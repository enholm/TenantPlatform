using System.Text.Json;
using TenantPlatform.Core.Localization;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceDefinitionFields;

internal static class ServiceDefinitionFieldRules
{
    public static void ValidateTranslations(
        IReadOnlyCollection<ServiceDefinitionFieldTranslationRequest>
            translations)
    {
        var duplicateLanguageCode =
            translations
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.LanguageCode))
                .GroupBy(
                    x => x.LanguageCode.Trim(),
                    StringComparer.OrdinalIgnoreCase)
                .Any(x => x.Count() > 1);

        if (duplicateLanguageCode)
        {
            throw new InvalidOperationException(
                "A language can only occur once.");
        }

        var unsupportedLanguage =
            translations
                .Where(x =>
                    !string.IsNullOrWhiteSpace(
                        x.LanguageCode))
                .Select(x =>
                    x.LanguageCode.Trim())
                .FirstOrDefault(languageCode =>
                    !SupportedLanguages.All.Any(
                        language =>
                            string.Equals(
                                language.Code,
                                languageCode,
                                StringComparison.OrdinalIgnoreCase)));

        if (unsupportedLanguage is not null)
        {
            throw new InvalidOperationException(
                $"Language '{unsupportedLanguage}' is not supported.");
        }
    }

    public static string NormalizeKey(
        string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    public static string? SerializeOptions(
        ServiceFieldType fieldType,
        string? optionsText)
    {
        if (fieldType is not
            (ServiceFieldType.Choice or
             ServiceFieldType.MultiChoice))
        {
            return null;
        }

        var options = ParseOptions(optionsText);

        if (options.Count == 0)
        {
            throw new InvalidOperationException(
                "Choice fields must contain at least one option.");
        }

        return JsonSerializer.Serialize(
            options.Select(x => new
            {
                value = x
            }));
    }

    public static string? DeserializeOptions(
        string? options)
    {
        if (string.IsNullOrWhiteSpace(options))
        {
            return null;
        }

        try
        {
            using var document =
                JsonDocument.Parse(options);

            return string.Join(
                Environment.NewLine,
                document.RootElement
                    .EnumerateArray()
                    .Select(x =>
                        x.GetProperty("value").GetString())
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static List<string> ParseOptions(
        string? optionsText)
    {
        if (string.IsNullOrWhiteSpace(optionsText))
        {
            return [];
        }

        return optionsText
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ServiceDefinitionFieldTranslation
        CreateTranslation(
            Guid fieldId,
            string languageCode,
            string label,
            string? placeholder,
            string? helpText)
    {
        return new ServiceDefinitionFieldTranslation
        {
            Id = Guid.NewGuid(),
            ServiceDefinitionFieldId = fieldId,
            LanguageCode = languageCode,
            Label = label.Trim(),
            Placeholder = NormalizeOptional(placeholder),
            HelpText = NormalizeOptional(helpText)
        };
    }

    public static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
