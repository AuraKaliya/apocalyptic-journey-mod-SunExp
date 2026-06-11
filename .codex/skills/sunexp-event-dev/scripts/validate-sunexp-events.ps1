param(
    [string]$ModRoot = "",
    [string]$EntryPath = ""
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..\..")).Path
}

function Resolve-RepoPath {
    param(
        [string]$Path,
        [string]$DefaultPath,
        [string]$RepoRoot
    )
    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = $DefaultPath
    }
    elseif (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path $RepoRoot $Path
    }
    return (Resolve-Path -LiteralPath $Path).Path
}

function Read-Rows {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }
    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -le 1) {
        return @()
    }
    return @($rows | Select-Object -Skip 1 | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Id)
    })
}

function Read-AllText {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path)
}

function Normalize-Id {
    param([string]$Id)
    if ($null -eq $Id) {
        return ""
    }
    return $Id.TrimStart("*")
}

function Add-Failure {
    param([string]$Message)
    $script:Failures.Add($Message) | Out-Null
}

function Add-Warning {
    param([string]$Message)
    $script:Warnings.Add($Message) | Out-Null
}

function New-IdSet {
    param([object[]]$Rows)
    $set = @{}
    foreach ($row in $Rows) {
        $id = Normalize-Id $row.Id
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            $set[$id] = $true
        }
    }
    return $set
}

function Test-DataTextPair {
    param(
        [string]$Kind,
        [object[]]$DataRows,
        [object[]]$TextRows
    )
    $textIds = New-IdSet $TextRows
    foreach ($row in $DataRows) {
        $id = Normalize-Id $row.Id
        if (-not $textIds.ContainsKey($id)) {
            Add-Failure "$Kind data row '$id' has no matching text row."
        }
    }
}

function Test-EventTextAlignment {
    param(
        [object[]]$EventRows,
        [object[]]$TextRows
    )
    $textById = @{}
    foreach ($text in $TextRows) {
        $textById[(Normalize-Id $text.Id)] = $text
    }
    foreach ($event in $EventRows) {
        $id = Normalize-Id $event.Id
        if (-not $textById.ContainsKey($id)) {
            continue
        }
        $text = $textById[$id]
        if ([string]::IsNullOrWhiteSpace($text.TotalDescribe) -or -not $text.TotalDescribe.TrimStart().StartsWith("<main>")) {
            Add-Failure "EventList '$id' TotalDescribe is missing or not aligned."
        }
        for ($i = 1; $i -le 4; $i++) {
            $scriptColumn = "${i}Script"
            $describeColumn = "${i}Describe"
            $hasScript = ($event.PSObject.Properties.Name -contains $scriptColumn) -and -not [string]::IsNullOrWhiteSpace($event.$scriptColumn)
            $description = if ($text.PSObject.Properties.Name -contains $describeColumn) { $text.$describeColumn } else { "" }
            if ($hasScript -and ([string]::IsNullOrWhiteSpace($description) -or -not $description.TrimStart().StartsWith("<main>"))) {
                Add-Failure "EventList '$id' option $i has script but missing/misaligned '$describeColumn'."
            }
            elseif (-not $hasScript -and -not [string]::IsNullOrWhiteSpace($description)) {
                Add-Warning "EventList '$id' option $i has '$describeColumn' text but no '$scriptColumn'."
            }
        }
    }
}

function Test-EventScriptHelpers {
    param(
        [object[]]$EventRows,
        [string]$EntryText
    )
    $defined = @(
        [regex]::Matches($EntryText, "(?m)^function\s+(SunExp_[A-Za-z0-9_]+)\s*\(") |
            ForEach-Object { $_.Groups[1].Value } |
            Sort-Object -Unique
    )
    $registered = @(
        [regex]::Matches($EntryText, 'SunExp_RegisterDynamicMethod\s*\(\s*config\s*,\s*"(?<name>SunExp_[A-Za-z0-9_]+)"') |
            ForEach-Object { $_.Groups["name"].Value } |
            Sort-Object -Unique
    )
    $scriptColumns = @("InitScript", "EntryScript", "1Script", "2Script", "3Script", "4Script")
    foreach ($row in $EventRows) {
        foreach ($column in $scriptColumns) {
            if (-not ($row.PSObject.Properties.Name -contains $column)) {
                continue
            }
            $code = $row.$column
            if ([string]::IsNullOrWhiteSpace($code)) {
                continue
            }
            foreach ($match in [regex]::Matches($code, "(SunExp_[A-Za-z0-9_]+)\s*\(")) {
                $helper = $match.Groups[1].Value
                if ($defined -notcontains $helper) {
                    Add-Failure "EventList '$($row.Id)' column '$column' calls undefined helper '$helper'."
                }
                elseif ($registered -notcontains $helper) {
                    Add-Failure "EventList '$($row.Id)' column '$column' calls unregistered helper '$helper'."
                }
            }
        }
    }
}

