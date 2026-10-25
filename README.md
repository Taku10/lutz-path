# LutzPath

A job application tracker for college students and recent graduates. Browse jobs by company, track application progress, prepare for interviews, and manage follow-up reminders.

Built with C#/.NET 10, ASP.NET Core Web APIs, EF Core, SQLite, Docker Compose, and a plain HTML/CSS/JavaScript frontend.

## Features

- Jobs ordered by Date Found, with search, filters, and pagination.
- Company directories and individual job detail pages.
- Application statuses, interview dates, priority, notes, and preparation information.
- Follow-up and interview reminders.
- Master Word resume uploads, tailoring profiles, AI drafts, and versioned Word exports. See [resume setup](docs/resumes.md).
- Optional Google Sheets sync with permanent source IDs and protected application fields.

## Quick start

Install Docker Desktop and use Linux containers. From the repository root:

~~~powershell
docker compose up --build --detach
~~~

Open [LutzPath](http://localhost:3000). Create a profile to start tracking jobs. A fresh checkout does not contain personal records or credentials.

To run your configured Google Sheets integration, use both Compose files:

~~~powershell
docker compose -f docker-compose.yml -f docker-compose.sheets.yml up --build --detach
~~~

See [Google Sheets setup](docs/google-sheets.md) before enabling it. The base Compose file runs without Google credentials.

## Services and data

| Service | Responsibility | Swagger |
| --- | --- | --- |
| User Service | Profiles | [localhost:5001/swagger](http://localhost:5001/swagger) |
| Job Application Service | Jobs, applications, and sheet sync | [localhost:5002/swagger](http://localhost:5002/swagger) |
| Reminder Service | Follow-ups, interviews, and deadlines | [localhost:5003/swagger](http://localhost:5003/swagger) |
| Resume Service | Master resumes, tailoring profiles, drafts, and Word exports | [health](http://localhost:5004/health) |

Each API owns a separate SQLite database. Services validate cross-service references through HTTP, using Docker service names inside containers. Nginx serves the frontend and proxies browser API requests.

| Database | Named volume | Container path |
| --- | --- | --- |
| Users | users-data | /data/users.db |
| Jobs and applications | applications-data | /data/jobapplications.db |
| Reminders | reminders-data | /data/reminders.db |
| Resumes and Word files | resumes-data | /data/resumes.db and /data/documents/ |

Docker prefixes volume names with the Compose project name. Reuse the same project to retain data. Containers apply checked-in EF Core migrations at startup.

~~~powershell
docker compose down
~~~

This preserves database volumes. Adding --volumes deletes their data.

## Repository layout

~~~text
frontend/       Static UI, Nginx configuration, and browser tests
src/            Four ASP.NET Core API projects and their migrations
tests/          Backend service, SQLite, HTTP client, and sync tests
scripts/        API, container, browser, and workbook-import utilities
docs/           Development, spreadsheet mapping, and sync guides
~~~

## Development and tests

Install the .NET 10 SDK for backend development:

~~~powershell
dotnet tool restore
dotnet restore LutzPath.sln
dotnet test LutzPath.sln --configuration Release
~~~

Browser tests also require Node.js and Microsoft Edge:

~~~powershell
npm.cmd ci --prefix frontend
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Frontend.ps1
~~~

Tests use isolated databases and test containers, not your saved workspace data.

## Documentation

- [Development and verification](docs/development.md): local APIs, REST behavior, migrations, and test scripts.
- [Frontend guide](frontend/README.md): navigation, source files, and browser behavior.
- [Spreadsheet field mapping](docs/spreadsheet-fields.md).
- [Google Sheets setup and automatic syncing](docs/google-sheets.md).

## Configuration and limits

Copy .env.example to .env only for a new setup; preserve your existing .env when updating. Host ports can be overridden there. Container-to-container traffic continues to use port 8080.

Credentials, databases, dependencies, build output, and private recovery artifacts are excluded from Git. Personal data in an existing Docker volume is not part of a GitHub push.

Profiles are workspace selectors, not authentication. Reminders are stored in the app and do not send notifications. Keep one replica per API while using SQLite.
