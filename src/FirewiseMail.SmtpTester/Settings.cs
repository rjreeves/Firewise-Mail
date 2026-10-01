namespace FirewiseMail.SmtpTester;

public sealed class Settings
{
    public SmtpSettings Smtp { get; set; } = new();
    public TestMailSettings Mail { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 465;
    public bool UseImplicitTls { get; set; } = true;
    public string SocketSecurity { get; set; } = "SslOnConnect";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class TestMailSettings
{
    public string SenderAddress { get; set; } = string.Empty;
    public string ReplyToAddress { get; set; } = string.Empty;
    public string TestRecipient { get; set; } = string.Empty;
    public string Subject { get; set; } = "FireWise Mail SMTP test";
    public string HtmlBody { get; set; } = "<html><body><p>Test message.</p></body></html>";
    public string AttachmentPath { get; set; } = string.Empty;
}
