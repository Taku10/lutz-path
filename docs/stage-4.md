# Stage 4: Docker and Docker Compose

## What was added

- A multi-stage Dockerfile in each API project.
- A .dockerignore in each API project to exclude local build output and databases.
- A runnable docker-compose.yml for all three APIs.
- One named volume per service for persistent SQLite data.
- Docker service-name URLs configured through environment variables.
- .env.example for optional host-port changes.
- scripts/Test-Stage4.ps1 for isolated container workflow and persistence checks.

No API models or database schemas changed. Each API still applies its own
existing migrations at startup.

## Start the system

Open Docker Desktop and make sure it is using Linux containers. From the
repository root:

~~~powershell
docker version
docker compose config --quiet
docker compose up --build
~~~

Stop your local dotnet run APIs first if they use ports 5001-5003.
The first build downloads .NET SDK/runtime images and NuGet packages, so it takes
longer. Later builds reuse cached layers when inputs have not changed.

After the logs show the APIs are listening, open:

| API | Swagger | Health |
| --- | --- | --- |
| User | http://localhost:5001/swagger | http://localhost:5001/health |
| Job Application | http://localhost:5002/swagger | http://localhost:5002/health |
| Reminder | http://localhost:5003/swagger | http://localhost:5003/health |

GET /health returns 200 with the API's service name and status healthy.
Compose uses Development so Swagger is available for this local learning setup.

To run in the background instead:

~~~powershell
docker compose up --build --detach
docker compose ps
docker compose logs --follow
~~~

Press Ctrl+C to leave log-following. Background containers continue running.
To stop and remove this project's containers and network:

~~~powershell
docker compose down
~~~

Named database volumes remain. Run docker compose up again to reuse them.

## Images, containers, and Dockerfiles

An image is the packaged application and runtime, built from a Dockerfile.
A container is a running instance of that image, with its own process and network
environment. Docker Desktop runs these Linux containers on your Windows machine.

Each Dockerfile has two stages:

1. The SDK stage restores packages and publishes Release output. The SDK contains
   the compiler and build tools.
2. The ASP.NET runtime stage copies only the published application and runs it.
   It does not include the full SDK or your source tree.

The project file is copied before other source files so Docker can reuse the
restore layer when only C# source changes.

The API runs as the image's non-root app user. The /data directory is owned by
that user. A newly created named volume receives that directory's ownership, so
SQLite can write its database and journal files there.

WORKDIR sets the directory for later commands.
COPY transfers files from the build context or another image stage.
RUN executes a command while building the image.
USER chooses the runtime account.
ENTRYPOINT starts dotnet with the service's published DLL.
EXPOSE documents port 8080; Compose ports actually publishes it to your computer.

Each service's build context is its own project directory. Its .dockerignore
excludes bin, obj, local SQLite files, HTTP request examples, and .env. This keeps
local database contents and Windows build artifacts out of Linux images.

## Compose and networking

Compose describes the containers, settings, networks, and volumes needed to run
the application together.

~~~mermaid
flowchart TD
    C[Browser / API client]
    C -->|localhost:5001| U[userservice:8080]
    C -->|localhost:5002| A[jobapplicationservice:8080]
    C -->|localhost:5003| R[reminderservice:8080]
    A -->|HTTP GET: user lookup| U
    R -->|HTTP GET: application lookup| A
    U --> UV[(users-data volume<br/>/data/users.db)]
    A --> AV[(applications-data volume<br/>/data/jobapplications.db)]
    R --> RV[(reminders-data volume<br/>/data/reminders.db)]
~~~

Compose creates a private default network for this project. Its DNS resolves
userservice, jobapplicationservice, and reminderservice to the appropriate
containers. You do not need fixed IP addresses.

The calling APIs override their local settings:

~~~yaml
# Job Application Service:
Services__UserService: "http://userservice:8080/"

# Reminder Service:
Services__JobApplicationService: "http://jobapplicationservice:8080/"
~~~

Double underscores represent nested .NET configuration keys.

localhost inside Reminder Service means Reminder Service's own container.
It does not mean your Windows computer or Job Application Service.
Use http://jobapplicationservice:8080/ from that container.

Your browser is outside Docker, so it uses localhost with the published host
port. It cannot resolve Docker's private service names.

All containers can listen on 8080 because each has its own network environment.
Compose maps three different host ports to those internal ports.

depends_on specifies container startup order, not API readiness. Each API
can start without calling another API. A request made before a dependency is
ready receives the Stage 3 failure response, so wait for /health or retry.
There are no Docker health checks or automatic application-request retries.

