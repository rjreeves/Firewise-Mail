using System.Text.RegularExpressions;

namespace FirewiseMail.App.Database;

public static partial class RecipientPlanner
{
    public static IReadOnlyList<Recipient> BuildDistinctRecipients(IEnumerable<EmailRecipientRow> rows)
    {
        return rows
            .Select(row => new EmailRecipientRow(NormalizeEmail(row.EmailAddress), row.IsMailed))
            .Where(row => !string.IsNullOrWhiteSpace(row.EmailAddress))
            .Where(row => EmailRegex().IsMatch(row.EmailAddress!))
            .GroupBy(row => row.EmailAddress!)
            .Select(group => new Recipient(
                group.Key,
                group.Any(row => row.IsMailed == true)))
            .OrderBy(recipient => recipient.EmailAddress)
            .ToList();
    }

    public static RecipientSummary Summarize(IEnumerable<Recipient> recipients)
    {
        var recipientList = recipients.ToList();
        var alreadyMailed = recipientList.Count(recipient => recipient.IsMailed);
        return new RecipientSummary(recipientList.Count, recipientList.Count - alreadyMailed, alreadyMailed);
    }

    public static IReadOnlyList<string> SelectPendingBatch(IEnumerable<Recipient> recipients, int batchSize)
    {
        var safeBatchSize = Math.Clamp(batchSize, 1, 500);
        return recipients
            .Where(recipient => !recipient.IsMailed)
            .OrderBy(recipient => recipient.EmailAddress)
            .Take(safeBatchSize)
            .Select(recipient => recipient.EmailAddress)
            .ToList();
    }

    public static string NormalizeEmail(string? emailAddress)
    {
        return (emailAddress ?? string.Empty).Trim().ToLowerInvariant();
    }

    public static bool IsValidEmail(string emailAddress)
    {
        return EmailRegex().IsMatch(emailAddress);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}

