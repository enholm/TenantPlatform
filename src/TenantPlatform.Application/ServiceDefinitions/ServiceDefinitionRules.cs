using TenantPlatform.Core.Localization;
using TenantPlatform.Core.Services;
namespace TenantPlatform.Application.ServiceDefinitions;

internal static class ServiceDefinitionRules
{
    public static void ValidateTranslations(
        IReadOnlyCollection<ServiceDefinitionTranslationRequest>
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

        if (!translations.Any(x =>
                !string.IsNullOrWhiteSpace(x.Name)))
        {
            throw new InvalidOperationException(
                "At least one language must have a name.");
        }
    }

    public static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    public static void ValidateProviderModel(
        ServiceDefinitionProviderEditDto model)
    {
        if (model.ServiceProviderOrganizationId ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "ServiceProviderRequired");
        }

        if (model.IntegrationType ==
                ServiceProviderIntegrationType.Email &&
            string.IsNullOrWhiteSpace(
                model.RequestEmailAddress))
        {
            throw new InvalidOperationException(
                "ServiceProviderEmailRequired");
        }

        if (!model.IsActive)
        {
            model.IsDefault = false;
        }

        if (model.IntegrationType !=
            ServiceProviderIntegrationType.Email)
        {
            model.RequestEmailAddress = null;
        }
        else
        {
            model.RequestEmailAddress =
                model.RequestEmailAddress?.Trim();
        }
    }
}
