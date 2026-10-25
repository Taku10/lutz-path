# LutzPath

LutzPath is a beginner-friendly backend for college students and recent graduates
to save job postings, track applications and interviews, and manage follow-up
reminders.

All five development stages are complete. The system has three independent
ASP.NET Core Web APIs, separate SQLite databases, REST communication, Docker
Compose, Swagger, and focused xUnit tests. There is no frontend in this version.

## Architecture

~~~mermaid
flowchart TD
    C[Client / Swagger]
    C --> U[User Service<br/>ASP.NET Core API]
    C --> A[Job Application Service<br/>ASP.NET Core API]
    C --> R[Reminder Service<br/>ASP.NET Core API]
    A -->|HTTP GET: verify user| U
    R -->|HTTP GET: verify application and owner| A
    U --> UD[(users.db)]
    A --> AD[(jobapplications.db)]
    R --> RD[(reminders.db)]
~~~

Each microservice is itself an ASP.NET Core Web API; there is no separate gateway.
No service references another service's project or directly accesses its database.

| Service | Responsibility | Local Swagger | SQLite data |
| --- | --- | --- | --- |
| User | Basic profile information | http://localhost:5001/swagger | Users |
| Job Application | Saved postings, application status, interviews, notes | http://localhost:5002/swagger | Jobs and Applications |
| Reminder | Due dates and completion tracking | http://localhost:5003/swagger | Reminders |

Jobs are shared posting records. An application with status Saved associates a
saved job with a user. The local Jobs-to-Applications relationship uses a foreign
key. Cross-service IDs are ordinary values validated through HTTP.

## Technologies

- C# and .NET 10.
- ASP.NET Core controller-based Web APIs.
- Entity Framework Core 10 and SQLite.
- Swagger/OpenAPI through Swashbuckle.
- HttpClient and HttpClientFactory.
- Docker, Docker Desktop, and Docker Compose.
- xUnit and Microsoft.NET.Test.Sdk.
- Visual Studio Code.

There is no Kubernetes, message broker, Redis, cloud infrastructure, service mesh,
or complex authentication in this version.

## Run with Docker

Start Docker Desktop in Linux-container mode. From the repository root:

~~~powershell
docker compose up --build
~~~

Stop local APIs already using ports 5001-5003 first. The initial build downloads
.NET images and restores packages. After startup migrations finish, open the
Swagger URLs in the table above. GET /health on each API returns HTTP 200 with
its service name and status healthy.

For background execution and logs:

~~~powershell
docker compose up --build --detach
docker compose ps
docker compose logs --follow
~~~

To stop and remove containers/network while preserving database volumes:

~~~powershell
docker compose down
~~~

The next start reuses saved data. Adding --volumes deletes this Compose project's
database volumes.

| Container | Dependency URL | Database volume/path |
| --- | --- | --- |
| userservice | None | users-data: /data/users.db |
| jobapplicationservice | http://userservice:8080/ | applications-data: /data/jobapplications.db |
| reminderservice | http://jobapplicationservice:8080/ | reminders-data: /data/reminders.db |

SQLite runs inside each API process, so no database-server containers are needed.
Each container mounts only its own data volume and runs as a non-root user.
The Dockerfiles build with the SDK and run published output with the ASP.NET
runtime. Local databases and build artifacts are excluded from images.

Containers use Docker service names. Your browser uses localhost and published
host ports. localhost inside a container refers to that same container.
depends_on controls startup order; requests still need Stage 3 failure handling
when a dependency is not ready.

Container databases are separate from local dotnet run databases. Compose adds
its project name to volume names; reuse that project name to keep using its data.

If ports are busy, copy .env.example to .env and change its host-port values.
This does not change internal service URLs. See [Stage 4](docs/stage-4.md).

## Run directly with the .NET SDK

Install the .NET 10 SDK. Stop older API processes before rebuilding to avoid
Windows file locks and serving stale code.

~~~powershell
dotnet tool restore
dotnet restore LutzPath.sln
dotnet build LutzPath.sln
~~~

Run one command per terminal:

~~~powershell
dotnet run --project src/LutzPath.UserService --launch-profile http
dotnet run --project src/LutzPath.JobApplicationService --launch-profile http
dotnet run --project src/LutzPath.ReminderService --launch-profile http
~~~

Each API applies its checked-in migrations at startup. Its SQLite database lives
inside its project directory and survives restarts. Database files are ignored
by Git; migrations are versioned source files.

The http launch profile sets Development, enabling Swagger. Health endpoints are
available in all environments and check API liveness only.

Local dependency defaults live in appsettings.json:

| Calling API | Key | Default |
| --- | --- | --- |
| Job Application | Services:UserService | http://localhost:5001/ |
| Reminder | Services:JobApplicationService | http://localhost:5002/ |

Environment variables Services__UserService and Services__JobApplicationService
override these values. ConnectionStrings__DefaultConnection overrides database
storage. Compose uses these overrides for Docker URLs and /data paths.

## API endpoints

| Resource | Endpoints |
| --- | --- |
| Users | GET/POST /api/users; GET/PUT/DELETE /api/users/{id} |
| Jobs | GET/POST /api/jobs; GET/PUT/DELETE /api/jobs/{id} |
| Applications | GET/POST /api/applications; GET/PUT/DELETE /api/applications/{id} |
| Applications for a user | GET /api/applications/user/{userId} |
| Application status | PATCH /api/applications/{id}/status |
| Reminders | GET/POST /api/reminders; GET/PUT/DELETE /api/reminders/{id} |
| Reminders for a user | GET /api/reminders/user/{userId} |

