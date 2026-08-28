using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Services;
using TenantPlatform.Infrastructure.Persistence;

namespace TenantPlatform.Web.Email;

public class InboundServiceRequestEmailService
    : IInboundServiceRequestEmailService
{
    private readonly IDbContextFactory<TenantPlatformDbContext>
        _dbContextFactory;

    private readonly IServiceRequestReplyAddressParser
        _addressParser;

    public InboundServiceRequestEmailService(
        IDbContextFactory<TenantPlatformDbContext> dbContextFactory,
        IServiceRequestReplyAddressParser addressParser)
    {
        _dbContextFactory =
            dbContextFactory;

        _addressParser =
            addressParser;
    }

    public async Task<bool> ProcessAsync(
        InboundEmailMessage message,
        CancellationToken cancellationToken = default)
    {
        var replyToken =
            message.ToAddresses
                .Select(address =>
                {
                    var success =
                        _addressParser.TryGetReplyToken(
                            address,
                            out var token);

                    return success
                        ? token
                        : null;
                })
                .FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(x));

        if (string.IsNullOrWhiteSpace(
            replyToken))
        {
            return false;
        }

        await using var dbContext =
            await _dbContextFactory
                .CreateDbContextAsync(
                    cancellationToken);

        var alreadyProcessed =
            await dbContext.ServiceRequestMessages
                .AsNoTracking()
                .AnyAsync(
                    x =>
                        x.ExternalMessageId ==
                        message.ExternalMessageId,
                    cancellationToken);

        if (alreadyProcessed)
        {
            return true;
        }

        var request =
            await dbContext.ServiceRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x =>
                        x.ReplyToken ==
                        replyToken,
                    cancellationToken);

        if (request is null)
        {
            return false;
        }

        dbContext.ServiceRequestMessages.Add(
            new ServiceRequestMessage
            {
                Id =
                    Guid.NewGuid(),

                ServiceRequestId =
                    request.Id,

                Direction =
                    ServiceRequestMessageDirection.Inbound,

                Type =
                    ServiceRequestMessageType.Email,

                ExternalMessageId =
                    message.ExternalMessageId,

                FromAddress =
                    message.FromAddress,

                ToAddress =
                    message.ToAddresses
                        .FirstOrDefault(),

                Subject =
                    message.Subject,

                Body =
                    message.Body,

                CreatedAt =
                    message.ReceivedAt.ToUniversalTime()
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }
}

