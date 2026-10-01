using System.Text.Json;

namespace FirewiseMail.SmtpTester;

public static class SettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Settings Load(string baseDirectory)
    {
        var settingsPath = Path.Combine(baseDirectory, "appsettings.json");

        if (!File.Exists(settingsPath))
        {
            throw new FileNotFoundException(
                $"Missing {settingsPath}. Copy appsettings.example.json to appsettings.json and fill in real values.");
        }

        using var stream = File.OpenRead(settingsPath);
        var settings = JsonSerializer.Deserialize<Settings>(stream, JsonOptions) ?? new Settings();

        var smtpPassword = Environment.GetEnvironmentVariable("FIREWISE_SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(smtpPassword))
        {
            settings.Smtp.Password = smtpPassword;
        }

        return settings;
    }
}
