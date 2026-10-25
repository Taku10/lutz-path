param(
    [ValidateRange(1024, 65532)]
    [int]$BasePort = 5300
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = 'lutzpath-ui-' + [guid]::NewGuid().ToString('N').Substring(0, 10)
$composeArguments = @('--project-name', $project, '--file', (Join-Path $root 'docker-compose.yml'))
$runDirectory = Join-Path $root ('.artifacts/' + $project)
New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
$oldEnvironment = @($env:FRONTEND_PORT, $env:USER_SERVICE_PORT, $env:JOB_APPLICATION_SERVICE_PORT, $env:REMINDER_SERVICE_PORT, $env:LUTZPATH_UI_URL)
$env:FRONTEND_PORT = [string]$BasePort
$env:USER_SERVICE_PORT = [string]($BasePort + 1)
$env:JOB_APPLICATION_SERVICE_PORT = [string]($BasePort + 2)
$env:REMINDER_SERVICE_PORT = [string]($BasePort + 3)
$env:LUTZPATH_UI_URL = "http://127.0.0.1:$BasePort"
$started = $false

function Invoke-Compose {
    & docker compose @composeArguments @args
    if ($LASTEXITCODE -ne 0) { throw "Compose failed: $args" }
}

try {
    foreach ($port in @($BasePort, ($BasePort + 1), ($BasePort + 2), ($BasePort + 3))) {
        $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
        try { $listener.Start() } finally { $listener.Stop() }
    }
    $started = $true
    Invoke-Compose up --build --detach
    foreach ($offset in @(1, 2, 3)) {
        $ready = $false
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            try {
                $health = Invoke-RestMethod "http://127.0.0.1:$($BasePort + $offset)/health" -TimeoutSec 2
                if ($health.status -eq 'healthy') { $ready = $true; break }
            } catch { }
            Start-Sleep -Milliseconds 500
        }
        if (-not $ready) { throw "API $offset did not become ready." }
    }
    $page = Invoke-WebRequest "$env:LUTZPATH_UI_URL/" -UseBasicParsing -TimeoutSec 10
    if ($page.StatusCode -ne 200) { throw 'Frontend did not become ready.' }
    Push-Location (Join-Path $root 'frontend')
    try {
        npm.cmd run check
        if ($LASTEXITCODE -ne 0) { throw 'JavaScript syntax check failed.' }
        npm.cmd run test:ui
        if ($LASTEXITCODE -ne 0) { throw 'Browser checks failed; inspect frontend/test-results.' }
    } finally { Pop-Location }
} finally {
    if ($started) {
        & docker compose @composeArguments logs --no-color 2>&1 | Set-Content -LiteralPath "$runDirectory/compose.log"
        & docker compose @composeArguments down
    }
    @(
        "Project: $project"
        'Test containers/network removed; test volumes retained.'
        "Optional test volume cleanup: docker compose --project-name $project --file docker-compose.yml down --volumes"
    ) | Set-Content -LiteralPath "$runDirectory/run-info.txt"
    $env:FRONTEND_PORT = $oldEnvironment[0]
    $env:USER_SERVICE_PORT = $oldEnvironment[1]
    $env:JOB_APPLICATION_SERVICE_PORT = $oldEnvironment[2]
    $env:REMINDER_SERVICE_PORT = $oldEnvironment[3]
    $env:LUTZPATH_UI_URL = $oldEnvironment[4]
}
