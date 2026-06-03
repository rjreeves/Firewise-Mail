using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FirewiseMail.Tests.Integration;

public sealed class LocalSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;

    public int Port { get; private set; }

    public List<string> Messages { get; } = [];

    public void Start()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serverTask = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client), _cancellation.Token);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var clientScope = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        await WriteLineAsync(stream, "220 localhost test smtp");

        while (!_cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (line is null)
            {
                break;
            }

            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("EHLO") || upper.StartsWith("HELO"))
            {
                await WriteLineAsync(stream, "250-localhost");
                await WriteLineAsync(stream, "250-AUTH PLAIN LOGIN");
                await WriteLineAsync(stream, "250 OK");
            }
            else if (upper.StartsWith("AUTH"))
            {
                await WriteLineAsync(stream, "235 Authentication successful");
            }
            else if (upper.StartsWith("MAIL FROM") || upper.StartsWith("RCPT TO"))
            {
                await WriteLineAsync(stream, "250 OK");
            }
            else if (upper == "DATA")
            {
                await WriteLineAsync(stream, "354 End data with <CR><LF>.<CR><LF>");
                var builder = new StringBuilder();
                while (true)
                {
                    var dataLine = await reader.ReadLineAsync();
                    if (dataLine is null || dataLine == ".")
                    {
                        break;
                    }

                    builder.AppendLine(dataLine);
                }

                Messages.Add(builder.ToString());
                await WriteLineAsync(stream, "250 Message accepted");
            }
            else if (upper == "QUIT")
            {
                await WriteLineAsync(stream, "221 Bye");
                break;
            }
            else
            {
                await WriteLineAsync(stream, "250 OK");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();

        if (_serverTask is not null)
        {
            try
            {
                await _serverTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cancellation.Dispose();
    }

    private static async Task WriteLineAsync(Stream stream, string line)
    {
        var bytes = Encoding.ASCII.GetBytes(line + "\r\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
