using FirewiseMail.SmtpTester;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

var settings = SettingsLoader.Load(AppContext.BaseDirectory);

var recipient = args.Length > 0 ? args[0] : settings.Mail.TestRecipient;

if (string.IsNullOrWhiteSpace(recipient))
{
    Console.Error.WriteLine("No recipient given. Pass one as an argument or set Mail.TestRecipient in appsettings.json.");
    return 1;
}

if (string.IsNullOrWhiteSpace(settings.Smtp.Password))
{
    Console.Error.WriteLine("SMTP password is not configured. Set it in appsettings.json or the FIREWISE_SMTP_PASSWORD environment variable.");
    return 1;
}

var message = new MimeMessage();
message.From.Add(MailboxAddress.Parse(settings.Mail.SenderAddress));
message.ReplyTo.Add(MailboxAddress.Parse(settings.Mail.ReplyToAddress));
message.To.Add(MailboxAddress.Parse(recipient));
message.Subject = settings.Mail.Subject;

var bodyBuilder = new BodyBuilder { HtmlBody = settings.Mail.HtmlBody };

var attachmentPath = string.IsNullOrWhiteSpace(settings.Mail.AttachmentPath)
    ? Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.pdb")
    : settings.Mail.AttachmentPath;

if (File.Exists(attachmentPath))
{
   // bodyBuilder.Attachments.Add(attachmentPath);
}
else if (!string.IsNullOrWhiteSpace(settings.Mail.AttachmentPath))
{
    Console.Error.WriteLine($"Attachment not found, sending without it: {attachmentPath}");
}

message.Body = bodyBuilder.ToMessageBody();

Console.WriteLine($"Connecting to {settings.Smtp.Host}:{settings.Smtp.Port}...");

try
{
    using var smtpClient = new SmtpClient();
    var secureSocketOptions = Enum.TryParse<SecureSocketOptions>(settings.Smtp.SocketSecurity, ignoreCase: true, out var configured)
        ? configured
        : settings.Smtp.UseImplicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;

    await smtpClient.ConnectAsync(settings.Smtp.Host, settings.Smtp.Port, secureSocketOptions);
    await smtpClient.AuthenticateAsync(settings.Smtp.Username, settings.Smtp.Password);
    await smtpClient.SendAsync(message);
    await smtpClient.DisconnectAsync(true);

    Console.WriteLine($"Sent test email to {recipient}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to send test email: {ex.GetBaseException().Message}");
    return 1;
}
