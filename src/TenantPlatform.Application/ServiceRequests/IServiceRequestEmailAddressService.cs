namespace TenantPlatform.Application.ServiceRequests;

public interface IServiceRequestEmailAddressService
{
    string GetReplyAddress(
        string replyToken);
}

