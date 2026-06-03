namespace FirewiseMail.App.Configuration;

public sealed class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public SmtpSettings Smtp { get; set; } = new();
    public MailSettings Mail { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
}

public sealed class DatabaseSettings
{
    public string ConnectionString { get; set; } = "User Id=fireworks;Host=127.0.0.1;Database=fireworks;Initial Schema=fireworks";
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "mail.fire-wise.com.au";
    public int Port { get; set; } = 465;
    public bool UseImplicitTls { get; set; } = true;
    public string SocketSecurity { get; set; } = "SslOnConnect";
    public string Username { get; set; } = "invoicing@fire-wise.com.au";
    public string Password { get; set; } = string.Empty;
}

public sealed class MailSettings
{
    public string SenderAddress { get; set; } = "Gordon@fire-wise.com.au";
    public string ReplyToAddress { get; set; } = "Gordon@fire-wise.com.au";
    public string ReturnAddress { get; set; } = "Gordon@fire-wise.com.au";
    public string DefaultSubject { get; set; } = "The current edition of Fire Wise";
    public string DefaultHtmlBody { get; set; } = DefaultBody;
    public int BatchSize { get; set; } = 100;
    public string PendingEmailPrefix { get; set; } = string.Empty;

    private const string DefaultBody = """
        <html><br>
        <p>
        Please find electronic version of Fire Wise attached.<br><br>
        Gordon Rippon-King<br>
        Managing Editor<br>
        'Fire Wise'<br>
        <b>
        PO Box 496, Cowes, 3922<br>
        M: 0402 051 412<br>
        www.fire-wise.com.au
        </b>
        </p>
        </html>
        """;
}

public sealed class LoggingSettings
{
    public string Directory { get; set; } = "logs";
    public string SuccessFileName { get; set; } = "successful-sends.log";
    public string ErrorFileName { get; set; } = "errored-sends.log";
    public string SmtpProtocolFileName { get; set; } = "smtp-protocol.log";
}
