# Fire-Wise Mail Operations

## SMTP Password

The app reads the SMTP password from `appsettings.json`, but it also supports this environment variable:

```text
FIREWISE_SMTP_PASSWORD
```

If `FIREWISE_SMTP_PASSWORD` is set, it overrides the password value in `appsettings.json`.

Recommended practice:
- Keep the source `appsettings.json` password blank.
- Set `FIREWISE_SMTP_PASSWORD` on the sending machine.
- Use dry run mode before sending a real campaign.

## Layout Launchers

Use the matching launcher for the screen:

```text
FirewiseMail-1366.cmd
FirewiseMail-1080.cmd
```

