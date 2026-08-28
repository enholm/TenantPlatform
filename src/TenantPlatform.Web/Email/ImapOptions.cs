namespace TenantPlatform.Web.Email;

public class ImapOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 993;

    public bool UseSsl { get; set; } = true;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
    public string Folder { get; set; } = "INBOX";
}

