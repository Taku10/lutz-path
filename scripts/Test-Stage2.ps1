param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$IncludeCommunicationChecks
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$runDirectory = Join-Path $root ('.artifacts/stage2-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$names = @('UserService', 'JobApplicationService', 'ReminderService')
$processes = @{}
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$oldConnection = $env:ConnectionStrings__DefaultConnection
$oldUserUrl = $env:Services__UserService
$oldApplicationUrl = $env:Services__JobApplicationService
$script:checks = 0

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Start-Api([string]$name) {
    $index = [array]::IndexOf($names, $name)
    $port = 5101 + $index
    $projectDirectory = Join-Path $root "src/LutzPath.$name"
    $dll = Join-Path $projectDirectory "bin/$Configuration/net10.0/LutzPath.$name.dll"
    if (-not (Test-Path -LiteralPath $dll)) { throw "Build $Configuration first: dotnet build LutzPath.sln -c $Configuration" }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Services__UserService = 'http://localhost:5101/'
    $env:Services__JobApplicationService = 'http://localhost:5102/'
    $env:ConnectionStrings__DefaultConnection = "Data Source=$runDirectory/$name.db"
    $process = Start-Process dotnet -ArgumentList @(
        ('"' + $dll + '"'), '--urls', "http://localhost:$port",
        '--contentRoot', ('"' + $projectDirectory + '"')
    ) -WindowStyle Hidden -PassThru -RedirectStandardOutput "$runDirectory/$name.stdout.log" -RedirectStandardError "$runDirectory/$name.stderr.log"
    $processes[$name] = $process
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        if ($process.HasExited) { throw "$name exited. See logs in $runDirectory" }
        try {
            $health = Invoke-RestMethod "http://localhost:$port/health" -TimeoutSec 2
            if ($health.service -eq $name) { return }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw "$name did not become ready. See logs in $runDirectory"
}

function Stop-Api([string]$name) {
    $process = $processes[$name]
    if ($null -ne $process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id
        $process.WaitForExit()
    }
    $processes.Remove($name)
}

function Invoke-Api([string]$method, [int]$port, [string]$path, $body = $null, [int]$expected = 200) {
    $arguments = @{
        Uri = "http://localhost:$port$path"
        Method = $method
        UseBasicParsing = $true
        TimeoutSec = 15
    }
    if ($null -ne $body) {
        $arguments.Body = ConvertTo-Json -InputObject $body -Depth 10
        $arguments.ContentType = 'application/json'
    }
    try {
        $response = Invoke-WebRequest @arguments
    } catch {
        if ($null -eq $_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
        Assert-True ($status -eq $expected) "$method $path expected $expected but returned $status. $_"
        return
    }
    Assert-True ([int]$response.StatusCode -eq $expected) "$method $path expected $expected but returned $($response.StatusCode)"
    if ($expected -eq 201) {
        Assert-True (-not [string]::IsNullOrWhiteSpace($response.Headers['Location'])) "$path is missing its Location header"
    }
    if ($response.Content) { $parsed = ConvertFrom-Json -InputObject $response.Content; return $parsed }
}

try {
    foreach ($port in @(5101, 5102, 5103)) {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
        try { $listener.Start() } finally { $listener.Stop() }
    }
    foreach ($name in $names) { Start-Api $name }

    foreach ($port in @(5101, 5102, 5103)) {
        $document = Invoke-Api GET $port '/swagger/v1/swagger.json'
        Assert-True ($null -ne $document.paths.'/health'.get) 'Health endpoint absent from Swagger'
        $ui = Invoke-WebRequest "http://localhost:$port/swagger/index.html" -UseBasicParsing
        Assert-True ($ui.StatusCode -eq 200) 'Swagger UI unavailable'
    }

    $userBody = @{ firstName = 'Taylor'; lastName = 'Student'; email = 'taylor@example.com'; graduationDate = '2027-05-15' }
    $user = Invoke-Api POST 5101 '/api/users' $userBody 201
    Assert-True ($user.id -gt 0) 'User ID was not generated'
    $readUser = Invoke-Api GET 5101 "/api/users/$($user.id)"
    Assert-True ($readUser.email -eq $userBody.email) 'User was not saved'
    $users = @(Invoke-Api GET 5101 '/api/users')
    Assert-True ($users.Count -eq 1) 'Unexpected users list'
    $userBody.firstName = 'Taylor Updated'
    $updatedUser = Invoke-Api PUT 5101 "/api/users/$($user.id)" $userBody
    Assert-True ($updatedUser.firstName -eq 'Taylor Updated') 'User update failed'
    Invoke-Api POST 5101 '/api/users' @{ firstName = ' '; lastName = 'Student'; email = 'invalid' } 400

    $jobBody = @{ company = 'Example Company'; title = 'Graduate Developer'; location = 'Chicago'; jobUrl = 'https://example.com/jobs/1'; description = 'Entry-level C# role'; datePosted = '2026-10-24' }
    $job = Invoke-Api POST 5102 '/api/jobs' $jobBody 201
    $readJob = Invoke-Api GET 5102 "/api/jobs/$($job.id)"
    Assert-True ($readJob.title -eq $jobBody.title) 'Job was not saved'
    $jobs = @(Invoke-Api GET 5102 '/api/jobs')
    Assert-True ($jobs.Count -eq 1) 'Unexpected jobs list'
    $jobBody.location = 'Remote'
    $updatedJob = Invoke-Api PUT 5102 "/api/jobs/$($job.id)" $jobBody
    Assert-True ($updatedJob.location -eq 'Remote') 'Job update failed'
    Invoke-Api POST 5102 '/api/jobs' @{ company = ''; title = ''; jobUrl = 'invalid' } 400

    $applicationBody = @{ userId = $user.id; jobId = $job.id; status = 'Applied'; dateApplied = '2026-10-24T15:00:00Z'; interviewDate = $null; notes = 'Initial application' }
    $application = Invoke-Api POST 5102 '/api/applications' $applicationBody 201
    Assert-True ($application.status -eq 'Applied') 'Status was not returned as a string'
    $readApplication = Invoke-Api GET 5102 "/api/applications/$($application.id)"
    Assert-True ($readApplication.dateApplied.EndsWith('Z')) 'UTC timestamp was not preserved'
    $applications = @(Invoke-Api GET 5102 '/api/applications')
    Assert-True ($applications.Count -eq 1) 'Unexpected applications list'
    $byUser = @(Invoke-Api GET 5102 "/api/applications/user/$($user.id)")
    Assert-True ($byUser.Count -eq 1) 'Application user filter failed'
    $none = @(Invoke-Api GET 5102 '/api/applications/user/999999')
    Assert-True ($none.Count -eq 0) 'Application filter returned another user data'
    $applicationBody.interviewDate = '2026-11-05T15:00:00Z'
    $applicationBody.notes = 'Interview booked'
    $updatedApplication = Invoke-Api PUT 5102 "/api/applications/$($application.id)" $applicationBody
    Assert-True ($updatedApplication.notes -eq 'Interview booked') 'Application update failed'
    $patched = Invoke-Api PATCH 5102 "/api/applications/$($application.id)/status" @{ status = 'Interview' }
    Assert-True ($patched.status -eq 'Interview') 'Status patch failed'
    Invoke-Api PATCH 5102 "/api/applications/$($application.id)/status" @{ status = 'Unknown' } 400
    Invoke-Api PATCH 5102 "/api/applications/$($application.id)/status" @{ status = 3 } 400
    Invoke-Api PATCH 5102 "/api/applications/$($application.id)/status" @{} 400
    Invoke-Api POST 5102 '/api/applications' @{ userId = $user.id; jobId = 999999; status = 'Saved' } 400
    Invoke-Api POST 5102 '/api/applications' @{ userId = 0; jobId = $job.id } 400
    Invoke-Api POST 5102 '/api/applications' @{ userId = $user.id; jobId = $job.id; interviewDate = '2026-11-05T15:00:00' } 400
    Invoke-Api DELETE 5102 "/api/jobs/$($job.id)" $null 409

    $reminderBody = @{ userId = $user.id; applicationId = $application.id; title = 'Prepare for interview'; dueDate = '2026-11-04T15:00:00Z'; completed = $false }
    $reminder = Invoke-Api POST 5103 '/api/reminders' $reminderBody 201
    $readReminder = Invoke-Api GET 5103 "/api/reminders/$($reminder.id)"
    Assert-True ($readReminder.dueDate.EndsWith('Z')) 'Reminder UTC timestamp was not preserved'
    $reminders = @(Invoke-Api GET 5103 '/api/reminders')
    Assert-True ($reminders.Count -eq 1) 'Unexpected reminders list'
    $byUser = @(Invoke-Api GET 5103 "/api/reminders/user/$($user.id)")
    Assert-True ($byUser.Count -eq 1) 'Reminder user filter failed'
    $none = @(Invoke-Api GET 5103 '/api/reminders/user/999999')
    Assert-True ($none.Count -eq 0) 'Reminder filter returned another user data'
    $reminderBody.completed = $true
    $updatedReminder = Invoke-Api PUT 5103 "/api/reminders/$($reminder.id)" $reminderBody
    Assert-True $updatedReminder.completed 'Reminder completion update failed'
    Invoke-Api POST 5103 '/api/reminders' @{ userId = 0; applicationId = 0; title = '' } 400
    Invoke-Api POST 5103 '/api/reminders' @{ userId = 1; applicationId = 1; title = 'Missing date' } 400
    Invoke-Api POST 5103 '/api/reminders' @{ userId = 1; applicationId = 1; title = 'Local time'; dueDate = '2026-11-04T15:00:00' } 400

    foreach ($entry in @(
        @{ port = 5101; path = '/api/users'; body = $userBody },
        @{ port = 5102; path = '/api/jobs'; body = $jobBody },
        @{ port = 5102; path = '/api/applications'; body = $applicationBody },
        @{ port = 5103; path = '/api/reminders'; body = $reminderBody }
    )) {
        Invoke-Api GET $entry.port "$($entry.path)/999999" $null 404
        Invoke-Api PUT $entry.port "$($entry.path)/999999" $entry.body 404
        Invoke-Api DELETE $entry.port "$($entry.path)/999999" $null 404
    }
    Invoke-Api PATCH 5102 '/api/applications/999999/status' @{ status = 'Offer' } 404

    if ($IncludeCommunicationChecks) {
        Invoke-Api POST 5102 '/api/applications' @{ userId = 999999; jobId = $job.id; status = 'Applied' } 400
        $invalidUserUpdate = @{
            userId = 999999; jobId = $job.id; status = 'Withdrawn'
            dateApplied = $null; interviewDate = $null; notes = 'Must not be saved'
        }
        Invoke-Api PUT 5102 "/api/applications/$($application.id)" $invalidUserUpdate 400
        $unchangedApplication = Invoke-Api GET 5102 "/api/applications/$($application.id)"
        Assert-True ($unchangedApplication.userId -eq $user.id -and $unchangedApplication.notes -eq 'Interview booked') 'Invalid-user update changed an application'
        Invoke-Api POST 5103 '/api/reminders' @{
            userId = $user.id; applicationId = 999999; title = 'Unknown application'; dueDate = '2026-11-04T15:00:00Z'
        } 400
        $otherUser = Invoke-Api POST 5101 '/api/users' @{
            firstName = 'Other'; lastName = 'Student'; email = 'other@example.com'
        } 201
        $wrongOwner = @{
            userId = $otherUser.id; applicationId = $application.id
            title = 'Wrong owner'; dueDate = '2026-11-04T15:00:00Z'; completed = $false
        }
        Invoke-Api POST 5103 '/api/reminders' $wrongOwner 400
        Invoke-Api PUT 5103 "/api/reminders/$($reminder.id)" $wrongOwner 400
        $unchangedReminder = Invoke-Api GET 5103 "/api/reminders/$($reminder.id)"
        Assert-True ($unchangedReminder.userId -eq $user.id -and $unchangedReminder.completed) 'Invalid reminder update changed the database'
        Invoke-Api DELETE 5101 "/api/users/$($otherUser.id)" $null 204
        $allApplications = @(Invoke-Api GET 5102 '/api/applications')
        $allReminders = @(Invoke-Api GET 5103 '/api/reminders')
        Assert-True ($allApplications.Count -eq 1 -and $allReminders.Count -eq 1) 'Rejected creates left database records'
        Write-Output 'PASS: HTTP existence and ownership validation; rejected writes left data unchanged.'
    }

    # All records must survive process restarts, proving this is SQLite persistence.
    foreach ($name in $names) { Stop-Api $name }
    foreach ($name in $names) { Start-Api $name }
    $persistedUser = Invoke-Api GET 5101 "/api/users/$($user.id)"
    Assert-True ($persistedUser.firstName -eq 'Taylor Updated') 'User did not survive restart'
    $persistedJob = Invoke-Api GET 5102 "/api/jobs/$($job.id)"
    Assert-True ($persistedJob.location -eq 'Remote') 'Job did not survive restart'
    $persistedApplication = Invoke-Api GET 5102 "/api/applications/$($application.id)"
    Assert-True ($persistedApplication.status -eq 'Interview') 'Application did not survive restart'
    $persistedReminder = Invoke-Api GET 5103 "/api/reminders/$($reminder.id)"
    Assert-True $persistedReminder.completed 'Reminder did not survive restart'

    Stop-Api 'UserService'
    $null = Invoke-Api GET 5102 "/api/applications/$($application.id)"
    if ($IncludeCommunicationChecks) {
        Invoke-Api POST 5102 '/api/applications' $applicationBody 503
        $failedUpdate = @{
            userId = $user.id; jobId = $job.id; status = 'Withdrawn'
            notes = 'Must not be saved'
        }
        Invoke-Api PUT 5102 "/api/applications/$($application.id)" $failedUpdate 503
        $unchanged = Invoke-Api GET 5102 "/api/applications/$($application.id)"
        Assert-True ($unchanged.status -eq 'Interview' -and $unchanged.notes -eq 'Interview booked') 'Unavailable user dependency changed an application'
        $all = @(Invoke-Api GET 5102 '/api/applications')
        Assert-True ($all.Count -eq 1) 'Unavailable user dependency created an application'
        $null = Invoke-Api PATCH 5102 "/api/applications/$($application.id)/status" @{ status = 'Interview' }

        # Reminder validation only calls the application API, which reads locally.
        $extraReminder = Invoke-Api POST 5103 '/api/reminders' $reminderBody 201
        Invoke-Api DELETE 5103 "/api/reminders/$($extraReminder.id)" $null 204
        Write-Output 'PASS: User Service outage returns 503 without writes; local reads and status updates work.'
    }

    Stop-Api 'JobApplicationService'
    $null = Invoke-Api GET 5103 "/api/reminders/$($reminder.id)"
    if ($IncludeCommunicationChecks) {
        Invoke-Api POST 5103 '/api/reminders' $reminderBody 503
        $failedReminderUpdate = @{
            userId = $user.id; applicationId = $application.id
            title = 'Must not be saved'; dueDate = '2026-11-04T15:00:00Z'; completed = $false
        }
        Invoke-Api PUT 5103 "/api/reminders/$($reminder.id)" $failedReminderUpdate 503
        $unchanged = Invoke-Api GET 5103 "/api/reminders/$($reminder.id)"
        Assert-True ($unchanged.title -eq 'Prepare for interview' -and $unchanged.completed) 'Unavailable application dependency changed a reminder'
        $all = @(Invoke-Api GET 5103 '/api/reminders')
        Assert-True ($all.Count -eq 1) 'Unavailable application dependency created a reminder'
        Write-Output 'PASS: Job Application Service outage returns 503 without writes; reminder reads work.'
    }
    Start-Api 'UserService'
    Start-Api 'JobApplicationService'
    if ($IncludeCommunicationChecks) {
        $recoveredApplication = Invoke-Api POST 5102 '/api/applications' $applicationBody 201
        Invoke-Api DELETE 5102 "/api/applications/$($recoveredApplication.id)" $null 204
        $recoveredReminder = Invoke-Api POST 5103 '/api/reminders' $reminderBody 201
        Invoke-Api DELETE 5103 "/api/reminders/$($recoveredReminder.id)" $null 204
        Write-Output 'PASS: Writes recover after dependencies restart.'
    }

    foreach ($entry in @(
        @{ port = 5103; path = "/api/reminders/$($reminder.id)" },
        @{ port = 5102; path = "/api/applications/$($application.id)" },
        @{ port = 5102; path = "/api/jobs/$($job.id)" },
        @{ port = 5101; path = "/api/users/$($user.id)" }
    )) {
        Invoke-Api DELETE $entry.port $entry.path $null 204
        Invoke-Api GET $entry.port $entry.path $null 404
    }
    Write-Output "PASS: $script:checks checks covering CRUD, validation, Swagger, persistence, and independent reads."
    Write-Output "Isolated databases and logs: $runDirectory"
} finally {
    foreach ($name in $names) { Stop-Api $name }
    $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
    $env:ConnectionStrings__DefaultConnection = $oldConnection
    $env:Services__UserService = $oldUserUrl
    $env:Services__JobApplicationService = $oldApplicationUrl
}








