# Spreadsheet fields

The app now supports manual entry of the supplied tracker columns. A one-time Sheet1 import is available. Automatic posting sync is now available; see google-sheets.md for ownership rules and permanent IDs.

| Spreadsheet | App location |
| --- | --- |
| f (company names), City, Role, Posting Link | Job: company, location, title, job link |
| OPT Friendly, H-1B Visa | Job: visa information text, preserving qualifications |
| Likely Interview Process | Job: interview process |
| Posting Availability | Job: Open, Closed, Unknown |
| Last Availability Check | Job: UTC timestamp and separate evidence notes |
| Status | Application: includes Not applied, OA / Assessment, Interviewing |
| Date Found, Applied Date | Application: date found and applied date |
| Priority, Comments | Application: priority text and Your notes |
| Interview Prep Notes, Resume Tips, DSA Topics to Know | Application: preparation notes |
| Unlabelled S column | Application: additional source notes |
| Follow-up Date | Reminder Service: linked reminder due date |

Open any job from Jobs, Companies, or Applications to see its detail page. Use Edit job details for posting and visa fields, and Update application for personal tracking fields. Optional source information remains in expandable sections. Jobs are ordered by Date Found, with missing dates last. Priority and location filters are under More filters; search also includes preparation notes.

For follow-ups, save the application, then add a Reminder linked to it. Multiple reminders can be attached to one application. Times entered locally are stored in UTC; Date found is a date without a time. Visa information and priority remain descriptive text.

Existing records are retained by the AddTrackerFields migration, which runs at startup. Existing postings receive Unknown availability; new optional fields are empty. Docker volumes retain data.

Import ledgers and original source snapshots are retained privately for reconciliation and recovery. Dashboard summaries and Sheet2 account notes are excluded from job imports. Availability evidence with only a date or approximate time remains text without an invented timestamp.
Checks:
- dotnet test LutzPath.sln --configuration Release
- powershell -ExecutionPolicy Bypass -File scripts/Test-Frontend.ps1

## One-time Sheet1 import

Run powershell -ExecutionPolicy Bypass -File scripts/Import-Sheet1.ps1 to preview. Add -Apply to import, with optional -WorkbookPath, -UserId, and -ApiBase parameters. The default target is local Docker at localhost:3000/api, profile 1.

The private report is under .artifacts/imports/ and includes payloads, warnings, unassigned cells, a row-to-record ledger, and the pre-import SQLite backups for this run. Keep the ledger: rerunning the identical workbook for the same profile skips existing imported rows. A changed workbook has a different hash and is a separate import; this tool is not a synchronizer. Review its preview before applying. Existing records are never overwritten. If a write fails, keep the ledger and resume the same workbook rather than restarting from scratch.
