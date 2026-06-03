using FirewiseMail.App.Database;

namespace FirewiseMail.Tests.Integration;

public sealed class DatabaseIntegrationTests
{
    private readonly FireworksDatabaseFixture _database = new();

    [Fact]
    public async Task MarkBatchMailedAsync_updates_emails_and_email_history()
    {
        var email = $"codex-fwmail-db-{Guid.NewGuid():N}@example.invalid";
        const string note = "integration-test.pdf.";
        var service = new FireworksDatabaseService(_database.DatabaseSettings);

        await _database.CleanupEmailAsync(email);
        try
        {
            await _database.InsertEmailAsync(email, isMailed: false);

            await service.MarkBatchMailedAsync([email], note);

            Assert.True(await _database.IsMailedAsync(email));
            Assert.True(await _database.HasHistoryAsync(email, note));
        }
        finally
        {
            await _database.CleanupEmailAsync(email);
        }
    }

    [Fact]
    public async Task LoadNextPendingBatchAsync_filters_invalid_addresses_before_limiting_batch()
    {
        var prefix = $"codex-fwmail-batch-{Guid.NewGuid():N}";
        var invalidEmail = $"{prefix}@reeves";
        var validEmail = $"{prefix}jreeves@gmail.com";
        var service = new FireworksDatabaseService(_database.DatabaseSettings);

        await _database.CleanupEmailAsync(invalidEmail);
        await _database.CleanupEmailAsync(validEmail);
        try
        {
            await _database.InsertEmailAsync(invalidEmail, isMailed: false);
            await _database.InsertEmailAsync(validEmail, isMailed: false);

            var batch = await service.LoadNextPendingBatchAsync(1, prefix);

            var email = Assert.Single(batch);
            Assert.Equal(validEmail, email);
        }
        finally
        {
            await _database.CleanupEmailAsync(invalidEmail);
            await _database.CleanupEmailAsync(validEmail);
        }
    }
}
