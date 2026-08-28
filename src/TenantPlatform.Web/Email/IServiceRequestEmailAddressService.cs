namespace TenantPlatform.Web.Email;

public interface IServiceRequestEmailAddressService
{
    string GetReplyAddress(
        string replyToken);
}

