using System.Text.RegularExpressions;
using Devart.Data.PostgreSql;
using FirewiseMail.App.Configuration;

namespace FirewiseMail.App.Database;

public sealed partial class FireworksDatabaseService
{
    private readonly string _connectionString;

    public FireworksDatabaseService(DatabaseSettings settings)
    {
        _connectionString = settings.ConnectionString;
    }

    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
    }

    public async Task PrepareEmailTableAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT fireworks.prepare_firewise_emails();";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Recipient>> LoadDistinctRecipientsAsync(CancellationToken cancellationToken = default)
    {
        var recipients = new List<Recipient>();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                lower(trim(subs_email)) AS email_addr,
                bool_or(coalesce(is_mailed, false)) AS is_mailed
            FROM fireworks.emails
            WHERE nullif(trim(coalesce(subs_email, '')), '') IS NOT NULL
            GROUP BY lower(trim(subs_email))
            ORDER BY lower(trim(subs_email));
            """;

        var rows = new List<EmailRecipientRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new EmailRecipientRow(reader.GetString(0), reader.GetBoolean(1)));
        }

        recipients.AddRange(RecipientPlanner.BuildDistinctRecipients(rows));
        return recipients;
    }

    public async Task<RecipientSummary> GetRecipientSummaryAsync(CancellationToken cancellationToken = default)
    {
        var recipients = await LoadDistinctRecipientsAsync(cancellationToken);
        return RecipientPlanner.Summarize(recipients);
    }

    public async Task<IReadOnlyList<string>> LoadNextPendingBatchAsync(
        int batchSize,
        string? emailAddressPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var recipients = new List<string>();
        var safeBatchSize = Math.Clamp(batchSize, 1, 500);
        var normalizedPrefix = RecipientPlanner.NormalizeEmail(emailAddressPrefix);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        var prefixFilter = normalizedPrefix.Length == 0
            ? string.Empty
            : "AND lower(trim(subs_email)) LIKE :email_prefix";
        command.CommandText = $"""
            SELECT email_addr
            FROM (
                SELECT
                    lower(trim(subs_email)) AS email_addr,
                    bool_or(coalesce(is_mailed, false)) AS is_mailed
                FROM fireworks.emails
                WHERE nullif(trim(coalesce(subs_email, '')), '') IS NOT NULL
                  AND lower(trim(subs_email)) ~ '^[^@[:space:]]+@[^@[:space:]]+[.][^@[:space:]]+$'
                  {prefixFilter}
                GROUP BY lower(trim(subs_email))
            ) distinct_recipients
            WHERE is_mailed = false
            ORDER BY email_addr
            LIMIT {safeBatchSize};
            """;
        if (normalizedPrefix.Length > 0)
        {
            command.Parameters.Add(new PgSqlParameter("email_prefix", normalizedPrefix + "%"));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var emailAddress = reader.GetString(0);
            if (RecipientPlanner.IsValidEmail(emailAddress))
            {
                recipients.Add(emailAddress);
            }
        }

        return recipients;
    }

    public async Task<IReadOnlyList<string>> LoadInvalidPendingRecipientsAsync(
        string? emailAddressPrefix = null,
        CancellationToken cancellationToken = default)
    {
        var recipients = new List<string>();
        var normalizedPrefix = RecipientPlanner.NormalizeEmail(emailAddressPrefix);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        var prefixFilter = normalizedPrefix.Length == 0
            ? string.Empty
            : "AND lower(trim(subs_email)) LIKE :email_prefix";
        command.CommandText = $"""
            SELECT email_addr
            FROM (
                SELECT
                    lower(trim(subs_email)) AS email_addr,
                    bool_or(coalesce(is_mailed, false)) AS is_mailed
                FROM fireworks.emails
                WHERE nullif(trim(coalesce(subs_email, '')), '') IS NOT NULL
                  AND lower(trim(subs_email)) !~ '^[^@[:space:]]+@[^@[:space:]]+[.][^@[:space:]]+$'
                  {prefixFilter}
                GROUP BY lower(trim(subs_email))
            ) invalid_recipients
            WHERE is_mailed = false
            ORDER BY email_addr;
            """;
        if (normalizedPrefix.Length > 0)
        {
            command.Parameters.Add(new PgSqlParameter("email_prefix", normalizedPrefix + "%"));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            recipients.Add(reader.GetString(0));
        }

        return recipients;
    }

    public async Task MarkBatchMailedAsync(
        IReadOnlyCollection<string> emailAddresses,
        string attachmentNote,
        CancellationToken cancellationToken = default)
    {
        if (emailAddresses.Count == 0)
        {
            return;
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var emailAddress in emailAddresses.Select(RecipientPlanner.NormalizeEmail).Where(email => email.Length > 0).Distinct())
            {
                await MarkRecipientMailedAsync(connection, transaction, emailAddress, cancellationToken);
                await InsertEmailHistoryAsync(connection, transaction, emailAddress, attachmentNote, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private PgSqlConnection CreateConnection()
    {
        return new PgSqlConnection(_connectionString);
    }

    private static async Task MarkRecipientMailedAsync(
        PgSqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string emailAddress,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE fireworks.emails
            SET is_mailed = true
            WHERE lower(trim(subs_email)) = :email_addr;
            """;
        command.Parameters.Add(new PgSqlParameter("email_addr", emailAddress));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertEmailHistoryAsync(
        PgSqlConnection connection,
        System.Data.Common.DbTransaction transaction,
        string emailAddress,
        string attachmentNote,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO fireworks.email_history (etype, email_note, email_addr)
            VALUES ('Firewise', :email_note, :email_addr);
            """;
        command.Parameters.Add(new PgSqlParameter("email_note", attachmentNote));
        command.Parameters.Add(new PgSqlParameter("email_addr", emailAddress));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}