## Persistent and separate SQLite databases

| Compose volume | Mounted into | SQLite file |
| --- | --- | --- |
| users-data | User Service /data | /data/users.db |
| applications-data | Job Application Service /data | /data/jobapplications.db |
| reminders-data | Reminder Service /data | /data/reminders.db |

ConnectionStrings__DefaultConnection points to each absolute container path.

Volumes are Docker-managed storage outside the container's writable layer.
Rebuilding an image or replacing a container does not delete its named volume.
SQLite's database and any WAL/SHM files share that same directory.

Only the owning service mounts each volume. There are no database-server
containers: SQLite runs within the API process.

These are fresh container databases, separate from the *.db files used by local
dotnet run. Local records are not automatically imported.

Compose prefixes volume names with the project name. For this directory the
default project is normally lutz-path, giving names such as lutz-path_users-data.
Use the same project name to reconnect to the same databases.

docker compose down --volumes deletes that project's named database volumes.
Use ordinary docker compose down when you want to preserve your records.

Keep one container instance per service for this learning project. This SQLite
setup is not intended for multiple replicas sharing a database file.

## Optional port changes

If you want local APIs and container APIs running at the same time:

~~~powershell
Copy-Item .env.example .env
~~~

Edit .env:

~~~dotenv
USER_SERVICE_PORT=5201
JOB_APPLICATION_SERVICE_PORT=5202
REMINDER_SERVICE_PORT=5203
~~~

Then run docker compose up --build. Swagger uses ports 5201-5203.
The environment variables in your PowerShell session take precedence over .env.
The .env file is ignored by Git.

Changing host ports does not change service-to-service URLs, which still use
the Docker service names and port 8080. Host bindings use 127.0.0.1 for local use.

## Try the complete workflow

Use the request bodies in docs/stage-2.md, substituting returned IDs:

1. POST /api/users on port 5001.
2. POST /api/jobs on port 5002.
3. POST /api/applications on port 5002. Its user lookup now goes through Docker
   DNS to userservice:8080. Expect 201.
4. PUT /api/applications/{id} with the complete body, an interviewDate ending
   in Z, and status Interview. Expect 200.
5. POST /api/reminders on port 5003. Its application lookup goes through
   jobapplicationservice:8080 and checks the user's ID. Expect 201.
6. PUT the reminder with completed true. Expect 200.

To verify persistence, note the IDs, run docker compose down, start again, and
GET each record. It should still exist.

To observe failure handling:

~~~powershell
docker compose stop userservice
~~~

Creating an application returns 503; reading existing applications and updating
only their status still works. Restart the dependency:

~~~powershell
docker compose start userservice
~~~

Wait for its health endpoint and retry. For reminders, stop/start
jobapplicationservice instead. Reminder reads remain available while POST/PUT
returns 503.

## Repeatable container verification

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Stage4.ps1
~~~

The script:

- Builds and runs the same Compose file under a unique test project name.
- Uses ports 5201-5203 by default; pass -BasePort 5301 to change them.
- Uses separate named test volumes, leaving normal databases and containers alone.
- Checks non-root runtime accounts, correct volume mounts, Swagger, and the workflow.
- Removes and recreates containers to prove database persistence.
- Checks dependency outages and recovery.
- Removes its test containers/network when finished.
- Retains test volumes and writes logs/project details under ignored .artifacts/.

Test records are cleaned up on success. run-info.txt identifies the exact
test project and provides an optional command to remove only its test volumes.
It does not run docker system prune or remove unrelated resources.

## Verified results

All three Linux images built successfully. The container smoke test passed 59
checks: the full HTTP workflow, non-root users, separate database volumes,
Swagger, persistence after removing/recreating containers, and dependency
outages/recovery. Test containers and their network were removed afterward.

The script uses 127.0.0.1 explicitly because short health-check timeouts can be
affected by localhost IPv6 fallback when Compose publishes only IPv4.

## Troubleshooting

- Docker server connection error: start Docker Desktop and select Linux containers.
- Host port already in use: stop local APIs or use the .env overrides.
- Container exits: inspect docker compose logs followed by the service name.
- GET works but POST returns 503: check the dependency's health endpoint and logs.
- Editing source has no effect: rerun docker compose up --build.
- SQLite write errors with an older manually created volume: inspect its /data
  ownership. The supplied image initializes new volumes for the app user.
- Host migration commands affect local databases, not the container volumes.
  Containers apply the checked-in migrations themselves at startup.

This completes the Stage 4 checkpoint. Stage 5 tests and final documentation are now available in stage-5.md.
