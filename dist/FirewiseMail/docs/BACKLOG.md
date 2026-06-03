# Fire-Wise Mail Backlog

## Purpose

Build a WPF desktop application that prepares a Fire-Wise email run, loads distinct recipients from the Fireworks PostgreSQL database, sends PDF attachments using MailKit, records progress safely, and resumes after mail server errors.

## MVP Scope

- Connect to the local Fireworks PostgreSQL database using dotConnect.
- Provide a database prepare/reset step using `fireworks.prepare_firewise_emails()`. 
- The prepare/reset SQL is exactly `SELECT fireworks.prepare_firewise_emails();`.
- Load distinct email addresses from the `emails` table.
- Allow the operator to select one to three PDF attachments.
- Allow the operator to edit the email subject and body.
- Send emails using MailKit with a configurable batch size.
- Keep one SMTP connection open where practical.
- Resume after failure by continuing from successfully sent email state.
- Update `emails.is_mailed`.
- Update `email_history`.
- Show WPF progress as a percentage of distinct total recipients.
- Produce separate success and error logs. 
- All failed emails are to be logged, no further action.
- Table `emails`:
  - `is_mailed` boolean nullable
  - `subs_email` text nullable
- Table `email_history`:
  - `email_note` text nullable
  - `edatetime` timestamp without time zone nullable default now()
  - `etype` text nullable
  - `email_addr` text nullable
  - `subs_no` integer nullable
  - `cust_no` integer nullable
- Make `email_history.etype` = `Firewise`.
- Make `email_history.email_note` = name of the attached PDF. If multiple PDFs are attached, store all attachment names separated by commas, with a full stop at the end.

- SMTP server is `mail.fire-wise.com.au`.
- SMTP username is `invoicing@fire-wise.com.au`. This is only for SMTP server access and is not used as an email address.
- For MailKit use port 465 with SSL/TLS implicit TLS.

- Prefer to send emails in batches because individual sending appears slow.
- The email body is HTML and defaults to:

```html
<html><br>
<p>
Please find electronic version of Fire Wise attached.<br><br>
Gordon Rippon-King<br>
Managing Editor<br>
'Fire Wise'<br>
<b>
PO Box 496, Cowes, 3922<br>
M: 0402 051 412<br>
www.fire-wise.com.au
</b>
</p>
</html>
```
- Email subject defaults to `The current edition of Fire Wise`.
- Each batch is sent as one message with recipients in BCC.
- When a BCC batch is accepted successfully, mark `emails.is_mailed = true` for every address in that batch.
- Sending uses database-driven batching: fetch the next pending batch, send it, mark it mailed, then fetch the next pending batch. Do not rely on keeping all recipients in memory during sending.
- GUI supports separate launch profiles for known screen resolutions: `1366x768` and `1920x1080`.


## Epics

### 1. Project Foundation

#### FWMAIL-001: Create WPF Application Skeleton

Status: Complete.

Create the initial .NET WPF project structure.

Acceptance criteria:
- The application builds and starts.
- The main window opens without database or mail server dependencies.
- Project references are ready for WPF, MailKit, and dotConnect.

#### FWMAIL-002: Add Application Configuration

Status: Complete.

Store database, SMTP, sender, return address, batch size, and log path settings in configuration.

Acceptance criteria:
- Default database connection string is available:
  `User Id=fireworks;Host=127.0.0.1;Database=fireworks;Initial Schema=fireworks`
- Batch size defaults to 100 and is configurable.
- Sender, reply-to, and return email default to `Gordon@fire-wise.com.au`.
- SMTP username can be configured.
- SMTP password is not hardcoded in source code.

#### FWMAIL-003: Add Logging Infrastructure

Status: Complete.

Create separate logs for successful sends and failed sends.

Acceptance criteria:
- Success log records recipient, timestamp, attachment names, and job/run id.
- Error log records recipient or batch, timestamp, and failure reason.
- Log files are created automatically if missing.
- Log paths are visible in the GUI.

### 2. Database Integration

#### FWMAIL-004: Connect to Fireworks Database

Status: Complete.

Implement database connection using dotConnect.

Acceptance criteria:
- The app can test the database connection from the GUI.
- Connection errors are shown clearly.
- Connection string is read from configuration.

#### FWMAIL-005: Prepare Email Table

