using System.Data.Common;
using FirewiseMail.App.Configuration;

namespace FirewiseMail.Tests;

public sealed class AppSettingsLoaderTests
{
    [Fact]
    public void Load_uses_pgpass_password_when_connection_string_has_no_password()
    {
        var previousPgPassFile = Environment.GetEnvironmentVariable("PGPASSFILE");
        var previousDatabasePassword = Environment.GetEnvironmentVariable("FIREWISE_DB_PASSWORD");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "FirewiseMailTests", Guid.NewGuid().ToString("N"));
        var pgPassPath = Path.Combine(tempDirectory, "pgpass.conf");

        Directory.CreateDirectory(tempDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Database": {
                    "ConnectionString": "User Id=fireworks;Host=127.0.0.1;Database=fireworks;Initial Schema=fireworks"
                  }
                }
                """);
            File.WriteAllText(pgPassPath, "127.0.0.1:5432:fireworks:fireworks:pgpass-secret" + Environment.NewLine);
            Environment.SetEnvironmentVariable("PGPASSFILE", pgPassPath);
            Environment.SetEnvironmentVariable("FIREWISE_DB_PASSWORD", null);

            var settings = AppSettingsLoader.Load(tempDirectory);

            Assert.Equal("pgpass-secret", GetConnectionStringValue(settings.Database.ConnectionString, "Password"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PGPASSFILE", previousPgPassFile);
            Environment.SetEnvironmentVariable("FIREWISE_DB_PASSWORD", previousDatabasePassword);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_prefers_environment_database_password_over_pgpass()
    {
        var previousPgPassFile = Environment.GetEnvironmentVariable("PGPASSFILE");
        var previousDatabasePassword = Environment.GetEnvironmentVariable("FIREWISE_DB_PASSWORD");
        var tempDirectory = Path.Combine(Path.GetTempPath(), "FirewiseMailTests", Guid.NewGuid().ToString("N"));
        var pgPassPath = Path.Combine(tempDirectory, "pgpass.conf");

        Directory.CreateDirectory(tempDirectory);
        try
        {
            File.WriteAllText(
                Path.Combine(tempDirectory, "appsettings.json"),
                """
                {
                  "Database": {
                    "ConnectionString": "User Id=fireworks;Host=127.0.0.1;Database=fireworks;Initial Schema=fireworks"
                  }
                }
                """);
            File.WriteAllText(pgPassPath, "127.0.0.1:5432:fireworks:fireworks:pgpass-secret" + Environment.NewLine);
            Environment.SetEnvironmentVariable("PGPASSFILE", pgPassPath);
            Environment.SetEnvironmentVariable("FIREWISE_DB_PASSWORD", "env-secret");

            var settings = AppSettingsLoader.Load(tempDirectory);

            Assert.Equal("env-secret", GetConnectionStringValue(settings.Database.ConnectionString, "Password"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PGPASSFILE", previousPgPassFile);
            Environment.SetEnvironmentVariable("FIREWISE_DB_PASSWORD", previousDatabasePassword);
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string? GetConnectionStringValue(string connectionString, string key)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        return builder.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
