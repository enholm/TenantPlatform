using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Localization;
using TenantPlatform.Web.Email;

namespace TenantPlatform.Web.Services.Agreements;

public class AgreementReminderOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public string TransportMode { get; set; } = "Capture";
    public string ApplicationBaseUrl { get; set; } = "";
    public string CapturePath { get; set; } = "App_Data/agreement-reminders";
}
public record AgreementReminderMessage(Guid QueueId, Guid AccountId, Guid AgreementId, string Recipient,
    string Language, string AgreementTitle, AgreementDeadlineKind Kind, DateOnly DueDate);
public interface IAgreementReminderTransport
{
    Task<string?> SendAsync(AgreementReminderMessage message, CancellationToken ct);
}

// The queue is dedicated to agreements; the existing service-request outbox is not suitable
// because it requires a ServiceRequestId and writes service-request message history.
public class AgreementReminderTransport(IOptions<AgreementReminderOptions> options, IHostEnvironment environment,
    IEmailSender email, IStringLocalizer<TenantPlatformResources> localizer) : IAgreementReminderTransport
{
    public async Task<string?> SendAsync(AgreementReminderMessage message, CancellationToken ct)
    {
        var config = options.Value;
        if (!Uri.TryCreate(config.ApplicationBaseUrl, UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("https" or "http")) throw new AgreementValidationException("FollowupTransportNotConfigured");
        var culture = CultureInfo.GetCultureInfo(SupportedLanguages.All.Any(x => x.Code == message.Language)
            ? message.Language : SupportedLanguages.NbNo);
        var previousCulture = CultureInfo.CurrentCulture; var previousUi = CultureInfo.CurrentUICulture;
        string subject, body;
        try
        {
            CultureInfo.CurrentCulture = culture; CultureInfo.CurrentUICulture = culture;
            subject = localizer["FollowupEmailSubject", message.AgreementTitle];
            var url = config.ApplicationBaseUrl.TrimEnd('/') + "/agreements/" + message.AgreementId;
            body = localizer["FollowupEmailBody", message.AgreementTitle, localizer[$"FollowupKind{message.Kind}"],
                message.DueDate.ToString("d", culture), url];
        }
        finally { CultureInfo.CurrentCulture = previousCulture; CultureInfo.CurrentUICulture = previousUi; }
        if (config.TransportMode == "Capture")
        {
            var root = Path.GetFullPath(config.CapturePath, environment.ContentRootPath);
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, message.QueueId.ToString("N") + ".json");
            var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".partial");
            try
            {
                await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                    await JsonSerializer.SerializeAsync(file, new { message.QueueId, message.AccountId, message.Recipient, subject, body }, cancellationToken: ct);
                try { File.Move(temporary, path, overwrite: false); }
                catch (IOException) when (File.Exists(path)) { /* Same queue ID already captured completely. */ }
            }
            finally { File.Delete(temporary); }
            return "capture:" + message.QueueId.ToString("N");
        }
        if (config.TransportMode != "Smtp" || environment.IsDevelopment() || environment.IsEnvironment("Test") ||
            environment.IsEnvironment("Testing") || baseUri.Scheme != "https")
            throw new AgreementValidationException("FollowupTransportNotConfigured");
        await email.SendAsync(message.Recipient, null, subject, body, cancellationToken: ct);
        return null; // Existing SMTP adapter exposes neither a transport ID nor idempotency support.
    }
}