function Test-ForbiddenCaptions {
    param([string]$EntryText)
    $forbidden = @(
        "SunExp card recovered.",
        "SunExp relic recovered.",
        "SunExp blessing recovered.",
        "SunExp note closed."
    )
    foreach ($caption in $forbidden) {
        if ($EntryText.Contains($caption)) {
            Add-Failure "Entry.lua contains forbidden hard-coded event caption: $caption"
        }
    }
}

function Test-StoryEventIds {
    param([object[]]$EventRows)
    foreach ($row in $EventRows) {
        $id = Normalize-Id $row.Id
        if ($id -match "^[A-Za-z0-9]+_event_\d+$" -and -not $id.StartsWith("Sub_")) {
            Add-Warning "EventList '$id' looks like a story-chain row but is not prefixed with 'Sub_'."
        }
    }
}

function Test-MapEventRows {
    param(
        [object[]]$MapRows,
        [object[]]$MapTextRows
    )
    $textIds = New-IdSet $MapTextRows
    foreach ($row in $MapRows) {
        $id = Normalize-Id $row.Id
        if (-not $textIds.ContainsKey($id)) {
            Add-Failure "Map data row '$id' has no matching text row."
        }
        if ($row.Type -eq "Event" -and $row.NodeId -match "^SunExp_sunexp_Sub_") {
            Add-Warning "Map event '$id' points directly at story event '$($row.NodeId)'; prefer Lua runtime assignment for map-visible story chains."
        }
    }
}

$repoRoot = Get-RepoRoot
$modRootPath = Resolve-RepoPath -Path $ModRoot -DefaultPath (Join-Path $repoRoot "SunExp") -RepoRoot $repoRoot
$entryPathResolved = Resolve-RepoPath -Path $EntryPath -DefaultPath (Join-Path $repoRoot "SunExp\Scripts\Entry.lua") -RepoRoot $repoRoot

$script:Failures = New-Object System.Collections.Generic.List[string]
$script:Warnings = New-Object System.Collections.Generic.List[string]

$eventRows = @(Read-Rows (Join-Path $modRootPath "Data\EventList\sunexp.csv"))
$eventTextRows = @(Read-Rows (Join-Path $modRootPath "Text\EventList\sunexp.csv"))
$mapRows = @(Read-Rows (Join-Path $modRootPath "Data\Map\sunexp.csv"))
$mapTextRows = @(Read-Rows (Join-Path $modRootPath "Text\Map\sunexp.csv"))
$entryText = Read-AllText $entryPathResolved

Test-DataTextPair "EventList" $eventRows $eventTextRows
Test-EventTextAlignment $eventRows $eventTextRows
Test-EventScriptHelpers $eventRows $entryText
Test-ForbiddenCaptions $entryText
Test-StoryEventIds $eventRows
Test-MapEventRows $mapRows $mapTextRows

if ($script:Warnings.Count -gt 0) {
    Write-Host "Warnings:"
    foreach ($warning in $script:Warnings) {
        Write-Host " - $warning"
    }
}

if ($script:Failures.Count -gt 0) {
    Write-Host "Event validation failed: $($script:Failures.Count) failure(s)."
    foreach ($failure in $script:Failures) {
        Write-Host " - $failure"
    }
    exit 1
}

Write-Host "SunExp event validation passed: events=$($eventRows.Count), mapRows=$($mapRows.Count), warnings=$($script:Warnings.Count)."
