namespace TenantPlatform.Web.Email;

public interface IServiceRequestReplyAddressParser
{
    bool TryGetReplyToken(
        string address,
        out string replyToken);
}

