# Fire-Wise Mail

A new project.

Goals & Features:-
- using dotConnect and connection string "User Id=fireworks;Host=127.0.0.1;Database=fireworks;Initial Schema=fireworks"
- Use Mailkit for emailing a document to more than 3,000 email address
- the program needs to be able to restart, when the mail server errors. 
- Suggest using the last successfull email sent as a starting point and continue with next email.
- The program need to point to a PDF files (normally one but occasionly 2 -3), which are to be attached to a standard email body
- Feature to edit the email body
- The source of email address in the database table emails. Note to avoid duplicate email address suggest DISTINCT on reading the table
- The emails table is to be updated with flag is_emailed
- Also the email_history table to be updated
- Lets have a GUI - wpf app, giving option to satisfy above and some progress indication say percentage of distinct total
- Also produce 2 logs indicating 1. Successful sent emails. 2. Emails errored with reason.
- Using Mailkit I suggest we send say 100 at a time - maybe using BCC. And one open connection to the mail server. Make the 100 value configurable
- Send and return email "Gordon@fire-wise.com.au"
- Mail server credentials "invoicing@fire-wise.com.au", "F1r3W1s3!Acc0unts"
- to prepare/reset emails table use "SELECT  `fireworks.prepare_firewise_emails()"


