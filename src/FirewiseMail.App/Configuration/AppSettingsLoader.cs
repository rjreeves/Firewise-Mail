using System.IO;
using System.Data.Common;
using System.Text;
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
        ApplyDatabasePassword(settings);

        var smtpPassword = Environment.GetEnvironmentVariable("FIREWISE_SMTP_PASSWORD");
        if (!string.IsNullOrWhiteSpace(smtpPassword))
        {
            settings.Smtp.Password = smtpPassword;
        }
    }

    private static void ApplyDatabasePassword(AppSettings settings)
    {
        var configuredPassword = Environment.GetEnvironmentVariable("FIREWISE_DB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(configuredPassword))
        {
            settings.Database.ConnectionString = SetConnectionStringValue(settings.Database.ConnectionString, "Password", configuredPassword);
            return;
        }

        if (HasConnectionStringValue(settings.Database.ConnectionString, "Password", "Pwd"))
        {
            return;
        }

        var password = FindPgPassPassword(settings.Database.ConnectionString);
        if (!string.IsNullOrEmpty(password))
        {
            settings.Database.ConnectionString = SetConnectionStringValue(settings.Database.ConnectionString, "Password", password);
        }
    }

    private static string? FindPgPassPassword(string connectionString)
    {
        var builder = CreateConnectionStringBuilder(connectionString);
        var host = GetConnectionStringValue(builder, "Host", "Server") ?? "localhost";
        var port = GetConnectionStringValue(builder, "Port") ?? "5432";
        var database = GetConnectionStringValue(builder, "Database", "Initial Catalog") ?? "*";
        var username = GetConnectionStringValue(builder, "User Id", "UserID", "Username", "User") ?? "*";
        var pgPassPath = GetPgPassPath();

        if (string.IsNullOrWhiteSpace(pgPassPath) || !File.Exists(pgPassPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(pgPassPath))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = SplitPgPassLine(line);
            if (parts.Count != 5)
            {
                continue;
            }

            if (PgPassMatches(parts[0], host)
                && PgPassMatches(parts[1], port)
                && PgPassMatches(parts[2], database)
                && PgPassMatches(parts[3], username))
            {
                return parts[4];
            }
        }

        return null;
    }

    private static string? GetPgPassPath()
    {
        var configuredPath = Environment.GetEnvironmentVariable("PGPASSFILE");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return string.IsNullOrWhiteSpace(appData)
            ? null
            : Path.Combine(appData, "postgresql", "pgpass.conf");
    }

    private static List<string> SplitPgPassLine(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var isEscaped = false;

        foreach (var character in line)
        {
            if (isEscaped)
            {
                current.Append(character);
                isEscaped = false;
                continue;
            }

            if (character == '\\')
            {
                isEscaped = true;
                continue;
            }

            if (character == ':' && parts.Count < 4)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (isEscaped)
        {
            current.Append('\\');
        }

        parts.Add(current.ToString());
        return parts;
    }

    private static bool PgPassMatches(string pattern, string value)
    {
        return pattern == "*" || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasConnectionStringValue(string connectionString, params string[] keys)
    {
        var builder = CreateConnectionStringBuilder(connectionString);
        return keys.Any(key => !string.IsNullOrWhiteSpace(GetConnectionStringValue(builder, key)));
    }

    private static string SetConnectionStringValue(string connectionString, string key, string value)
    {
        var builder = CreateConnectionStringBuilder(connectionString);
        builder[key] = value;
        return builder.ConnectionString;
    }

    private static string? GetConnectionStringValue(DbConnectionStringBuilder builder, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value is not null)
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static DbConnectionStringBuilder CreateConnectionStringBuilder(string connectionString)
    {
        return new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };
    }
}
