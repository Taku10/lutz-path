# Stage 5: testing, documentation, and cleanup

## What was added

tests/LutzPath.Tests is an xUnit project included in LutzPath.sln. Only the test
project references all three API projects; the running microservices remain
independent and do not reference one another.

There are 33 tests across six test classes:

| File | Behavior checked |
| --- | --- |
| Services/UserServiceTests.cs | Profile persistence and trimming |
| Services/JobServiceTests.cs | Job creation and protecting referenced jobs |
| Services/ApplicationServiceTests.cs | Valid application, missing user/job, outage, rejected updates, local status updates |
| Services/ReminderServiceTests.cs | Reminder creation, missing application, wrong owner, outage, rejected updates |
| Clients/UserServiceClientTests.cs | Request path, 200/404, unexpected statuses, connection failure, timeout, caller cancellation |
| Clients/ApplicationServiceClientTests.cs | Response contract, missing record, malformed data, server failure, timeout, caller cancellation |

The unused template test and its unused coverage collector dependency were removed.
Generated TestResults directories are ignored. Naming, required packages, and
template remnants were reviewed. No production architecture change was required.

## Run the tests

Install the .NET 10 SDK, then run from the repository root:

~~~powershell
dotnet restore LutzPath.sln
dotnet test LutzPath.sln --configuration Release
~~~

Expect:

~~~text
Passed: 33
Failed: 0
~~~

You do not need running APIs or Docker for these tests.
Stop a running Release API process if it locks files needed by the test build.

To run one group:

~~~powershell
dotnet test tests/LutzPath.Tests --configuration Release --filter FullyQualifiedName~ApplicationServiceTests
~~~

To build with compiler warnings treated as errors:

~~~powershell
dotnet build LutzPath.sln --configuration Release --no-restore --warnaserror
~~~

To verify whitespace formatting:

~~~powershell
dotnet format whitespace LutzPath.sln --no-restore --verify-no-changes
~~~

For manual CRUD requests and end-to-end verification, continue to use Swagger
and the existing Stage 3/4 smoke scripts. See README.md for their commands.

## Understanding an xUnit test

A [Fact] identifies one test. A [Theory] runs the same test with several inputs
provided by [InlineData]. This is useful for checking several malformed HTTP
responses without duplicating the test body.

Tests follow three steps:

1. Arrange: create the context, fake dependency, service, and request.
2. Act: call the service.
3. Assert: check the result or exception and verify database state.

For example, the unknown-user test provides a fake client whose UserExists is
false. It expects ArgumentException and verifies that the application table is
empty. This checks the business outcome, not just whether a helper was called.

Assert.ThrowsAsync verifies an expected exception from an asynchronous method.
The tests await service calls normally; they do not use .Wait() or .Result.

## Testing database behavior without changing your data

Infrastructure/TestDatabase.cs opens one private in-memory SQLite connection
and creates the chosen context against it. It applies the API's actual migrations.

The connection stays open for the test, because an in-memory SQLite database
disappears when its connection closes. using var ensures the context and
connection are disposed afterward.

This uses the same SQLite provider as the application rather than EF Core's
different InMemory provider. Tests exercise real persistence and foreign keys.
They do not read users.db, jobapplications.db, reminders.db, or Docker volumes.

After writes, tests clear EF's change tracker before reading stored records.
That verifies persisted values rather than merely inspecting the original
tracked object.

These are focused service tests with database integration. Pure unit tests
would replace every outside dependency, including database access. Adding a
repository solely for that purpose would complicate this project, so the small
SQLite integration is intentional.

## Fakes, interfaces, and HTTP unit tests

FakeUserServiceClient implements IUserServiceClient. Tests can make it return
true, false, or an unavailable-service exception. FakeApplicationServiceClient
can return an application, null, or an exception.

The production service receives these through its normal constructor. Production
dependency injection instead supplies the real HTTP clients. This is the
practical benefit of coding against an interface.

StubHttpMessageHandler replaces only HttpClient's transport. It can return
specific response bodies or simulate connection failure without a real server.

Timeout tests wait on a cancellable operation and set a short test-only
HttpClient timeout. Production's timeout remains five seconds.
Caller-cancellation tests ensure deliberate cancellation is not misreported as
a dependency outage.

NullLogger supplies a logger that discards messages in tests. No logging setup
or network service is needed.

The test-only aliases UserServiceImplementation and ReminderServiceImplementation
avoid ambiguity between service class names and similarly named namespaces.

## Scope of verification

- The xUnit suite checks important business outcomes and HTTP-client behavior.
- The live API scripts check routing, JSON validation, status codes, persistence,
  and communication between running APIs.
- The Docker script additionally checks Linux containers, service-name URLs,
  runtime users, named volumes, and data survival after container recreation.

The suite is intentionally small. It does not aim for 100% coverage and does not
add load tests, authentication, notification delivery, or distributed consistency.

## Project completion

The README now includes the description, architecture diagram, technologies,
Docker/local run commands, endpoints, testing, migrations, architecture decisions,
and current limitations. Earlier walkthroughs remain available as learning
references.

All five stages are complete. The result is a working backend with three
services, three SQLite databases, HTTP validation, Docker Compose, Swagger,
and repeatable tests.

