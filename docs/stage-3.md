# Stage 3: service-to-service HTTP communication

## What changed

Application POST and PUT now validate UserId through User Service.
Reminder POST and PUT now validate ApplicationId and its owner through
Job Application Service. Each check completes before any local data is changed.

No schemas changed, so no new migrations or packages were needed.
ASP.NET Core already supplies HttpClientFactory support.

~~~mermaid
flowchart LR
    C[Client / Swagger] --> U[User API]
    C --> A[Job Application API]
    C --> R[Reminder API]
    A -->|GET /api/users/id| U
    R -->|GET /api/applications/id| A
    U --> UD[(users.db)]
    A --> AD[(jobapplications.db)]
    R --> RD[(reminders.db)]
~~~

This diagram shows all available routes; one request does not always visit all
three services. For example, the application's GET endpoint reads its database
without calling User Service.

## Run the system

Stop your older API processes with Ctrl+C before rebuilding.

From the repository root:

~~~powershell
dotnet build LutzPath.sln
~~~

Run one command in each of three terminals:

~~~powershell
dotnet run --project src/LutzPath.UserService --launch-profile http
dotnet run --project src/LutzPath.JobApplicationService --launch-profile http
dotnet run --project src/LutzPath.ReminderService --launch-profile http
~~~

Swagger pages remain http://localhost:5001/swagger,
http://localhost:5002/swagger, and http://localhost:5003/swagger.

## Follow a request through the system

1. POST a user in User Service and record its returned id.
2. POST a job in Job Application Service and record its returned id.
3. POST an application with those IDs on port 5002:

~~~json
{
  "userId": 1,
  "jobId": 1,
  "status": "Applied",
  "dateApplied": "2026-10-24T15:00:00Z",
  "interviewDate": null,
  "notes": "Submitted online."
}
~~~

Replace 1 with your actual IDs.

ApplicationsController calls ApplicationService.CreateAsync. The service first
checks its local job table. It then asks IUserServiceClient whether the user
exists. UserServiceClient sends GET http://localhost:5001/api/users/1.

If User Service returns 200, the application service saves its own application
and returns 201. If User Service returns 404, it returns 400 without saving.

4. Record an interview with PUT /api/applications/{id}, sending the complete
   body with an interviewDate ending in Z and status Interview.
5. POST a reminder on port 5003 using the returned application ID:

~~~json
{
  "userId": 1,
  "applicationId": 1,
  "title": "Prepare for interview",
  "dueDate": "2026-11-04T15:00:00Z",
  "completed": false
}
~~~

ReminderService asks IApplicationServiceClient for the application.
ApplicationServiceClient sends GET
http://localhost:5002/api/applications/1. It deserializes just the Id and UserId
fields it needs into ApplicationReference. It does not reference another
project's EF entity.

The reminder is saved only when the application exists and its UserId matches.
This checks consistency between supplied IDs; authentication is still out of scope.

The complete CRUD request bodies are also available in stage-2.md and each
service's .http file.

## Important files

Job Application Service:

- Interfaces/IUserServiceClient.cs: the user lookup contract.
- Clients/UserServiceClient.cs: performs the HTTP GET and handles HTTP failures.
- Services/ApplicationService.cs: checks the user before POST/PUT writes.
- Controllers/ApplicationsController.cs: translates validation failures into 400
  and dependency failures into 503.
- Exceptions/ServiceUnavailableException.cs: a readable signal for a failed
  dependency call.
- Program.cs: registers and configures the typed client.
- appsettings.json: contains Services:UserService.

Reminder Service follows the same structure with IApplicationServiceClient,
ApplicationServiceClient, and ApplicationReference.

## HttpClient, interfaces, and dependency injection

HttpClient sends HTTP requests and receives responses.

HttpClientFactory creates configured HttpClient instances and manages the
underlying HTTP handlers/connections. A typed client is a class dedicated to one
remote API. Its constructor receives the configured HttpClient.

Program.cs registers the typed client:

~~~csharp
builder.Services.AddHttpClient<IUserServiceClient, UserServiceClient>(client =>
{
    var baseUrl = builder.Configuration["Services:UserService"]
        ?? throw new InvalidOperationException("UserService URL is missing.");
    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});
~~~

Do not also register this client with AddScoped or AddTransient: AddHttpClient
already registers its interface/implementation pair.

ApplicationService receives IUserServiceClient through its constructor. The
business service knows how to ask whether a user exists without having to know
how an HTTP request is constructed. Later, unit tests can supply a fake
IUserServiceClient without starting a network server.

