param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-Stage2.ps1') -Configuration $Configuration -IncludeCommunicationChecks
