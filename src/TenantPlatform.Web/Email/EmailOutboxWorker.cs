using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Email;

public class EmailOutboxWorker
    : BackgroundService
{
    private readonly IServiceScopeFactory
        _scopeFactory;

    private readonly ILogger<EmailOutboxWorker>
        _logger;

    public EmailOutboxWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<EmailOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextMessageAsync(
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error processing email outbox.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(5),
                stoppingToken);
        }
    }

    private async Task ProcessNextMessageAsync(
        CancellationToken cancellationToken)
    {
        using var scope =
            _scopeFactory.CreateScope();

        var dbContextFactory =
            scope.ServiceProvider
                .GetRequiredService<
                    IDbContextFactory<TenantPlatformDbContext>>();

        var emailSender =
            scope.ServiceProvider
                .GetRequiredService<IEmailSender>();

        await using var dbContext =
            await dbContextFactory
                .CreateDbContextAsync(
                    cancellationToken);

        var now =
            DateTimeOffset.UtcNow;

        var message =
            await dbContext.EmailOutboxMessages
                .Where(x =>
                    x.Status ==
                        EmailOutboxStatus.Pending &&
                    (!x.NextAttemptAt.HasValue ||
                     x.NextAttemptAt <= now))
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(
                    cancellationToken);

        if (message is null)
        {
            return;
        }

        message.AttemptCount++;
        message.LastAttemptAt = now;

        try
        {
            await emailSender.SendAsync(
                message.ToAddress,
                message.ReplyToAddress,
                message.Subject,
                message.Body,
                cancellationToken);

            message.Status =
                EmailOutboxStatus.Sent;

            message.SentAt =
                DateTimeOffset.UtcNow;

            message.LastError =
                null;

            dbContext.ServiceRequestMessages.Add(
                new ServiceRequestMessage
                {
                    Id =
                        Guid.NewGuid(),

                    ServiceRequestId =
                        message.ServiceRequestId,

                    Direction =
                        ServiceRequestMessageDirection.Outbound,

                    Type =
                        ServiceRequestMessageType.Email,

                    ToAddress =
                        message.ToAddress,

                    Subject =
                        message.Subject,

                    Body =
                        message.Body,

                    CreatedAt =
                        DateTimeOffset.UtcNow
                });
        }
        catch (Exception ex)
        {
            var error = ex.ToString();
            message.LastError =
                ex.Message.Length > 4000
                    ? ex.Message[..4000]
                    : ex.Message;

            if (message.AttemptCount >= 5)
            {
                message.Status =
                    EmailOutboxStatus.Failed;
            }
            else
            {
                message.NextAttemptAt =
                    DateTimeOffset.UtcNow
                        .AddMinutes(
                            Math.Pow(
                                2,
                                message.AttemptCount));
            }

            _logger.LogWarning(
                ex,
                "Failed sending email outbox message {MessageId}. Attempt {AttemptCount}.",
                message.Id,
                message.AttemptCount);
        }

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }
}