The configured base URL plus api/users/1 produces the full request URL.
using var disposes the response after reading it. await waits for the result
without synchronously blocking the request thread. CancellationToken forwards
caller cancellation to the outgoing request.

The catch filter:

~~~csharp
catch (OperationCanceledException exception)
    when (!cancellationToken.IsCancellationRequested)
~~~

handles an HTTP timeout while letting a caller's deliberate cancellation
propagate normally.

Clients convert connection failures, timeouts, and unexpected HTTP status codes
into ServiceUnavailableException. The application client also handles malformed,
missing, or inconsistent application response data. Controllers return safe,
readable ProblemDetails responses instead of exposing raw exception details.
Client warnings retain technical diagnostics in local logs.

## Configuration

Local defaults:

| Calling API | Setting | Value |
| --- | --- | --- |
| Job Application | Services:UserService | http://localhost:5001/ |
| Reminder | Services:JobApplicationService | http://localhost:5002/ |

Environment variables override these values:

~~~powershell
# Example when starting Job Application Service:
$env:Services__UserService = "http://localhost:5001/"
dotnet run --project src/LutzPath.JobApplicationService --launch-profile http
~~~

Use Services__JobApplicationService for Reminder Service.
Two underscores represent the configuration section separator.

These URLs are for local development. The Stage 4 Compose configuration now
overrides them with Docker service names. See stage-4.md for the container workflow.

## Failure behavior

| Situation | Result |
| --- | --- |
| Unknown local job when creating/updating an application | 400; no write |
| User Service returns 404 | 400; no write |
| Application API returns 404 during reminder validation | 400; no write |
| Application belongs to another user | 400; no write |
| Connection failure or five-second HTTP timeout | 503; no write |
| Unexpected dependency HTTP response or invalid application payload | 503; no write |
| The requested local PUT target does not exist | 404 before remote lookup |
| Dependency recovers | A subsequent valid request can succeed |

There are no automatic retries. Failed requests return control to the caller.
A client can try again after the service is available.

Both POST and full PUT validate their remote references, even if those IDs have
not changed. This keeps the rule straightforward. During an outage, existing
GET, DELETE, and application PATCH /status operations remain local. Reminder
completion currently uses PUT, so it needs Job Application Service available.

## Try the failure cases in Swagger

Use a real local job ID when testing user failures; local job validation runs first.

1. Choose a positive user ID that GET /api/users/{id} confirms is missing.
   POST an application with that ID. Expect 400 and a message saying the user
   does not exist. Verify GET /api/applications has no new record.
2. Stop User Service with Ctrl+C. POST the same application with a known valid
   user and job. Expect 503 with a useful detail message:

~~~json
{
  "title": "Dependency unavailable",
  "status": 503,
  "detail": "User Service is unavailable. Please try again later."
}
~~~

The actual response also contains standard ProblemDetails metadata.
A slow server produces a similar message saying it did not respond in time.

3. GET existing applications and PATCH an existing status while User Service
   is stopped. These still work. Restart User Service and retry the POST;
   expect 201.
4. POST a reminder for a missing application: expect 400.
5. POST a reminder using an existing application's ID but a different user's
   ID: expect 400.
6. Stop Job Application Service. GET existing reminders: expect 200.
   POST or PUT a reminder: expect 503, with no database changes.
   Restart Job Application Service and retry a valid request: expect success.

A reminder can still be created while User Service is stopped, because its
application lookup reads the application service's local data. It does not
add another user lookup.

## Repeatable verification

~~~powershell
dotnet build LutzPath.sln --configuration Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Stage3.ps1
~~~

This runs the Stage 2 CRUD checks plus live HTTP validation, ownership,
unavailable-dependency, unchanged-data, and recovery checks. It uses ports
5101-5103, overrides dependency URLs to those same ports, and uses isolated
SQLite files under ignored .artifacts/. It stops only its own processes.
The final output reports the check count and artifact location.

The build passed with zero warnings/errors and the Stage 3 script passed 133
checks. The script tests stopped-service outages; five-second timeouts and
unexpected response handling are implemented but not simulated by this script.
Stage 5 now adds focused tests for these client failure cases; see stage-5.md.

## Database ownership and limits

Reading another service's database would tie callers to its private schema and
storage location. HTTP gives each service control over its data and exposes
only its public contract.

These HTTP checks and database saves are separate operations, not a distributed
transaction. A remote record could be deleted after validation. Existing Stage 2
records are not retroactively validated. Deleting users or applications still
does not cascade across service databases. Keep the documented cleanup order
while learning; a stronger deletion policy can be designed separately.

This completes the Stage 3 checkpoint. Dockerfiles and Compose are now documented in stage-4.md.