POST returns 201 and a Location header. GET/PUT/PATCH return 200.
DELETE returns 204. Missing records return 404; empty lists return 200 with [].
Invalid input or reference IDs return 400. Deleting a referenced job returns 409.
Unavailable dependencies return 503 without saving the attempted change.

PUT replaces all editable fields, so send a complete body. PATCH /status changes
only the status; it does not automatically set DateApplied.

Statuses: Saved, Applied, Assessment, Interview, Offer, Rejected, Withdrawn.
Use names in JSON, not integers. Interview/application/reminder timestamps use
UTC values ending in Z. GraduationDate and DatePosted use YYYY-MM-DD.

## Example workflow

1. Create a user and record the returned ID.
2. Create a job and record the returned ID.
3. Create an application using those IDs. The API verifies its local job and
   asks User Service whether the user exists.
4. Update the application with its interview date and status Interview.
5. Create a reminder. Reminder Service requests the application and verifies
   that its UserId matches the supplied user.

Missing or mismatched remote records return 400. Connection failures,
five-second timeouts, unexpected HTTP statuses, and invalid application lookup
payloads return 503. There are no automatic retries.

Application and reminder POST/PUT validate dependencies before changing local
data. Reads, deletes, and application status PATCH remain local. Completing a
reminder currently uses PUT and requires Job Application Service.

See the [Swagger CRUD walkthrough](docs/stage-2.md) for request bodies and the
[HTTP communication walkthrough](docs/stage-3.md) for failure exercises.

## Solution structure

~~~text
LutzPath.sln
src/
  LutzPath.UserService/
  LutzPath.JobApplicationService/
  LutzPath.ReminderService/
tests/
  LutzPath.Tests/
    Services/
    Clients/
    Infrastructure/
scripts/
  Test-Stage2.ps1
  Test-Stage3.ps1
  Test-Stage4.ps1
docs/
  stage-2.md
  stage-3.md
  stage-4.md
  stage-5.md
docker-compose.yml
.env.example
dotnet-tools.json
~~~

Each API contains Controllers, Models, DTOs, Data, Interfaces, Services, and
Migrations. APIs that call dependencies also contain Clients and Exceptions.
Program.cs configures dependency injection, SQLite, Swagger, and startup
migrations.

The usual path is Controller -> Service -> DbContext -> SQLite.
DTOs describe public JSON; entities describe stored records. Interfaces define
operations and dependency injection supplies their implementations. Service
classes apply rules and coordinate database or HTTP operations.

## Testing

Run all xUnit tests without starting APIs or Docker:

~~~powershell
dotnet test LutzPath.sln --configuration Release
~~~

Expected: 33 passed, 0 failed. Service tests use isolated in-memory SQLite
databases and fake remote clients. HTTP client unit tests replace the transport
with a small handler. They cover timeouts, caller cancellation, unexpected status
codes, and malformed responses. No mocking framework or coverage package is used.

Service tests include real EF Core/SQLite integration; they are not all pure
unit tests. Each uses the existing migrations. They never access saved local or
container database files. See [Stage 5](docs/stage-5.md) for an explanation.

Repeat the live API checks:

~~~powershell
dotnet build LutzPath.sln --configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Stage3.ps1
~~~

This includes the Stage 2 CRUD baseline and Stage 3 communication checks: 133
checks passed at the Stage 3 checkpoint. It uses ports 5101-5103 and private
databases under ignored .artifacts/.

Repeat container checks:

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Stage4.ps1
~~~

59 checks passed at the Stage 4 checkpoint, including volume persistence across
container recreation, Docker service-name communication, and outage recovery.
The script uses a unique project, ports 5201-5203, and separate volumes. Its test
containers/network are removed afterward; test volumes/logs are retained.
run-info.txt identifies the exact project and optional volume cleanup command.

## Migrations

The local dotnet-ef tool and EF Core packages are pinned to 10.0.12.

~~~powershell
dotnet ef migrations list --project src/LutzPath.UserService
dotnet ef database update --project src/LutzPath.UserService
~~~

Substitute the other project paths for their databases. InitialCreate already
exists; do not generate it again. After a model change, add a new named migration
for only the affected service and review it. These host commands affect local
databases; containers apply migrations to their own volumes at startup.

## Architecture decisions

- Three services demonstrate independent APIs, databases, and HTTP communication
  while keeping the project manageable.
- Jobs and applications share a service because they have a close relationship;
  their foreign key and local validation remain simple.
- Each service owns a database so another service cannot depend on its private
  tables or bypass its rules.
- HTTP is sufficient for the lookup workflows and easy to observe with Swagger.
- Controllers handle HTTP; business services apply rules; typed clients contain
  remote-call details.
- A separate repository layer is unnecessary for this version: DbContext and
  DbSet provide the required data access.
- Simple interfaces make dependencies replaceable in tests without introducing
  a framework or extra production abstraction.
- Advanced infrastructure was excluded to focus on C#, EF Core, REST, and Docker.

## Current limits

There is no authentication/authorization; a supplied UserId is not proof of
identity. Reminder records do not send email or notifications. Status transition
rules, unique-email checks, and duplicate-application prevention are not enforced.

Remote checks and local saves are separate operations, not a distributed
transaction. A remote record can be deleted after validation. Cross-service
deletes do not cascade, and older records are not retroactively validated.
For the learning workflow, delete reminders first, applications second, then
jobs/users.

Keep one container instance per service. Startup migrations and the SQLite
volume layout are intended for this single-instance learning environment.

All five stages are complete. No additional infrastructure was introduced.

