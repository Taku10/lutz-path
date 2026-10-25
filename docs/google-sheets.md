# Automatic Google Sheets sync

LutzPath checks Sheet1 every five minutes while the Job Application Service is running, and once at startup. Jobs shows the last successful sync, counts, row warnings, and a Sync now button. Preview Google Sheet remains read-only.

## Running the connected app

~~~powershell
docker compose -f docker-compose.yml -f docker-compose.sheets.yml up --build --detach
~~~

The ignored .env contains GOOGLE_SHEETS_CREDENTIALS_FILE (the local JSON credential path) and GOOGLE_SHEETS_AUTOMATIC_SYNC=true. The override configures profile 1, a 300-second interval, and automatic ID assignment. The base Compose file alone disables Google integration.

Credentials are mounted read-only as a Docker secret. The service account has Editor permission and the spreadsheets scope. Google cannot restrict that scope to a column; the code deliberately writes only permanent IDs in column T, never source job/application columns.

## Permanent IDs

Sheet1 column T is LutzPath ID. All original 530 rows were reconciled using full-row import fingerprints and the import ledger, resolving the four duplicate posting identities. Bindings live in the application's SQLite database.

Add a new row normally and leave its ID blank. The next cycle validates it, assigns an ID, checks readback, and imports it once. Keep the ID with its job: sort or move the ENTIRE row including column T. Do not sort A:R alone, edit IDs, or copy an existing ID onto another job.

Duplicate IDs are skipped. An existing job whose ID was cleared is flagged for reconciliation rather than duplicated. A check also catches IDs that appear to have moved onto a different existing job. Google has no atomic compare-and-set for this workflow; the writer checks content before and after assignment and pauses if the row changes. Avoid sorting while a sync is assigning IDs.

## Ownership

| Data | Sync behavior |
| --- | --- |
| Company, role, location, posting link | Refreshed from Google |
| OPT/H-1B descriptions, interview process | Refreshed from Google |
| Availability, evidence, precise check timestamp | Refreshed from Google |
| Status, comments, dates, priority, preparation notes | Initialized on new import, then protected |
| Description and Date Posted | Preserved; no source columns map to them |
| Reminders | Never overwritten or deleted |

New records initialize application fields from the sheet. Future applied dates are retained with a note. Follow-up dates and unlabelled source cells are retained in additional source notes; create their reminders in LutzPath. Existing application records are never rewritten.

Precise CT/CDT/CST check times become UTC. Date-only/approximate checks remain evidence text without an invented time. Sheet-owned posting fields can replace manual posting edits when their source changes.

## Failure handling

Missing source rows do not delete records. Locally deleted records keep binding tombstones and are not recreated. Invalid rows are skipped with warnings. Unbound IDs matching existing jobs need reconciliation rather than guessed matches.

Google failures retain saved data and retry next cycle. Manual and scheduled cycles share one lock. Database changes commit together or roll back; an ID written to Google can remain after a database failure so retry uses that same ID.

The UI checks status every 30 seconds while visible and refreshes after a successful cycle without interrupting an open editor. One replica is supported with SQLite. k3s deployment remains a separate later task.

## APIs and storage

- GET /api/integrations/google-sheets/status
- GET /api/integrations/google-sheets/preview?userId=1
- POST /api/integrations/google-sheets/sync

GoogleSheetBindings and GoogleSheetSyncStates belong to the Job Application Service database. Back up the whole database, including these tables. Setup snapshots, reconciliation mappings and a pre-sync database backup are retained privately under .artifacts/.

The first live cycle imported Mercury, refreshed 154 postings and skipped none. The repeat cycle created/updated/skipped zero rows. All 530 original applications and reminders remained unchanged. Counts naturally change on later cycles.

48 backend tests and 8 browser workflows cover matching, idempotence, protection, failures, concurrency and UI flows. Live verification also checked real ID assignment and importing the new source row.

References:
- https://developers.google.com/identity/protocols/oauth2/service-account
- https://developers.google.com/workspace/sheets/api/reference/rest/v4/spreadsheets.values/batchUpdate
