# Development and verification

## Local backend

Install the .NET 10 SDK and restore from the repository root:

~~~powershell
dotnet tool restore
dotnet restore LutzPath.sln
dotnet build LutzPath.sln --configuration Release
~~~

Run each API in a separate terminal:

~~~powershell
dotnet run --project src/LutzPath.UserService --launch-profile http
dotnet run --project src/LutzPath.JobApplicationService --launch-profile http
dotnet run --project src/LutzPath.ReminderService --launch-profile http
~~~

Stop processes already using ports 5001–5003 first. The Development launch profiles enable Swagger. GET /health reports API liveness.

Local databases are created in each API project directory. They are separate from the databases in Docker volumes. Do not delete migrations: they reproduce and upgrade the database schema.

## Request flow

Controllers accept HTTP requests and DTOs. Business services apply validation and use EF Core DbContext for local data access. Interfaces support dependency injection and test replacements. Typed HTTP clients handle remote lookups.

Application creation validates the local job and checks the user through User Service. Reminder creation checks the application and its owner through Job Application Service. Each API accesses only its own database.

Missing references return 400. Dependency outages and five-second lookup timeouts return 503 without saving the attempted change. Existing application JobId and UserId cannot be changed; reassignment returns 409.

Reads, deletes, and application status PATCH use local data. Completing a reminder uses PUT and validates its application. Remote validation and a local save are separate operations; cross-service deletions do not cascade.

## REST endpoints

| Resource | Routes |
| --- | --- |
| Users | GET/POST /api/users; GET/PUT/DELETE /api/users/{id} |
| Jobs | GET/POST /api/jobs; GET/PUT/DELETE /api/jobs/{id} |
| Applications | GET/POST /api/applications; GET/PUT/DELETE /api/applications/{id} |
| User applications | GET /api/applications/user/{userId} |
| Status | PATCH /api/applications/{id}/status |
| Reminders | GET/POST /api/reminders; GET/PUT/DELETE /api/reminders/{id} |
| User reminders | GET /api/reminders/user/{userId} |
| Google Sheets | GET status/preview and POST sync under /api/integrations/google-sheets |

POST returns 201; successful reads and updates return 200; DELETE returns 204. Missing records return 404. Deleting a job referenced by an application returns 409.

PUT replaces editable fields, so send a complete request. Status PATCH changes only Status and does not set DateApplied. Status names are NotApplied, Saved, Applied, Assessment, Interview, Offer, Rejected, and Withdrawn. JSON uses names rather than integers.

Application, interview, and reminder timestamps require UTC values ending in Z. GraduationDate, DateFound, and DatePosted use YYYY-MM-DD.

Each API project includes a .http file with manual request examples. Use a fresh test profile and returned IDs when trying writes. Swagger documents the current request schemas, including optional tracker fields.

## Configuration

| Setting | Purpose |
| --- | --- |
| ConnectionStrings__DefaultConnection | SQLite location |
| Services__UserService | User API URL for application validation |
| Services__JobApplicationService | Application API URL for reminder validation |

Local defaults are in appsettings.json. Compose overrides service URLs with Docker DNS names and database paths with /data. localhost inside a container refers to that container.

The frontend proxies browser requests through Nginx, avoiding cross-origin setup. depends_on controls container startup order; HTTP clients still handle unavailable dependencies.

## EF Core migrations

The local dotnet-ef tool is pinned in dotnet-tools.json.

~~~powershell
dotnet ef migrations list --project src/LutzPath.JobApplicationService
dotnet ef migrations add YourChange --project src/LutzPath.JobApplicationService
dotnet ef database update --project src/LutzPath.JobApplicationService
~~~

Substitute the API whose model changed. Review generated migrations before using them. Host commands update local databases; containers apply migrations to their own databases at startup.

## Verification scripts

| Command | Coverage |
| --- | --- |
| dotnet test LutzPath.sln --configuration Release | Services, migrations, SQLite persistence, HTTP clients, and sheet sync |
| scripts/Test-Stage2.ps1 | CRUD baseline on ports 5101–5103 |
| scripts/Test-Stage3.ps1 | CRUD plus cross-service validation and outage handling |
| scripts/Test-Stage4.ps1 | Docker communication, persistence, and recovery on ports 5201–5204 |
| scripts/Test-Frontend.ps1 | Desktop/mobile browser flows on ports 5300–5303 |

The stage-named scripts are retained entry points; Test-Stage3.ps1 calls Test-Stage2.ps1. Run PowerShell scripts from the repository root with -NoProfile -ExecutionPolicy Bypass -File when needed.

API checks require a Release build. Container checks require Docker Desktop. Browser checks additionally require Node.js, installed Microsoft Edge, and npm.cmd ci --prefix frontend.

Tests create private databases and unique Docker projects. Logs go under ignored .artifacts/. Container scripts remove their own containers and networks, retaining test volumes for inspection.

## Optional workbook import

scripts/Import-Sheet1.ps1 is a separate one-time importer, not the Google Sheets synchronizer. Always specify your workbook and profile explicitly:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Import-Sheet1.ps1 -WorkbookPath .\tracker.xlsx -UserId 1
~~~

This previews records. Add -Apply only after reviewing the report. The import ledger under .artifacts/imports/ prevents repeated imports of the identical workbook; keep that ledger. A modified workbook is treated as a different import.

## GitHub contents

Version source, migrations, tests, Dockerfiles, Compose files, dependency manifests/lockfiles, and documentation. Keep .env, service-account keys, SQLite files, private import reports/backups, build output, and installed dependencies out of Git.

The running app does not require old setup scripts, historical test logs, or stage-by-stage narrative documents.