Status: Complete.

Run the database preparation/reset function before loading recipients.

Acceptance criteria:
- The GUI has a clear prepare/reset action.
- The action calls `SELECT fireworks.prepare_firewise_emails();` once confirmed.
- The operator is warned before running the prepare/reset step.
- Any function error is shown and logged.
- Recipient loading is refreshed after a successful prepare/reset.

#### FWMAIL-006: Load Distinct Email Recipients

Status: Complete.

Read distinct email addresses from the `emails` table.

Acceptance criteria:
- Duplicate email addresses are removed.
- Blank email addresses are excluded.
- Invalid email addresses are flagged or excluded.
- The GUI shows total distinct recipients.
- The GUI shows pending and already emailed counts.

#### FWMAIL-007: Track Email Send State

Status: Complete.

Use `emails.is_mailed` to identify sent and pending recipients.

Acceptance criteria:
- Already emailed recipients are not sent again.
- Pending recipients are selected after restart.
- Resume behavior is based on database state, not only local logs.
- Failed recipients remain unsent unless explicitly marked otherwise.

#### FWMAIL-008: Update Database After Send

Status: Complete.

Update database state after each successful send.

Acceptance criteria:
- `emails.is_mailed` is updated after a successful email.
- `email_history` is updated with send details.
- `email_history.etype` is set to `Firewise`.
- `email_history.email_note` is set to the attached PDF file name, or all attached PDF names separated by commas with a full stop at the end.
- Failed sends do not get marked as sent.
- Database updates happen soon enough that a crash can resume safely.

### 3. Email Composition

#### FWMAIL-009: Edit Email Subject and Body

Status: Complete.

Provide GUI fields for email subject and body text.

Acceptance criteria:
- Subject and body are editable before sending.
- Subject defaults to `The current edition of Fire Wise`.
- Body supports multi-line text.
- Body defaults to the Fire Wise HTML message.
- Values remain visible during the session.
- Empty subject or body blocks sending.

#### FWMAIL-010: Select PDF Attachments

Status: Complete.

Allow the operator to select one to three PDF files.

Acceptance criteria:
- Only PDF files can be selected.
- At least one PDF is required before sending.
- Up to three PDFs can be attached.
- Selected files are shown in the GUI.
- Missing files are detected before sending.

#### FWMAIL-011: Validate Campaign Before Send

Status: Complete.

Check required fields and dependencies before SMTP sending starts.

Acceptance criteria:
- The app blocks send if no pending recipients are available.
- The app blocks send if attachments are missing.
- The app blocks send if subject or body is empty.
- The app warns if SMTP or database settings are incomplete.
- The app shows a final confirmation with recipient count and attachment names.

### 4. Mail Sending

#### FWMAIL-012: Implement MailKit SMTP Send

Status: Complete.

Send email using MailKit.

Acceptance criteria:
- SMTP connection can be opened and authenticated.
- SMTP authentication uses `invoicing@fire-wise.com.au`.
- Sender, reply-to, and return email are set to `Gordon@fire-wise.com.au` unless changed in configuration.
- Attachments are included.
- Errors are caught, shown, and logged.

#### FWMAIL-013: Send in Configurable Batches

Status: Complete.

Send recipients in configurable batches, defaulting to 100.

Acceptance criteria:
- Batch size is read from configuration or GUI input.
- Batch size can be changed before sending.
- One SMTP connection is reused where practical.
- The app continues through multiple batches.
- Batch progress is shown.

#### FWMAIL-014: Send Batches Using BCC

Status: Complete.

Send each batch as one email message with recipients in BCC.

Acceptance criteria:
- Customer email addresses are placed in BCC, not TO or CC.
- Customers cannot see other customer addresses.
- `emails.is_mailed` is marked true for every recipient in a successfully accepted BCC batch.
- Failed batches are logged and are not marked as sent.

#### FWMAIL-015: Handle SMTP Failures and Restart

Status: Complete.

Make sending restart-safe when the mail server errors.

Acceptance criteria:
- The app stops cleanly on unrecoverable SMTP failure.
- Successful sends before the error remain recorded.
- Restart resumes with unsent recipients from `emails.is_mailed`.
- Error reason is logged.
- The operator can retry after fixing the SMTP issue.

### 5. GUI Experience

