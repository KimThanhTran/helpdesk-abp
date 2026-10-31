# Google Sheets export

Every time a ticket changes, the API can send a small signed summary to a Google Apps Script web app, which keeps a Google Sheet up to date for people who want a simple view or their own formulas and charts.

You do **not** need to create the spreadsheet: the script creates it in your Google Drive the first time it receives a request, and remembers it.

```
Helpdesk API ──(HTTPS POST, HMAC-signed)──▶ Apps Script web app ──▶ Google Sheet
                                                                      ├─ "Tickets": one row per ticket, latest state
                                                                      └─ "Events":  one row per change (log)
```

## Set it up (about 5 minutes)

Use your **personal** Google account unless the sheet is meant to belong to an organisation.

1. Open <https://script.google.com>, choose **New project**, delete the sample code and paste the whole of [`Code.gs`](Code.gs).
2. Pick a long random secret, for example `openssl rand -hex 32`. Open **Project settings → Script properties → Add script property** and save it as `SECRET`.
3. Choose **Deploy → New deployment → Web app**, with **Execute as: Me** and **Who has access: Anyone**, then **Deploy** and approve the permissions Google asks for (it needs to create and edit spreadsheets). Copy the web app URL, which ends in `/exec`.
4. Give the API the same two values. Set them as environment variables or in a secrets file, never in a file you commit:

   ```bash
   Helpdesk__Sheets__WebhookUrl=https://script.google.com/macros/s/<id>/exec
   Helpdesk__Sheets__Secret=<the same secret>
   ```

   With Docker Compose put them in a `.env` file as `SHEETS_WEBHOOK_URL` and `SHEETS_SECRET`.
5. Create or change a ticket. A spreadsheet called **Helpdesk tickets** appears in your Drive. If you cannot find it, run `showSpreadsheetUrl` from the Apps Script editor and read the link in the execution log.

> After you edit `Code.gs` you must **Deploy → Manage deployments → Edit → New version**, otherwise the web app keeps running the old code.

Check that the deployment is alive by opening the URL in a browser: it answers `{"ok":true,"service":"helpdesk-sheets"}` and nothing else. The script has no endpoint that returns ticket data.

## What is sent, and what is not

Privacy is the default. Out of the box a row contains only the ticket code, category, status, priority, the dates and the number of times it was reopened.

| Setting | Default | Adds to the sheet |
| --- | --- | --- |
| `Helpdesk__Sheets__IncludeTitle` | `false` | the ticket title (may contain personal details) |
| `Helpdesk__Sheets__IncludePeople` | `false` | the requester's and the assignee's user names |

The **description, the comments and the internal notes are never sent**, whatever the settings. Anyone you share the spreadsheet with can read what it holds, so decide on the settings with that in mind.

## How a request is protected

The web app has to be reachable by "anyone", because Apps Script cannot restrict a service-to-service call by account or address. Protection is therefore in the message itself:

- Each request carries `timestamp`, `payload` and `signature = HMAC-SHA256(secret, "timestamp.payload")` (lower-case hex). The script recomputes it and refuses a request that does not match.
- A request whose timestamp is more than 5 minutes away from the script's clock is refused, so a captured request cannot be replayed later.
- Apps Script cannot read HTTP headers, which is why the signature is in the body.
- The script always answers HTTP 200 (a limit of Apps Script). A refusal is reported as `{"ok":false,"error":"bad_signature"}`, and the API logs it as a warning.

To rotate the secret, change the Script property `SECRET` and the API setting at the same time.

## Behaviour worth knowing

- **Best effort.** The ticket change is saved first and the export is sent after. If Google is unreachable the failure is logged and the ticket is not affected; the missed update is not retried. A later change to the same ticket brings the row up to date.
- **Out-of-order deliveries are safe.** Each row carries `updatedAt`, and the script never replaces a row with an older one.
- **Duplicates are ignored.** Every delivery has a unique event id, so the same one arriving twice is logged once.
- **Quotas apply.** A personal Google account has daily limits on Apps Script runs. A helpdesk with a few hundred changes a day is far below them.

## Try it without a Google account

[`local-server.js`](local-server.js) runs the real `Code.gs` against an in-memory fake of Google Sheets, which is also how the script is tested:

```bash
HELPDESK_SHEETS_SECRET=dev-secret node integrations/google-sheets/local-server.js
# then start the API with  Helpdesk__Sheets__WebhookUrl=http://localhost:8787/exec  Helpdesk__Sheets__Secret=dev-secret
curl http://localhost:8787/dump        # shows what the fake "spreadsheet" contains
```

or with Docker: `docker compose --profile sheets-demo up --build` (see the main README).

## Tests

```bash
node --test integrations/google-sheets/Code.test.js
```

23 tests cover signatures, replay protection, tampering, the missing secret, creating the spreadsheet once, updating a row instead of adding one, late and duplicate deliveries, and an agreement with the .NET side: both sides check the same signature vector and the same list of field names, so a change that breaks one side fails a test on the other.
