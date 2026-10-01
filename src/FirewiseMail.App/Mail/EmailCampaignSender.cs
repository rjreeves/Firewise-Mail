using System.IO;
using FirewiseMail.App.Configuration;
using FirewiseMail.App.Database;
using FirewiseMail.App.Logging;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace FirewiseMail.App.Mail;

public sealed class EmailCampaignSender
{
    private readonly SmtpSettings _smtpSettings;
    private readonly MailSettings _mailSettings;
    private readonly FireworksDatabaseService _databaseService;
    private readonly EmailRunLogger _logger;

    public EmailCampaignSender(
        SmtpSettings smtpSettings,
        MailSettings mailSettings,
        FireworksDatabaseService databaseService,
        EmailRunLogger logger)
    {
        _smtpSettings = smtpSettings;
        _mailSettings = mailSettings;
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task SendCampaignAsync(
        string subject,
        string htmlBody,
        IReadOnlyList<string> attachmentPaths,
        IProgress<CampaignProgress> progress,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        if (!dryRun && string.IsNullOrWhiteSpace(_smtpSettings.Password))
        {
            throw new InvalidOperationException("SMTP password is not configured.");
        }

        var jobId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var attachmentNote = FormatAttachmentNote(attachmentPaths);
        var sent = 0;
        var failed = 0;

        var invalidRecipients = await _databaseService.LoadInvalidPendingRecipientsAsync(_mailSettings.PendingEmailPrefix, cancellationToken);
        foreach (var invalidRecipient in invalidRecipients)
        {
            _logger.LogError("InvalidRecipient", $"{invalidRecipient} skipped: invalid email address", jobId);
        }
        if (!dryRun)
        {
            await _databaseService.MarkBatchSkippedAsync(invalidRecipients, "invalid email address", cancellationToken);
        }

        using var smtpClient = dryRun
            ? new SmtpClient()
            : new SmtpClient(_logger.CreateSmtpProtocolLogger(jobId));
        if (!dryRun)
        {
            await smtpClient.ConnectAsync(
                _smtpSettings.Host,
                _smtpSettings.Port,
                GetSecureSocketOptions(_smtpSettings),
                cancellationToken);
            await smtpClient.AuthenticateAsync(_smtpSettings.Username, _smtpSettings.Password, cancellationToken);
        }

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var summary = await _databaseService.GetRecipientSummaryAsync(cancellationToken);
                progress.Report(new CampaignProgress(sent, failed, summary.Pending, summary.TotalDistinct, "Fetching next pending batch..."));

                if (summary.Pending <= 0)
                {
                    progress.Report(new CampaignProgress(sent, failed, 0, summary.TotalDistinct, "Campaign complete."));
                    break;
                }

                var batch = await _databaseService.LoadNextPendingBatchAsync(_mailSettings.BatchSize, _mailSettings.PendingEmailPrefix, cancellationToken);
                if (batch.Count == 0)
                {
                    progress.Report(new CampaignProgress(sent, failed, summary.Pending, summary.TotalDistinct, "No valid pending email addresses found."));
                    break;
                }

                progress.Report(new CampaignProgress(sent, failed, summary.Pending, summary.TotalDistinct, dryRun ? $"Dry run checked batch of {batch.Count}." : $"Sending BCC batch of {batch.Count}..."));

                try
                {
                    if (!dryRun)
                    {
                        var message = CreateMessage(subject, htmlBody, attachmentPaths, batch);
                        var envelopeSender = MailboxAddress.Parse(_mailSettings.ReturnAddress);
                        var envelopeRecipients = batch.Select(MailboxAddress.Parse).ToList();
                        await smtpClient.SendAsync(message, envelopeSender, envelopeRecipients, cancellationToken);

                        await _databaseService.MarkBatchMailedAsync(batch, attachmentNote, cancellationToken);

                        foreach (var recipient in batch)
                        {
                            _logger.LogSuccess(recipient, attachmentNote, jobId);
                        }
                    }

                    sent += batch.Count;
                    var afterSendSummary = dryRun
                        ? summary with { Pending = Math.Max(0, summary.Pending - sent) }
                        : await _databaseService.GetRecipientSummaryAsync(cancellationToken);
                    progress.Report(new CampaignProgress(sent, failed, afterSendSummary.Pending, afterSendSummary.TotalDistinct, dryRun ? $"Dry run checked batch of {batch.Count}." : $"Sent batch of {batch.Count}."));

                    if (dryRun)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    failed += batch.Count;
                    var reason = ex.GetBaseException().Message;
                    var batchName = string.Join(", ", batch);
                    _logger.LogError(batchName, reason, jobId);
                    await _databaseService.MarkBatchSkippedAsync(batch, reason, cancellationToken);

                    var afterFailureSummary = await _databaseService.GetRecipientSummaryAsync(cancellationToken);
                    progress.Report(new CampaignProgress(sent, failed, afterFailureSummary.Pending, afterFailureSummary.TotalDistinct, $"Skipped failed batch: {reason}"));
                }
            }
        }
        finally
        {
            if (smtpClient.IsConnected)
            {
                await smtpClient.DisconnectAsync(true, CancellationToken.None);
            }
        }
    }

    private MimeMessage CreateMessage(
        string subject,
        string htmlBody,
        IReadOnlyList<string> attachmentPaths,
        IReadOnlyList<string> recipients)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_mailSettings.SenderAddress));
        message.ReplyTo.Add(MailboxAddress.Parse(_mailSettings.ReplyToAddress));
        message.To.Add(MailboxAddress.Parse(_mailSettings.SenderAddress));
        message.Bcc.AddRange(recipients.Select(MailboxAddress.Parse));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        foreach (var attachmentPath in attachmentPaths)
        {
            bodyBuilder.Attachments.Add(attachmentPath);
        }

        message.Body = bodyBuilder.ToMessageBody();
        return message;
    }

    private static string FormatAttachmentNote(IReadOnlyList<string> attachmentPaths)
    {
        return string.Join(", ", attachmentPaths.Select(Path.GetFileName)) + ".";
    }

    private static SecureSocketOptions GetSecureSocketOptions(SmtpSettings settings)
    {
        if (Enum.TryParse<SecureSocketOptions>(settings.SocketSecurity, ignoreCase: true, out var configuredOption))
        {
            return configuredOption;
        }

        return settings.UseImplicitTls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
    }
}
