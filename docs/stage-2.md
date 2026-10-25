# Stage 2: try the CRUD APIs in Swagger

Start all three APIs using the README commands. In Swagger, expand an operation,
select **Try it out**, enter the body or ID, then select **Execute**.

Record IDs from responses; the examples below use 1 only as a placeholder.
IDs may differ if you already created data.

## 1. Create and update a user

Open http://localhost:5001/swagger and use POST /api/users:

~~~json
{
  "firstName": "Taylor",
  "lastName": "Student",
  "email": "taylor@example.com",
  "graduationDate": "2027-05-15"
}
~~~

Expect 201 with an id and the supplied fields. GET /api/users should list it;
GET /api/users/{id} should return it. For PUT /api/users/{id}, send the same body
with a changed firstName; expect 200 and the updated record.

Try an invalid email or blank firstName: expect 400 with validation errors.

## 2. Save a job posting

Open http://localhost:5002/swagger and use POST /api/jobs:

~~~json
{
  "company": "Example Company",
  "title": "Graduate Developer",
  "location": "Chicago",
  "jobUrl": "https://example.com/jobs/1",
  "description": "Entry-level C# role",
  "datePosted": "2026-10-24"
}
~~~

Expect 201. Note the returned job id. GET the job individually and list all jobs.
Use PUT with the full body and location changed to Remote; expect 200.
A blank company/title or malformed URL returns 400.

## 3. Create an application

On the same API, use POST /api/applications. Substitute the user and job IDs:

~~~json
{
  "userId": 1,
  "jobId": 1,
  "status": "Applied",
  "dateApplied": "2026-10-24T15:00:00Z",
  "interviewDate": null,
  "notes": "Submitted through the company website."
}
~~~

Expect 201. GET /api/applications, GET /api/applications/{id}, and
GET /api/applications/user/{userId} should return the matching records.

For a user who has not applied yet, status Saved and dateApplied null represent
a saved job associated with that user.

To record an interview, PUT /api/applications/{id} with the full body:

~~~json
{
  "userId": 1,
  "jobId": 1,
  "status": "Interview",
  "dateApplied": "2026-10-24T15:00:00Z",
  "interviewDate": "2026-11-05T15:00:00Z",
  "notes": "Interview with the engineering team."
}
~~~

Expect 200. To change only the status, PATCH /api/applications/{id}/status:

~~~json
{
  "status": "Offer"
}
~~~

Valid statuses: Saved, Applied, Assessment, Interview, Offer, Rejected, Withdrawn.
JSON uses names rather than numbers. Unknown statuses or an empty PATCH body
return 400. PATCH leaves other fields unchanged.

An unknown JobId returns 400 because jobs are local to this service.
At the original Stage 2 checkpoint, positive but unknown UserId values were
accepted. With Stage 3 now implemented, User Service must confirm the user exists;
unknown users return 400 and unavailable User Service returns 503.
No API reads another service's database. See stage-3.md for details.

## 4. Create and complete a reminder

Open http://localhost:5003/swagger and POST /api/reminders:

~~~json
{
  "userId": 1,
  "applicationId": 1,
  "title": "Prepare for interview",
  "dueDate": "2026-11-04T15:00:00Z",
  "completed": false
}
~~~

Expect 201. GET /api/reminders, GET /api/reminders/{id}, and
GET /api/reminders/user/{userId} should return the matching records.

To complete it, PUT /api/reminders/{id} with the full body and completed true.
Expect 200. Missing dueDate, a blank title, or non-positive reference IDs return
400. Stage 3 now also validates the application and its owner through HTTP:
missing applications or mismatched users return 400; an unavailable application
API returns 503.

Timestamps must include Z for UTC, such as 15:00 UTC. For a real interview, convert
your local time to UTC first. Calendar dates (graduationDate/datePosted) use
YYYY-MM-DD without a time. There is no notification delivery in this stage.

## 5. Check persistence and independent APIs

Stop and restart all three APIs. GET the records by ID: they should still exist,
because SQLite stores them on disk.

Stop User Service and read an application. It still works. Stop Job Application
Service and read a reminder. It still works. Each API reads only its own database.

## 6. Check deletion and missing records

Before deleting your application, try DELETE /api/jobs/{jobId}. Expect 409 because
an application still references that job.

Then delete in this order:

1. DELETE /api/reminders/{id} on port 5003.
2. DELETE /api/applications/{id} on port 5002.
3. DELETE /api/jobs/{id} on port 5002.
4. DELETE /api/users/{id} on port 5001.

Each successful deletion returns 204 with no response body. GET, PUT, or DELETE
using a positive ID that does not exist returns 404. A list with no matching rows
returns 200 with [].

Deletion does not cascade across services. Following the cleanup order prevents
orphaned references during this learning stage.

## What to understand before continuing

- Controller: turns an HTTP request into a service call and chooses the response.
- DTO: describes the JSON the caller sends or receives.
- Entity: represents a stored database record.
- Interface: describes methods without choosing their implementation.
- Service: performs the operation and applies local rules.
- Dependency injection: supplies each controller's service and each service's context.
- DbContext: EF Core's connection to that service's database and tracked changes.
- Migration: versioned instructions for creating or changing database tables.

HTTP validation is now implemented. Continue with the stage-3.md walkthrough.
