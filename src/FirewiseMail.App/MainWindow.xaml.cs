using System.IO;
using System.Windows;
using Microsoft.Win32;
using FirewiseMail.App.Configuration;
using FirewiseMail.App.Database;
using FirewiseMail.App.Logging;
using FirewiseMail.App.Mail;

namespace FirewiseMail.App;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly FireworksDatabaseService _databaseService;
    private readonly EmailRunLogger _logger;
    private readonly List<string> _attachmentPaths = [];
    private CancellationTokenSource? _sendCancellation;
    private int _pendingRecipients;

    public MainWindow()
    {
        InitializeComponent();
        ApplyLayoutProfile();

        _settings = AppSettingsLoader.Load(AppContext.BaseDirectory);
        _logger = new EmailRunLogger(_settings.Logging, AppContext.BaseDirectory);
        _databaseService = new FireworksDatabaseService(_settings.Database);

        DatabaseText.Text = _settings.Database.ConnectionString;
        SmtpText.Text = $"{_settings.Smtp.Host}:{_settings.Smtp.Port} implicit TLS, auth user {_settings.Smtp.Username}";
        BatchSizeText.Text = _settings.Mail.BatchSize.ToString();
        AddressesText.Text = $"Sender: {_settings.Mail.SenderAddress}\nReply-To: {_settings.Mail.ReplyToAddress}\nReturn: {_settings.Mail.ReturnAddress}";
        SubjectTextBox.Text = _settings.Mail.DefaultSubject;
        BodyTextBox.Text = _settings.Mail.DefaultHtmlBody;
        SuccessLogText.Text = _logger.SuccessLogPath;
        ErrorLogText.Text = _logger.ErrorLogPath;
        SmtpProtocolLogText.Text = _logger.SmtpProtocolLogPath;
        StatusText.Text = "Foundation loaded: configuration and log files are ready.";
        HeaderStatusText.Text = "Not ready";
        UpdateCompositionValidation();
    }

    private void ApplyLayoutProfile()
    {
        var layout = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith("--layout", StringComparison.OrdinalIgnoreCase));

        if (layout is not null && layout.Contains("1366", StringComparison.OrdinalIgnoreCase))
        {
            Width = 1180;
            Height = 680;
            MinWidth = 1060;
            MinHeight = 640;
            LeftColumn.Width = new GridLength(270);
            RightColumn.Width = new GridLength(300);
            WorkRow.Height = new GridLength(490);
            return;
        }

        Width = 1280;
        Height = 840;
        MinWidth = 1180;
        MinHeight = 760;
        LeftColumn.Width = new GridLength(300);
        RightColumn.Width = new GridLength(340);
        WorkRow.Height = new GridLength(620);
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDatabaseActionAsync(
            "Testing database connection...",
            async () =>
            {
                await _databaseService.TestConnectionAsync();
                DatabaseStatusText.Text = "Database connection succeeded.";
                LatestErrorText.Text = "No errors.";
            });
    }

    private async void PrepareEmailTableButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Prepare/reset the emails table by running SELECT fireworks.prepare_firewise_emails();?",
            "Confirm Prepare / Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await RunDatabaseActionAsync(
            "Preparing email table...",
            async () =>
            {
                await _databaseService.PrepareEmailTableAsync();
                DatabaseStatusText.Text = "Email table prepared successfully.";
                LatestErrorText.Text = "No errors.";
                await RefreshRecipientSummaryAsync();
            });
    }

    private async void LoadRecipientsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDatabaseActionAsync(
            "Loading recipients...",
            async () =>
            {
                await RefreshRecipientSummaryAsync();
                DatabaseStatusText.Text = "Recipients loaded successfully.";
                LatestErrorText.Text = "No errors.";
            });
    }

    private async Task RefreshRecipientSummaryAsync()
    {
        var summary = await _databaseService.GetRecipientSummaryAsync();
        _pendingRecipients = summary.Pending;
        TotalRecipientsText.Text = summary.TotalDistinct.ToString();
        PendingRecipientsText.Text = summary.Pending.ToString();
        MailedRecipientsText.Text = summary.AlreadyMailed.ToString();
        UpdateCompositionValidation();
    }

    private void SelectAttachmentsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Fire-Wise PDF attachments",
            Filter = "PDF files (*.pdf)|*.pdf",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var selectedPaths = dialog.FileNames
            .Where(path => string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .ToList();

        if (selectedPaths.Count > 3)
        {
            MessageBox.Show(
                "Select no more than three PDF attachments.",
                "Attachment Limit",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            selectedPaths = selectedPaths.Take(3).ToList();
        }

        _attachmentPaths.Clear();
        _attachmentPaths.AddRange(selectedPaths);
        RefreshAttachmentList();
        UpdateCompositionValidation();
    }

    private void ClearAttachmentsButton_Click(object sender, RoutedEventArgs e)
    {
        _attachmentPaths.Clear();
        RefreshAttachmentList();
        UpdateCompositionValidation();
    }

    private void CompositionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        UpdateCompositionValidation();
    }

    private async void SendCampaignButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsCampaignSetupValid(out var validationMessage))
        {
            MessageBox.Show(validationMessage, "Campaign Not Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var isDryRun = DryRunCheckBox.IsChecked == true;
        var attachmentNames = string.Join(", ", _attachmentPaths.Select(Path.GetFileName));
        var confirm = MessageBox.Show(
            $"""
            {(isDryRun ? "Dry run" : "Send")} campaign?

            Pending recipients: {_pendingRecipients}
            Batch size: {_settings.Mail.BatchSize}
            Attachments: {attachmentNames}
            Sender: {_settings.Mail.SenderAddress}
            Reply-To: {_settings.Mail.ReplyToAddress}
            Return: {_settings.Mail.ReturnAddress}
            """,
            "Confirm Send Campaign",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetSendingState(true);
        _sendCancellation = new CancellationTokenSource();

        var progress = new Progress<CampaignProgress>(ReportCampaignProgress);
        var senderService = new EmailCampaignSender(_settings.Smtp, _settings.Mail, _databaseService, _logger);

        try
        {
            await senderService.SendCampaignAsync(
                SubjectTextBox.Text.Trim(),
                BodyTextBox.Text,
                _attachmentPaths.ToList(),
                progress,
                isDryRun,
                _sendCancellation.Token);

            await RefreshRecipientSummaryAsync();
            SendProgressText.Text = isDryRun ? "Dry run complete. No emails sent and no database updates made." : "Campaign complete.";
            StatusText.Text = isDryRun ? "Dry run complete." : "Campaign complete.";
            HeaderStatusText.Text = isDryRun ? "Dry run complete" : "Complete";
        }
        catch (OperationCanceledException)
        {
            SendProgressText.Text = "Send stopped by operator.";
            StatusText.Text = "Send stopped.";
            HeaderStatusText.Text = "Stopped";
        }
        catch (Exception ex)
        {
            var message = ex.GetBaseException().Message;
            SendProgressText.Text = $"Send failed: {message}";
            StatusText.Text = "Send failed.";
            HeaderStatusText.Text = "Send failed";
            LatestErrorText.Text = message;
            MessageBox.Show(message, "Send Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _sendCancellation.Dispose();
            _sendCancellation = null;
            SetSendingState(false);
            await RefreshRecipientSummaryAsync();
        }
    }

    private void StopSendButton_Click(object sender, RoutedEventArgs e)
    {
        _sendCancellation?.Cancel();
        StopSendButton.IsEnabled = false;
        SendProgressText.Text = "Stopping after current operation...";
        HeaderStatusText.Text = "Stopping";
    }

    private void RefreshAttachmentList()
    {
        AttachmentsListBox.ItemsSource = null;
        AttachmentsListBox.ItemsSource = _attachmentPaths.Select(Path.GetFileName).ToList();
        AttachmentCountText.Text = $"{_attachmentPaths.Count} of 3 PDFs selected";
    }

    private void UpdateCompositionValidation()
    {
        if (ValidationText is null)
        {
            return;
        }

        var missingItems = new List<string>();

        if (_pendingRecipients <= 0)
        {
            missingItems.Add("load pending recipients");
        }

        if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
        {
            missingItems.Add("enter a subject");
        }

        if (string.IsNullOrWhiteSpace(BodyTextBox.Text))
        {
            missingItems.Add("enter an HTML body");
        }

        if (_attachmentPaths.Count == 0)
        {
            missingItems.Add("select at least one PDF");
        }

        if (_attachmentPaths.Any(path => !File.Exists(path)))
        {
            missingItems.Add("remove missing PDF files");
        }

        if (string.IsNullOrWhiteSpace(_settings.Smtp.Password) && DryRunCheckBox.IsChecked != true)
        {
            missingItems.Add("configure SMTP password");
        }

        var isValid = missingItems.Count == 0;
        if (SendCampaignButton is not null)
        {
            SendCampaignButton.IsEnabled = isValid && _sendCancellation is null;
        }

        if (isValid)
        {
            ValidationText.Text = "Campaign setup is valid.";
            ValidationText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            HeaderStatusText.Text = _sendCancellation is null ? "Ready to send" : "Sending";
            return;
        }

        ValidationText.Text = "To send later: " + string.Join(", ", missingItems) + ".";
        ValidationText.Foreground = System.Windows.Media.Brushes.DarkRed;
        HeaderStatusText.Text = _sendCancellation is null ? "Not ready" : "Sending";
    }

    private bool IsCampaignSetupValid(out string validationMessage)
    {
        var missingItems = new List<string>();

        if (_pendingRecipients <= 0)
        {
            missingItems.Add("load pending recipients");
        }

        if (string.IsNullOrWhiteSpace(SubjectTextBox.Text))
        {
            missingItems.Add("enter a subject");
        }

        if (string.IsNullOrWhiteSpace(BodyTextBox.Text))
        {
            missingItems.Add("enter an HTML body");
        }

        if (_attachmentPaths.Count == 0)
        {
            missingItems.Add("select at least one PDF");
        }

        if (_attachmentPaths.Any(path => !File.Exists(path)))
        {
            missingItems.Add("remove missing PDF files");
        }

        if (string.IsNullOrWhiteSpace(_settings.Smtp.Password) && DryRunCheckBox.IsChecked != true)
        {
            missingItems.Add("configure SMTP password in appsettings.json");
        }

        validationMessage = missingItems.Count == 0
            ? "Campaign setup is valid."
            : "Before sending, please " + string.Join(", ", missingItems) + ".";

        return missingItems.Count == 0;
    }

    private void ReportCampaignProgress(CampaignProgress progress)
    {
        var completed = Math.Max(0, progress.TotalDistinct - progress.Pending);
        SendProgressBar.Value = progress.TotalDistinct == 0
            ? 0
            : completed * 100.0 / progress.TotalDistinct;
        SendPercentText.Text = $"{SendProgressBar.Value:0}%";
        SendProgressText.Text = $"{progress.Message} Sent: {progress.Sent}; failed: {progress.Failed}; pending: {progress.Pending}.";
        StatusText.Text = progress.Message;
        HeaderStatusText.Text = "Sending";
    }

    private void SetSendingState(bool isSending)
    {
        SetDatabaseButtonsEnabled(!isSending);
        SelectAttachmentsButton.IsEnabled = !isSending;
        ClearAttachmentsButton.IsEnabled = !isSending;
        DryRunCheckBox.IsEnabled = !isSending;
        SubjectTextBox.IsEnabled = !isSending;
        BodyTextBox.IsEnabled = !isSending;
        SendCampaignButton.IsEnabled = !isSending;
        StopSendButton.IsEnabled = isSending;

        if (!isSending)
        {
            UpdateCompositionValidation();
        }
    }

    private async Task RunDatabaseActionAsync(string statusMessage, Func<Task> action)
    {
        SetDatabaseButtonsEnabled(false);
        StatusText.Text = statusMessage;
        DatabaseStatusText.Text = statusMessage;

        try
        {
            await action();
            StatusText.Text = "Database action completed.";
        }
        catch (Exception ex)
        {
            var message = ex.GetBaseException().Message;
            DatabaseStatusText.Text = $"Database error: {message}";
            StatusText.Text = "Database action failed.";
            HeaderStatusText.Text = "Database error";
            LatestErrorText.Text = message;
            _logger.LogError("Database", message, "DatabaseIntegration");
        }
        finally
        {
            SetDatabaseButtonsEnabled(true);
        }
    }

    private void SetDatabaseButtonsEnabled(bool isEnabled)
    {
        TestConnectionButton.IsEnabled = isEnabled;
        PrepareEmailTableButton.IsEnabled = isEnabled;
        LoadRecipientsButton.IsEnabled = isEnabled;
    }
}
