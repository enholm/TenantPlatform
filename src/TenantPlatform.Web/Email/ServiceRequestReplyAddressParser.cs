namespace TenantPlatform.Web.Email;

public class ServiceRequestReplyAddressParser
    : IServiceRequestReplyAddressParser
{
    public bool TryGetReplyToken(
        string address,
        out string replyToken)
    {
        replyToken =
            string.Empty;

        if (string.IsNullOrWhiteSpace(
            address))
        {
            return false;
        }

        var atIndex =
            address.IndexOf('@');

        if (atIndex <= 0)
        {
            return false;
        }

        var localPart =
            address[..atIndex];

        const string prefix =
            "request-";

        if (!localPart.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token =
            localPart[prefix.Length..];

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        replyToken =
            token;

        return true;
    }
}

