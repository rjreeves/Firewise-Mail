using System.IO;
using System.Text.Json;

namespace FirewiseMail.App.Configuration;

public static class AppSettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static AppSettings Load(string baseDirectory)
    {
        var settingsPath = Path.Combine(baseDirectory, "appsettings.json");

        if (!File.Exists(settingsPath))
        {
            var defaults = new AppSettings();
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(defaults, JsonOptions));
            ApplyEnvironmentOverrides(defaults);
            return defaults;
        }

        using var stream = File.OpenRead(settingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        ApplyEnvironmentOverrides(settings);
        return settings;
    }

    private static void ApplyEnvironmentOverrides(AppSettings settings)
    {
        var smtpPassword = Environment.GetEnvironmentVariable("FIREWISE_SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(smtpPassword))
        {
            settings.Smtp.Password = smtpPassword;
        }
    }
}
