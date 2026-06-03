using System.IO;
using System.Text;
using FirewiseMail.App.Configuration;
using MailKit;

namespace FirewiseMail.App.Logging;

public sealed class EmailRunLogger
{
    private readonly string _successLogPath;
    private readonly string _errorLogPath;
    private readonly string _smtpProtocolLogPath;

    public EmailRunLogger(LoggingSettings settings, string baseDirectory)
    {
        var logDirectory = Path.IsPathRooted(settings.Directory)
            ? settings.Directory
            : Path.Combine(baseDirectory, settings.Directory);

        Directory.CreateDirectory(logDirectory);

        _successLogPath = Path.Combine(logDirectory, settings.SuccessFileName);
        _errorLogPath = Path.Combine(logDirectory, settings.ErrorFileName);
        _smtpProtocolLogPath = Path.Combine(logDirectory, settings.SmtpProtocolFileName);

        EnsureFile(_successLogPath);
        EnsureFile(_errorLogPath);
        EnsureFile(_smtpProtocolLogPath, "Timestamp\tJobId\tDirection\tLine");
    }

    public string SuccessLogPath => _successLogPath;

    public string ErrorLogPath => _errorLogPath;

    public string SmtpProtocolLogPath => _smtpProtocolLogPath;

    public void LogSuccess(string recipient, string attachmentNames, string jobId)
    {
        Append(_successLogPath, $"{DateTimeOffset.Now:O}\t{jobId}\t{recipient}\t{attachmentNames}");
    }

    public void LogError(string recipientOrBatch, string reason, string jobId)
    {
        Append(_errorLogPath, $"{DateTimeOffset.Now:O}\t{jobId}\t{recipientOrBatch}\t{reason}");
    }

    public IProtocolLogger CreateSmtpProtocolLogger(string jobId)
    {
        return new SanitizedSmtpProtocolLogger(_smtpProtocolLogPath, jobId);
    }

    private static void EnsureFile(string path, string header = "Timestamp\tJobId\tRecipientOrBatch\tDetails")
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, header + Environment.NewLine);
        }
    }

    private static void Append(string path, string line)
    {
        File.AppendAllText(path, line + Environment.NewLine);
    }

    private sealed class SanitizedSmtpProtocolLogger : IProtocolLogger
    {
        private readonly string _path;
        private readonly string _jobId;
        private bool _suppressNextClientAuthLine;
        private bool _insideData;
        private bool _insideDataHeaders;
        private bool _loggedDataRedaction;

        public SanitizedSmtpProtocolLogger(string path, string jobId)
        {
            _path = path;
            _jobId = jobId;
        }

        public IAuthenticationSecretDetector? AuthenticationSecretDetector { get; set; }

        public void LogConnect(Uri uri)
        {
            AppendProtocolLine("CONNECT", uri.ToString());
        }

        public void LogClient(byte[] buffer, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(buffer, offset, count);
            foreach (var line in SplitProtocolLines(text))
            {
                LogClientLine(line);
            }
        }

        public void LogServer(byte[] buffer, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(buffer, offset, count);
            foreach (var line in SplitProtocolLines(text))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                AppendProtocolLine("S", line);
            }
        }

        public void Dispose()
        {
        }

        private void LogClientLine(string line)
        {
            if (!_insideData && line.Length == 0)
            {
                return;
            }

            if (_suppressNextClientAuthLine)
            {
                _suppressNextClientAuthLine = false;
                AppendProtocolLine("C", "[redacted authentication response]");
                return;
            }

            if (line.StartsWith("AUTH ", StringComparison.OrdinalIgnoreCase))
            {
                _suppressNextClientAuthLine = true;
                AppendProtocolLine("C", "AUTH [redacted]");
                return;
            }

            if (_insideData)
            {
                LogDataLine(line);
                return;
            }

            if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
            {
                _insideData = true;
                _insideDataHeaders = true;
                _loggedDataRedaction = false;
                AppendProtocolLine("C", line);
                return;
            }

            AppendProtocolLine("C", line);
        }

        private void LogDataLine(string line)
        {
            if (line == ".")
            {
                AppendProtocolLine("C", line);
                _insideData = false;
                _insideDataHeaders = false;
                return;
            }

            if (_insideDataHeaders)
            {
                if (line.Length == 0)
                {
                    _insideDataHeaders = false;
                    AppendDataRedaction();
                    return;
                }

                AppendProtocolLine("C", line);
                return;
            }

            AppendDataRedaction();
        }

        private void AppendDataRedaction()
        {
            if (_loggedDataRedaction)
            {
                return;
            }

            _loggedDataRedaction = true;
            AppendProtocolLine("C", "[redacted message body and attachments]");
        }

        private void AppendProtocolLine(string direction, string line)
        {
            Append(_path, $"{DateTimeOffset.Now:O}\t{_jobId}\t{direction}\t{line}");
        }

        private static IEnumerable<string> SplitProtocolLines(string text)
        {
            return text
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => line.TrimEnd('\r'));
        }
    }
}
