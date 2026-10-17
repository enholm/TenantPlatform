namespace TenantPlatform.Application.ServiceRequests;

public interface IServiceRequestReplyAddressParser
{
    bool TryGetReplyToken(
        string address,
        out string replyToken);
}

