using Microsoft.Extensions.Options;

namespace TenantPlatform.Web.Email;

public class ServiceRequestEmailAddressService
    : IServiceRequestEmailAddressService
{
    private readonly SmtpOptions _options;

    public ServiceRequestEmailAddressService(
        IOptions<SmtpOptions> options)
    {
        _options = options.Value;
    }

    public string GetReplyAddress(
        string replyToken)
    {
        return $"request-{replyToken}@{_options.ReplyDomain}";
    }
}

