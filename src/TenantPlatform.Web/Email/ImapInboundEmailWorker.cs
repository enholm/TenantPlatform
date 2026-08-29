using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TenantPlatform.Web.Email;

public class ImapInboundEmailWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ImapOptions _options;
    private readonly ILogger<ImapInboundEmailWorker> _logger;

    public ImapInboundEmailWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<ImapOptions> options,
        ILogger<ImapInboundEmailWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation(
                "Inbound email worker is disabled.");

            return;
        }

        _logger.LogInformation(
            "Inbound email worker started for {Host}:{Port}.",
            _options.Host,
            _options.Port);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessInboxAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error processing inbound email.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(15),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessInboxAsync(
        CancellationToken cancellationToken)
    {
        using var client = new ImapClient();

        client.CheckCertificateRevocation = false;

        try
        {
            try
            {
                await client.ConnectAsync(
                    _options.Host,
                    _options.Port,
                    GetSecureSocketOptions(),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "IMAP connect failed to {Host}:{Port}, SSL mode {SslMode}",
                    _options.Host,
                    _options.Port,
                    GetSecureSocketOptions());

                throw;
            }

            await client.AuthenticateAsync(
                _options.Username,
                _options.Password,
                cancellationToken);

            var inbox =
                string.Equals(
                    _options.Folder,
                    "INBOX",
                    StringComparison.OrdinalIgnoreCase)
                    ? client.Inbox
                    : await client.GetFolderAsync(
                        _options.Folder,
                        cancellationToken);

            await inbox.OpenAsync(
                FolderAccess.ReadWrite,
                cancellationToken);

            var messageIds =
                await inbox.SearchAsync(
                    SearchQuery.NotSeen,
                    cancellationToken);

            if (messageIds.Count == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Found {Count} unread inbound email message(s).",
                messageIds.Count);

            foreach (var messageId in messageIds)
            {
                cancellationToken
                    .ThrowIfCancellationRequested();

                await ProcessMessageAsync(
                    inbox,
                    messageId,
                    cancellationToken);
            }
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(
                    true,
                    cancellationToken);
            }
        }
    }

    private async Task ProcessMessageAsync(
        IMailFolder inbox,
        UniqueId messageId,
        CancellationToken cancellationToken)
    {
        MimeMessage message;

        try
        {
            message =
                await inbox.GetMessageAsync(
                    messageId,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not read IMAP message {MessageId}.",
                messageId);

            return;
        }

        var inboundMessage =
            MapMessage(
                message,
                messageId);

        if (inboundMessage is null)
        {
            _logger.LogWarning(
                "Skipping IMAP message {MessageId} because required fields are missing.",
                messageId);

            return;
        }

        using var scope =
            _scopeFactory.CreateScope();

        var inboundService =
            scope.ServiceProvider
                .GetRequiredService<
                    IInboundServiceRequestEmailService>();

        bool processed;

        try
        {
            processed =
                await inboundService.ProcessAsync(
                    inboundMessage,
                    cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed processing inbound message {ExternalMessageId}.",
                inboundMessage.ExternalMessageId);

            return;
        }

        if (!processed)
        {
            _logger.LogWarning(
                "Inbound message {ExternalMessageId} could not be matched to a service request.",
                inboundMessage.ExternalMessageId);

            return;
        }

        await inbox.AddFlagsAsync(
            messageId,
            MessageFlags.Seen,
            true,
            cancellationToken);

        _logger.LogInformation(
            "Inbound message {ExternalMessageId} processed successfully.",
            inboundMessage.ExternalMessageId);
    }

    private InboundEmailMessage? MapMessage(
        MimeMessage message,
        UniqueId uid)
    {
        var externalMessageId =
            !string.IsNullOrWhiteSpace(
                message.MessageId)
                ? message.MessageId
                : $"imap:{uid.Id}";

        if (string.IsNullOrWhiteSpace(
            externalMessageId))
        {
            externalMessageId =
                Guid.NewGuid().ToString("N");
        }

        var fromAddress =
            message.From
                .Mailboxes
                .FirstOrDefault()
                ?.Address;

        if (string.IsNullOrWhiteSpace(
            fromAddress))
        {
            return null;
        }

        var toAddresses =
            message.To
                .Mailboxes
                .Select(x => x.Address)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x))
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (toAddresses.Count == 0)
        {
            return null;
        }

        var body =
            GetMessageBody(message);

        var inReplyToMessageId =
            !string.IsNullOrWhiteSpace(
                message.InReplyTo)
                ? message.InReplyTo
                : null;

        var references =
            message.References.Count > 0
                ? string.Join(
                    " ",
                    message.References)
                : null;

        return new InboundEmailMessage
        {
            ExternalMessageId =
                externalMessageId,

            FromAddress =
                fromAddress,

            ToAddresses =
                toAddresses,

            Subject =
                message.Subject ?? string.Empty,

            Body =
                body,

            InReplyToMessageId =
                inReplyToMessageId,

            References =
                references,

            ReceivedAt =
                message.Date != DateTimeOffset.MinValue
                    ? message.Date
                    : DateTimeOffset.UtcNow
        };
    }

    private static string GetMessageBody(
        MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(
            message.TextBody))
        {
            return message.TextBody;
        }

        if (!string.IsNullOrWhiteSpace(
            message.HtmlBody))
        {
            return message.HtmlBody;
        }

        return string.Empty;
    }

    private SecureSocketOptions
        GetSecureSocketOptions()
    {
        if (_options.UseSsl)
        {
            return SecureSocketOptions.SslOnConnect;
        }

        return SecureSocketOptions.StartTlsWhenAvailable;
    }
}

