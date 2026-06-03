using Devart.Data.PostgreSql;
using FirewiseMail.App.Configuration;

namespace FirewiseMail.Tests.Integration;

public sealed class FireworksDatabaseFixture
{
    public DatabaseSettings DatabaseSettings { get; } = new()
    {
        ConnectionString = "User Id=fireworks;Host=127.0.0.1;Database=fireworks;Initial Schema=fireworks"
    };

    public async Task InsertEmailAsync(string emailAddress, bool isMailed)
    {
        await using var connection = new PgSqlConnection(DatabaseSettings.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO fireworks.emails (subs_email, is_mailed)
            VALUES (:email_addr, :is_mailed);
            """;
        command.Parameters.Add(new PgSqlParameter("email_addr", emailAddress));
        command.Parameters.Add(new PgSqlParameter("is_mailed", isMailed));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsMailedAsync(string emailAddress)
    {
        await using var connection = new PgSqlConnection(DatabaseSettings.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bool_or(coalesce(is_mailed, false))
            FROM fireworks.emails
            WHERE lower(trim(subs_email)) = :email_addr;
            """;
        command.Parameters.Add(new PgSqlParameter("email_addr", emailAddress.Trim().ToLowerInvariant()));
        var result = await command.ExecuteScalarAsync();
        return result is bool value && value;
    }

    public async Task<bool> HasHistoryAsync(string emailAddress, string note)
    {
        await using var connection = new PgSqlConnection(DatabaseSettings.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM fireworks.email_history
                WHERE lower(trim(email_addr)) = :email_addr
                  AND etype = 'Firewise'
                  AND email_note = :email_note
            );
            """;
        command.Parameters.Add(new PgSqlParameter("email_addr", emailAddress.Trim().ToLowerInvariant()));
        command.Parameters.Add(new PgSqlParameter("email_note", note));
        var result = await command.ExecuteScalarAsync();
        return result is bool value && value;
    }

    public async Task CleanupEmailAsync(string emailAddress)
    {
        await using var connection = new PgSqlConnection(DatabaseSettings.ConnectionString);
        await connection.OpenAsync();

        await using var historyCommand = connection.CreateCommand();
        historyCommand.CommandText = """
            DELETE FROM fireworks.email_history
            WHERE lower(trim(email_addr)) = :email_addr;
            """;
        historyCommand.Parameters.Add(new PgSqlParameter("email_addr", emailAddress.Trim().ToLowerInvariant()));
        await historyCommand.ExecuteNonQueryAsync();

        await using var emailCommand = connection.CreateCommand();
        emailCommand.CommandText = """
            DELETE FROM fireworks.emails
            WHERE lower(trim(subs_email)) = :email_addr;
            """;
        emailCommand.Parameters.Add(new PgSqlParameter("email_addr", emailAddress.Trim().ToLowerInvariant()));
        await emailCommand.ExecuteNonQueryAsync();
    }
}