#### FWMAIL-016: Main Window Layout

Status: Complete.

Build the main WPF screen for setup, preparation, review, send, and progress.

Acceptance criteria:
- Operator can see database status, recipient counts, attachments, subject/body, batch size, and progress.
- Prepare/reset action is separate from send action.
- Send button is disabled until validation passes.
- Stop/cancel control is available during sending.
- The app can be launched with a `1366x768` layout profile.
- The app can be launched with a `1920x1080` layout profile.

#### FWMAIL-017: Progress Display

Status: Complete.

Show campaign progress by percentage and counts.

Acceptance criteria:
- Shows sent count, failed count, pending count, and total distinct count.
- Shows percentage of distinct total.
- Shows current batch progress.
- Progress updates while sending.

#### FWMAIL-018: Error Feedback

Status: Complete.

Show meaningful errors without hiding log details.

Acceptance criteria:
- Database errors are distinguishable from SMTP errors.
- The most recent error is visible in the GUI.
- The operator can find the error log path.
- The app remains usable after recoverable errors.

### 6. Safety and Operations

#### FWMAIL-019: Dry Run Mode

Status: Complete.

Add an optional dry run mode for validation without sending.

Acceptance criteria:
- Dry run can test database connection and recipient loading.
- Dry run can validate attachments and SMTP settings.
- No emails are sent.
- No `is_mailed` flags are updated.
- Dry run results are visible in the GUI.

#### FWMAIL-020: Confirm Prepare and Send Actions

Status: Complete.

Protect operations that affect many recipients.

Acceptance criteria:
- Prepare/reset requires confirmation.
- Send requires confirmation.
- Send confirmation shows recipient count, sender, batch size, and attachment names.
- Sending does not begin until confirmed.

#### FWMAIL-021: Protect Secrets

Status: Complete.

Move SMTP credentials out of project source and documentation.

Acceptance criteria:
- Password is stored in a secure or local-only configuration location.
- Source files do not contain the SMTP password.
- Documentation explains how to configure credentials.

### 7. Testing

#### FWMAIL-022: Unit Test Recipient Loading

Status: Complete.

Test recipient filtering and duplicate handling.

Acceptance criteria:
- Duplicate emails are collapsed.
- Blank and invalid addresses are handled.
- Pending-only logic is tested.

#### FWMAIL-023: Unit Test Resume Logic

Status: Complete.

Test that sent recipients are skipped after restart.

Acceptance criteria:
- Previously sent recipients are not selected again.
- Failed recipients remain pending unless explicitly skipped.
- Resume uses database state.

#### FWMAIL-024: Integration Test Database Updates

Status: Complete.

Verify updates to `emails.is_mailed` and `email_history`.

Acceptance criteria:
- Successful sends update both database targets.
- Failed sends do not update `is_mailed`.
- Prepare/reset function can be tested safely against a test database.

#### FWMAIL-025: Integration Test with Test SMTP

Status: Complete.

Verify sending through a local or test SMTP server.

Acceptance criteria:
- Attachments are present in received messages.
- Sender, reply-to, and return email are correct.
- MailKit uses `mail.fire-wise.com.au` on port 465 with SSL/TLS implicit TLS.
- Logs and database updates match the send result.

## Suggested Build Order

1. FWMAIL-001: Create WPF Application Skeleton
2. FWMAIL-002: Add Application Configuration
3. FWMAIL-003: Add Logging Infrastructure
4. FWMAIL-004: Connect to Fireworks Database
5. FWMAIL-005: Prepare Email Table
6. FWMAIL-006: Load Distinct Email Recipients
7. FWMAIL-007: Track Email Send State
8. FWMAIL-009: Edit Email Subject and Body
9. FWMAIL-010: Select PDF Attachments
10. FWMAIL-011: Validate Campaign Before Send
11. FWMAIL-012: Implement MailKit SMTP Send
12. FWMAIL-008: Update Database After Send
13. FWMAIL-015: Handle SMTP Failures and Restart
14. FWMAIL-016: Main Window Layout
15. FWMAIL-017: Progress Display
16. FWMAIL-019: Dry Run Mode
17. FWMAIL-022 to FWMAIL-025: Tests

## Open Questions

- Should a failed BCC batch be retried automatically during the same run, or only logged for later operator review? Future decision.
