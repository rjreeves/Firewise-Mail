# Fire-Wise Mail Testing

## Automated Tests

Run:

```text
dotnet test FirewiseMail.slnx
```

Current automated coverage:
- Recipient email normalization.
- Duplicate recipient collapse.
- Blank and invalid email filtering.
- Sent versus pending summary counts.
- Resume batch selection that skips already mailed recipients.
- Local Fireworks database update of `emails.is_mailed` and `email_history`.
- MailKit send through a local fake SMTP server, with no real customer email sent.

## Integration Tests

Database integration tests use unique temporary `codex-fwmail-*` email addresses in the local Fireworks database and remove them after the test.

SMTP integration tests use a local fake SMTP server bound to `127.0.0.1`. They do not connect to `mail.fire-wise.com.au` and do not send real email.

The SMTP integration test scopes pending recipient selection to its unique `codex-fwmail-*` prefix so it cannot mark normal pending recipients as mailed.
