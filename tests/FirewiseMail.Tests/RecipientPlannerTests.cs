using FirewiseMail.App.Database;

namespace FirewiseMail.Tests;

public sealed class RecipientPlannerTests
{
    [Fact]
    public void BuildDistinctRecipients_collapses_duplicates_and_normalizes_addresses()
    {
        var recipients = RecipientPlanner.BuildDistinctRecipients(
        [
            new EmailRecipientRow(" Gordon@Fire-Wise.com.au ", false),
            new EmailRecipientRow("gordon@fire-wise.com.au", false),
            new EmailRecipientRow("editor@fire-wise.com.au", false)
        ]);

        Assert.Collection(
            recipients,
            recipient =>
            {
                Assert.Equal("editor@fire-wise.com.au", recipient.EmailAddress);
                Assert.False(recipient.IsMailed);
            },
            recipient =>
            {
                Assert.Equal("gordon@fire-wise.com.au", recipient.EmailAddress);
                Assert.False(recipient.IsMailed);
            });
    }

    [Fact]
    public void BuildDistinctRecipients_ignores_blank_and_invalid_addresses()
    {
        var recipients = RecipientPlanner.BuildDistinctRecipients(
        [
            new EmailRecipientRow(null, false),
            new EmailRecipientRow("", false),
            new EmailRecipientRow("not-an-email", false),
            new EmailRecipientRow("valid@fire-wise.com.au", false)
        ]);

        var recipient = Assert.Single(recipients);
        Assert.Equal("valid@fire-wise.com.au", recipient.EmailAddress);
    }

    [Fact]
    public void BuildDistinctRecipients_treats_duplicate_as_mailed_when_any_row_is_mailed()
    {
        var recipients = RecipientPlanner.BuildDistinctRecipients(
        [
            new EmailRecipientRow("reader@fire-wise.com.au", false),
            new EmailRecipientRow(" READER@fire-wise.com.au ", true)
        ]);

        var recipient = Assert.Single(recipients);
        Assert.True(recipient.IsMailed);
    }

    [Fact]
    public void Summarize_counts_total_pending_and_already_mailed()
    {
        var summary = RecipientPlanner.Summarize(
        [
            new Recipient("a@fire-wise.com.au", false),
            new Recipient("b@fire-wise.com.au", true),
            new Recipient("c@fire-wise.com.au", false)
        ]);

        Assert.Equal(3, summary.TotalDistinct);
        Assert.Equal(2, summary.Pending);
        Assert.Equal(1, summary.AlreadyMailed);
    }

    [Fact]
    public void SelectPendingBatch_skips_mailed_recipients_and_limits_batch_size()
    {
        var batch = RecipientPlanner.SelectPendingBatch(
        [
            new Recipient("c@fire-wise.com.au", false),
            new Recipient("a@fire-wise.com.au", true),
            new Recipient("b@fire-wise.com.au", false),
            new Recipient("d@fire-wise.com.au", false)
        ], 2);

        Assert.Equal(["b@fire-wise.com.au", "c@fire-wise.com.au"], batch);
    }
}

