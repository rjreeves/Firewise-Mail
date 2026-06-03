using FirewiseMail.App.Configuration;
using FirewiseMail.App.Database;
using FirewiseMail.App.Logging;
using FirewiseMail.App.Mail;

namespace FirewiseMail.Tests.Integration;

public sealed class SmtpIntegrationTests
{
    private readonly FireworksDatabaseFixture _database = new();

    [Fact]
    public async Task SendCampaignAsync_sends_to_local_smtp_and_updates_database()
    {
        var emailPrefix = $"codex-fwmail-smtp-{Guid.NewGuid():N}";
        var email = $"{emailPrefix}@example.invalid";
        var invalidEmail = $"{emailPrefix}@reeves";
        var tempDirectory = Path.Combine(Path.GetTempPath(), "FirewiseMailTests", Guid.NewGuid().ToString("N"));
        var attachmentPath = Path.Combine(tempDirectory, "firewise-test.pdf");

        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(attachmentPath, "%PDF-1.4 test attachment");

        await using var smtpServer = new LocalSmtpServer();
        smtpServer.Start();

        var service = new FireworksDatabaseService(_database.DatabaseSettings);
        var logger = new EmailRunLogger(new LoggingSettings { Directory = tempDirectory }, tempDirectory);
        var sender = new EmailCampaignSender(
            new SmtpSettings
            {
                Host = "127.0.0.1",
                Port = smtpServer.Port,
                SocketSecurity = "None",
                UseImplicitTls = false,
                Username = "test",
                Password = "test"
            },
            new MailSettings
            {
                SenderAddress = "Gordon@fire-wise.com.au",
                ReplyToAddress = "Gordon@fire-wise.com.au",
                ReturnAddress = "Gordon@fire-wise.com.au",
                BatchSize = 1,
                PendingEmailPrefix = emailPrefix
            },
            service,
            logger);

        await _database.CleanupEmailAsync(email);
        await _database.CleanupEmailAsync(invalidEmail);
        try
        {
            await _database.InsertEmailAsync(email, isMailed: false);
            await _database.InsertEmailAsync(invalidEmail, isMailed: false);
            var progress = new Progress<CampaignProgress>();

            await sender.SendCampaignAsync(
                "Integration test subject",
                "<html><p>Test body</p></html>",
                [attachmentPath],
                progress);

            Assert.True(await _database.IsMailedAsync(email));
            Assert.True(await _database.HasHistoryAsync(email, "firewise-test.pdf."));
            var message = Assert.Single(smtpServer.Messages);
            Assert.Contains("Integration test subject", message);

            var smtpLog = await File.ReadAllTextAsync(logger.SmtpProtocolLogPath);
            Assert.Contains("[redacted message body and attachments]", smtpLog);
            Assert.DoesNotContain("%PDF-1.4 test attachment", smtpLog);

            var errorLog = await File.ReadAllTextAsync(logger.ErrorLogPath);
            Assert.Contains("InvalidRecipient", errorLog);
            Assert.Contains($"{invalidEmail} skipped: invalid email address", errorLog);
            Assert.False(await _database.IsMailedAsync(invalidEmail));
        }
        finally
        {
            await _database.CleanupEmailAsync(email);
            await _database.CleanupEmailAsync(invalidEmail);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
