param(
    [string]$WorkbookPath = 'C:\Users\Takunda Madziwa\Downloads\Visa-Friendly New Grad Job Tracker.xlsx',
    [int]$UserId = 1,
    [string]$ApiBase = 'http://localhost:3000/api',
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$hash = (Get-FileHash -LiteralPath $WorkbookPath -Algorithm SHA256).Hash.ToLowerInvariant()
$directory = Join-Path $root ('.artifacts/imports/sheet1-' + $hash.Substring(0,16) + '-user' + $UserId)
New-Item -ItemType Directory -Force -Path $directory | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($WorkbookPath)
function Read-XmlEntry([string]$name) {
    $reader = [IO.StreamReader]::new($zip.GetEntry($name).Open())
    try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}
try {
    $shared = Read-XmlEntry 'xl/sharedStrings.xml'
    $strings = @($shared.SelectNodes('//*[local-name()="si"]') | ForEach-Object {
        ($_.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join ''
    })
    $workbook = Read-XmlEntry 'xl/workbook.xml'
    $sheet = $workbook.SelectSingleNode('//*[local-name()="sheet" and @name="Sheet1"]')
    if (!$sheet) { throw 'Sheet1 was not found.' }
    $relationship = $sheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
    $rels = Read-XmlEntry 'xl/_rels/workbook.xml.rels'
    $target = $rels.SelectSingleNode('//*[local-name()="Relationship" and @Id="' + $relationship + '"]').Target
    $sheetXml = Read-XmlEntry ('xl/' + $target.TrimStart('/'))
    $rows = @($sheetXml.SelectNodes('//*[local-name()="sheetData"]/*[local-name()="row"]') | ForEach-Object {
        $record = [ordered]@{ Row = [int]$_.r }
        foreach ($cell in $_.SelectNodes('./*[local-name()="c"]')) {
            $column = $cell.r -replace '\d',''
            $valueNode = $cell.SelectSingleNode('./*[local-name()="v"]')
            $value = if ($null -eq $valueNode) { '' } else { $valueNode.InnerText }
            if ($cell.t -eq 's' -and $value -ne '') { $value = $strings[[int]$value] }
            elseif ($cell.t -eq 'inlineStr') { $value = ($cell.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join '' }
            $record[$column] = $value
        }
        [pscustomobject]$record
    })
} finally { $zip.Dispose() }
$header = $rows[0]
if ($header.B -ne 'City' -or $header.C -ne 'Role' -or $header.Q -ne 'Posting Availability') { throw 'Unexpected Sheet1 headers; review the mapping before importing.' }
$unassigned = @($rows | Where-Object { $_.Row -gt 1 -and !$_.A -and !$_.C -and @($_.PSObject.Properties | Where-Object { $_.Name -ne 'Row' -and $_.Value }).Count -gt 0 })
ConvertTo-Json -InputObject $unassigned -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $directory 'unassigned-rows.json')
$dataRows = @($rows | Where-Object { $_.Row -gt 1 -and ($_.A -or $_.C) })
$culture = [Globalization.CultureInfo]::GetCultureInfo('en-US')
$central = [TimeZoneInfo]::FindSystemTimeZoneById('Central Standard Time')
function Text-OrNull($value) { if ([string]::IsNullOrWhiteSpace([string]$value)) { return $null }; return ([string]$value).Trim() }
function Read-Date($value) {
    if ([string]::IsNullOrWhiteSpace([string]$value)) { return $null }
    $number = 0.0
    if ([double]::TryParse([string]$value, [Globalization.NumberStyles]::Float, $culture, [ref]$number)) { return [DateTime]::FromOADate($number).Date }
    return [DateTime]::Parse([string]$value, $culture).Date
}
function Date-Utc($value) {
    $date = Read-Date $value
    if ($null -eq $date) { return $null }
    return [TimeZoneInfo]::ConvertTimeToUtc([DateTime]::SpecifyKind($date.AddHours(12), [DateTimeKind]::Unspecified), $central).ToString('o')
}
function Check-Time($value) {
    if (!$value) { return $null }
    if ([string]$value -match '^(?<stamp>(?:\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{4})\s+\d{1,2}:\d{2}(?::\d{2})?\s*(?:AM|PM)?)\s+(?<zone>CDT|CST|CT)\b') {
        $date = [DateTime]::SpecifyKind([DateTime]::Parse($Matches.stamp, $culture), [DateTimeKind]::Unspecified)
        if ($Matches.zone -eq 'CDT') { return [DateTimeOffset]::new($date,[TimeSpan]::FromHours(-5)).UtcDateTime.ToString('o') }
        if ($Matches.zone -eq 'CST') { return [DateTimeOffset]::new($date,[TimeSpan]::FromHours(-6)).UtcDateTime.ToString('o') }
        return [TimeZoneInfo]::ConvertTimeToUtc($date, $central).ToString('o')
    }
    return $null
}
$statusMap = @{ 'Not Applied'='NotApplied'; 'Saved'='Saved'; 'Applied'='Applied'; 'OA / Assessment'='Assessment'; 'Interviewing'='Interview'; 'Offer'='Offer'; 'Rejected'='Rejected'; 'Withdrawn'='Withdrawn' }
$availabilityMap = @{ 'Open'='Open'; 'Closed'='Closed'; 'Unknown / Recheck'='Unknown'; ''='Unknown' }
$warnings = [Collections.Generic.List[string]]::new()
foreach ($row in $unassigned) { $warnings.Add("Row $($row.Row): no company or role; original cells retained in unassigned-rows.json.") }
$payloads = @(
foreach ($row in $dataRows) {
    if (!$row.A -or !$row.C) { throw "Row $($row.Row): missing company or role." }
    if (!$statusMap.ContainsKey([string]$row.G)) { throw "Row $($row.Row): unknown status $($row.G)" }
    if (!$availabilityMap.ContainsKey([string]$row.Q)) { throw "Row $($row.Row): unknown availability $($row.Q)" }
    $sourceNotes = [Collections.Generic.List[string]]::new()
    if ($row.S) { $sourceNotes.Add('Unlabelled source column S: ' + $row.S) }
    $applied = Read-Date $row.I
    if ($applied -and $applied -gt (Get-Date).Date) {
        $warning = "Row $($row.Row): future applied date $($applied.ToString('yyyy-MM-dd')); original cell: $($row.I)"
        $warnings.Add($warning); $sourceNotes.Add($warning)
    }
    $checkTime = Check-Time $row.R
    if ($row.R -and !$checkTime) { $warnings.Add("Row $($row.Row): availability timestamp retained as evidence text; no timestamp inferred.") }
    $found = Read-Date $row.H
    $job = [ordered]@{
        company = (Text-OrNull $row.A); title = (Text-OrNull $row.C); location = (Text-OrNull $row.B)
        jobUrl = (Text-OrNull $row.F); description = $null; datePosted = $null
        optFriendlyInformation = (Text-OrNull $row.D); h1bVisaInformation = (Text-OrNull $row.E)
        likelyInterviewProcess = (Text-OrNull $row.M); postingAvailability = $availabilityMap[[string]$row.Q]
        lastAvailabilityCheck = $checkTime; availabilityCheckNotes = (Text-OrNull $row.R)
    }
    $application = [ordered]@{
        userId = $UserId; jobId = 0; status = $statusMap[[string]$row.G]
        dateApplied = (Date-Utc $row.I); interviewDate = $null; notes = (Text-OrNull $row.L)
        dateFound = $(if ($found) { $found.ToString('yyyy-MM-dd') } else { $null })
        priority = (Text-OrNull $row.K); interviewPrepNotes = (Text-OrNull $row.N)
        resumeTips = (Text-OrNull $row.O); dsaTopicsToKnow = (Text-OrNull $row.P)
        additionalSourceNotes = (Text-OrNull ($sourceNotes -join [Environment]::NewLine))
    }
    foreach ($key in $job.Keys) {
        $limit = if ($key -in @('company','title','location')) { 200 } elseif ($key -eq 'jobUrl') { 2048 } else { 20000 }
        if (([string]$job[$key]).Length -gt $limit) { throw "Row $($row.Row): $key exceeds API limit." }
    }
    if ($job.jobUrl -and ![uri]::IsWellFormedUriString($job.jobUrl, [UriKind]::Absolute)) { throw "Row $($row.Row): invalid posting URL." }
    foreach ($key in @('notes','priority','interviewPrepNotes','resumeTips','dsaTopicsToKnow','additionalSourceNotes')) {
        $limit = if ($key -eq 'notes') { 10000 } else { 20000 }
        if (([string]$application[$key]).Length -gt $limit) { throw "Row $($row.Row): $key exceeds API limit." }
    }
    [pscustomobject]@{ row = $row.Row; job = $job; application = $application; followUp = (Date-Utc $row.J) }
})
$payloads | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $directory 'preview.json')
$warnings | Set-Content -Encoding UTF8 (Join-Path $directory 'warnings.txt')
Write-Output "Validated $($payloads.Count) Sheet1 records. Warnings: $($warnings.Count)."
Write-Output "Import report: $directory"
if (!$Apply) { Write-Output 'Preview only. Use -Apply to import.'; exit 0 }
$user = Invoke-RestMethod "$ApiBase/users/$UserId" -TimeoutSec 20
if (!$user.id) { throw 'Destination profile not found.' }
$ledgerPath = Join-Path $directory 'ledger.json'
$ledger = @{}
if (Test-Path -LiteralPath $ledgerPath) {
    $previous = Get-Content -LiteralPath $ledgerPath -Raw | ConvertFrom-Json
    foreach ($property in $previous.PSObject.Properties) { $ledger[$property.Name] = $property.Value }
}
function Save-Ledger {
    $temp = $ledgerPath + '.tmp'
    $ledger | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 -LiteralPath $temp
    Move-Item -LiteralPath $temp -Destination $ledgerPath -Force
}
function Post-Record([string]$path, $body) {
    $json = $body | ConvertTo-Json -Depth 8 -Compress
    return Invoke-RestMethod ($ApiBase + $path) -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 30
}
$created = 0; $skipped = 0
foreach ($item in $payloads) {
    $key = [string]$item.row
    $entry = $ledger[$key]
    if (!$entry) { $entry = [pscustomobject]@{ jobId = 0; applicationId = 0; reminderId = 0 }; $ledger[$key] = $entry }
    if ($entry.applicationId) {
        $existing = Invoke-RestMethod "$ApiBase/applications/$($entry.applicationId)" -TimeoutSec 20
        if ($existing.userId -ne $UserId -or $existing.jobId -ne $entry.jobId) { throw "Import ledger mismatch for row $key." }
        $skipped++
        continue
    }
    if (!$entry.jobId) {
        $job = Post-Record '/jobs' $item.job
        $entry.jobId = $job.id
        Save-Ledger
    }
    $item.application.jobId = $entry.jobId
    $application = Post-Record '/applications' $item.application
    $entry.applicationId = $application.id
    Save-Ledger
    if ($item.followUp -and !$entry.reminderId) {
        $reminder = Post-Record '/reminders' @{ userId=$UserId; applicationId=$entry.applicationId; title=('Follow up: ' + $item.job.company); dueDate=$item.followUp; completed=$false }
        $entry.reminderId = $reminder.id
        Save-Ledger
    }
    $created++
    if ($created % 50 -eq 0) { Write-Output "Imported $created records..." }
}
Write-Output "Created $created applications; skipped $skipped previously imported rows."
