param(
    [ValidateRange(1024, 65532)]
    [int]$BasePort = 5201
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = 'lutzpath-stage4-' + [guid]::NewGuid().ToString('N').Substring(0, 10)
$runDirectory = Join-Path $root ('.artifacts/' + $project)
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$composeArguments = @('--project-name', $project, '--file', (Join-Path $root 'docker-compose.yml'))
$services = @('userservice', 'jobapplicationservice', 'reminderservice')
$oldPorts = @($env:USER_SERVICE_PORT, $env:JOB_APPLICATION_SERVICE_PORT, $env:REMINDER_SERVICE_PORT, $env:FRONTEND_PORT)
$env:USER_SERVICE_PORT = [string]$BasePort
$env:JOB_APPLICATION_SERVICE_PORT = [string]($BasePort + 1)
$env:REMINDER_SERVICE_PORT = [string]($BasePort + 2)
$env:FRONTEND_PORT = [string]($BasePort + 3)
$script:checks = 0
$composeStarted = $false

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
    $script:checks++
}

function Invoke-Compose {
    & docker compose @composeArguments @args
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed: $args" }
}

function Wait-Api([int]$port, [string]$expectedService) {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$port/health" -TimeoutSec 2
            if ($health.status -eq 'healthy' -and $health.service -eq $expectedService) { return }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    throw "$expectedService was not ready on port $port."
}

function Wait-AllApis {
    Wait-Api $BasePort 'UserService'
    Wait-Api ($BasePort + 1) 'JobApplicationService'
    Wait-Api ($BasePort + 2) 'ReminderService'
}

function Invoke-Api([string]$method, [int]$port, [string]$path, $body = $null, [int]$expected = 200) {
    $arguments = @{
        Uri = "http://127.0.0.1:$port$path"
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
    if ($response.Content) {
        $parsed = ConvertFrom-Json -InputObject $response.Content
        return $parsed
    }
}

try {
    # Do not stop an existing API to obtain a test port.
    foreach ($port in @($BasePort, ($BasePort + 1), ($BasePort + 2))) {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
        try { $listener.Start() } finally { $listener.Stop() }
    }

    Invoke-Compose config --quiet
    $composeStarted = $true
    Invoke-Compose up --build --detach
    Wait-AllApis
    Write-Output 'PASS: all three container APIs respond.'

    $userPort = $BasePort
    $applicationPort = $BasePort + 1
    $reminderPort = $BasePort + 2

    for ($index = 0; $index -lt $services.Count; $index++) {
        $id = (Invoke-Compose ps --quiet $services[$index]).Trim()
        $inspection = & docker inspect $id | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $id" }
        $container = @($inspection)[0]
        Assert-True ($container.Config.User -ne '' -and $container.Config.User -ne 'root' -and $container.Config.User -ne '0') 'API is not running as a non-root user'
        $mounts = @($container.Mounts | Where-Object { $_.Destination -eq '/data' })
        Assert-True ($mounts.Count -eq 1 -and $mounts[0].Type -eq 'volume') 'SQLite directory is not a named volume'
        $expectedVolume = @('users-data', 'applications-data', 'reminders-data')[$index]
        Assert-True ($mounts[0].Name -eq ($project + '_' + $expectedVolume)) 'A service is using the wrong SQLite volume'
        $doc = Invoke-Api GET ($BasePort + $index) '/swagger/v1/swagger.json'
        Assert-True ($null -ne $doc.paths.'/health'.get) 'OpenAPI health operation is missing'
        $ui = Invoke-WebRequest "http://127.0.0.1:$($BasePort + $index)/swagger/index.html" -UseBasicParsing
        Assert-True ($ui.StatusCode -eq 200) 'Swagger UI is unavailable'
    }

    $user = Invoke-Api POST $userPort '/api/users' @{
        firstName = 'Taylor'; lastName = 'Container'; email = 'container@example.com'; graduationDate = '2027-05-15'
    } 201
    $job = Invoke-Api POST $applicationPort '/api/jobs' @{
        company = 'Example Company'; title = 'Graduate Developer'; location = 'Chicago'
        jobUrl = 'https://example.com/jobs/1'; description = 'Container workflow'; datePosted = '2026-10-24'
    } 201
    $applicationBody = @{
        userId = $user.id; jobId = $job.id; status = 'Applied'
        dateApplied = '2026-10-24T15:00:00Z'; interviewDate = $null; notes = 'Created in Docker'
    }
    $application = Invoke-Api POST $applicationPort '/api/applications' $applicationBody 201
    Assert-True ($application.userId -eq $user.id) 'Application was not created through the user lookup'
    $applicationBody.status = 'Interview'
    $applicationBody.interviewDate = '2026-11-05T15:00:00Z'
    $applicationBody.notes = 'Interview booked in Docker'
    $updated = Invoke-Api PUT $applicationPort "/api/applications/$($application.id)" $applicationBody
    Assert-True ($updated.status -eq 'Interview' -and $updated.interviewDate.EndsWith('Z')) 'Interview update failed'

    $reminderBody = @{
        userId = $user.id; applicationId = $application.id; title = 'Prepare for interview'
        dueDate = '2026-11-04T15:00:00Z'; completed = $false
    }
    $reminder = Invoke-Api POST $reminderPort '/api/reminders' $reminderBody 201
    $reminderBody.completed = $true
    $updated = Invoke-Api PUT $reminderPort "/api/reminders/$($reminder.id)" $reminderBody
    Assert-True $updated.completed 'Reminder completion failed'
    Invoke-Api POST $applicationPort '/api/applications' @{ userId = 999999; jobId = $job.id; status = 'Applied' } 400
    Invoke-Api POST $reminderPort '/api/reminders' @{
        userId = $user.id; applicationId = 999999; title = 'Missing application'; dueDate = '2026-11-04T15:00:00Z'
    } 400
    Invoke-Api DELETE $applicationPort "/api/jobs/$($job.id)" $null 409
    Write-Output 'PASS: user -> job -> application -> interview -> reminder workflow over Docker service-name HTTP.'

    # Remove containers and their writable layers, keeping named volumes.
    Invoke-Compose down
    Invoke-Compose up --detach --no-build
    Wait-AllApis
    $storedUser = Invoke-Api GET $userPort "/api/users/$($user.id)"
    $storedJob = Invoke-Api GET $applicationPort "/api/jobs/$($job.id)"
    $storedApplication = Invoke-Api GET $applicationPort "/api/applications/$($application.id)"
    $storedReminder = Invoke-Api GET $reminderPort "/api/reminders/$($reminder.id)"
    Assert-True ($storedUser.email -eq 'container@example.com') 'User did not survive container replacement'
    Assert-True ($storedJob.title -eq 'Graduate Developer') 'Job did not survive container replacement'
    Assert-True ($storedApplication.notes -eq 'Interview booked in Docker' -and $storedApplication.status -eq 'Interview') 'Application did not survive container replacement'
    Assert-True $storedReminder.completed 'Reminder did not survive container replacement'
    Write-Output 'PASS: all records survive docker compose down/up container replacement.'

    Invoke-Compose stop userservice
    Invoke-Api POST $applicationPort '/api/applications' $applicationBody 503
    $null = Invoke-Api GET $applicationPort "/api/applications/$($application.id)"
    $all = @(Invoke-Api GET $applicationPort '/api/applications')
    Assert-True ($all.Count -eq 1) 'Failed application request wrote data'
    $null = Invoke-Api PATCH $applicationPort "/api/applications/$($application.id)/status" @{ status = 'Interview' }
    Invoke-Compose start userservice
    Wait-Api $userPort 'UserService'
    $recovered = Invoke-Api POST $applicationPort '/api/applications' $applicationBody 201
    Invoke-Api DELETE $applicationPort "/api/applications/$($recovered.id)" $null 204

    Invoke-Compose stop jobapplicationservice
    Invoke-Api POST $reminderPort '/api/reminders' $reminderBody 503
    $null = Invoke-Api GET $reminderPort "/api/reminders/$($reminder.id)"
    $all = @(Invoke-Api GET $reminderPort '/api/reminders')
    Assert-True ($all.Count -eq 1) 'Failed reminder request wrote data'
    Invoke-Compose start jobapplicationservice
    Wait-Api $applicationPort 'JobApplicationService'
    $recovered = Invoke-Api POST $reminderPort '/api/reminders' $reminderBody 201
    Invoke-Api DELETE $reminderPort "/api/reminders/$($recovered.id)" $null 204
    Write-Output 'PASS: dependency outages return 503, local reads work, and writes recover.'

    foreach ($entry in @(
        @{ port = $reminderPort; path = "/api/reminders/$($reminder.id)" },
        @{ port = $applicationPort; path = "/api/applications/$($application.id)" },
        @{ port = $applicationPort; path = "/api/jobs/$($job.id)" },
        @{ port = $userPort; path = "/api/users/$($user.id)" }
    )) {
        Invoke-Api DELETE $entry.port $entry.path $null 204
        Invoke-Api GET $entry.port $entry.path $null 404
    }
    Write-Output "PASS: $script:checks container checks."
} finally {
    if ($composeStarted) {
        & docker compose @composeArguments logs --no-color 2>&1 | Set-Content -LiteralPath "$runDirectory/compose.log"
        & docker compose @composeArguments down
        if ($LASTEXITCODE -ne 0) { Write-Warning "Cleanup failed for test project $project. Inspect it in Docker Desktop." }
    }
    @(
        "Project: $project"
        "Logs: $runDirectory/compose.log"
        'Test containers are removed. Named test volumes are retained.'
        "Optional removal of ONLY this test project's volumes:"
        "docker compose --project-name $project --file docker-compose.yml down --volumes"
    ) | Set-Content -LiteralPath "$runDirectory/run-info.txt"
    $env:USER_SERVICE_PORT = $oldPorts[0]
    $env:JOB_APPLICATION_SERVICE_PORT = $oldPorts[1]
    $env:REMINDER_SERVICE_PORT = $oldPorts[2]
    $env:FRONTEND_PORT = $oldPorts[3]
    Write-Output "Test logs and project details: $runDirectory"
}
