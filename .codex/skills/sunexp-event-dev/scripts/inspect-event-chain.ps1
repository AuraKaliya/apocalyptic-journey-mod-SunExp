param(
    [Parameter(Mandatory = $true)]
    [string]$Prefix,
    [string]$ModRoot = ""
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..\..")).Path
}

function Resolve-ModRoot {
    param([string]$Requested)
    $repoRoot = Get-RepoRoot
    if ([string]::IsNullOrWhiteSpace($Requested)) {
        return (Resolve-Path -LiteralPath (Join-Path $repoRoot "SunExp")).Path
    }
    if (-not [System.IO.Path]::IsPathRooted($Requested)) {
        $Requested = Join-Path $repoRoot $Requested
    }
    return (Resolve-Path -LiteralPath $Requested).Path
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

function Normalize-Id {
    param([string]$Id)
    if ($null -eq $Id) {
        return ""
    }
    return $Id.TrimStart("*")
}

$modRootPath = Resolve-ModRoot $ModRoot
$dataRows = Read-Rows (Join-Path $modRootPath "Data\EventList\sunexp.csv")
$textRows = Read-Rows (Join-Path $modRootPath "Text\EventList\sunexp.csv")

$textById = @{}
foreach ($row in $textRows) {
    $textById[(Normalize-Id $row.Id)] = $row
}

$matching = @($dataRows | Where-Object { (Normalize-Id $_.Id).StartsWith($Prefix) })
if ($matching.Count -eq 0) {
    Write-Host "No Data/EventList rows found for prefix '$Prefix'."
    exit 0
}

$report = foreach ($row in $matching) {
    $id = Normalize-Id $row.Id
    $text = $textById[$id]
    [pscustomobject]@{
        Id = $id
        HasText = ($null -ne $text)
        Name = if ($text) { $text.Name } else { "" }
        InitScript = $row.InitScript
        Option1 = $row.'1Script'
        Text1 = if ($text) { $text.'1Describe' } else { "" }
        Option2 = $row.'2Script'
        Text2 = if ($text) { $text.'2Describe' } else { "" }
        Option3 = $row.'3Script'
        Text3 = if ($text) { $text.'3Describe' } else { "" }
        Option4 = $row.'4Script'
        Text4 = if ($text) { $text.'4Describe' } else { "" }
    }
}

$report | Format-Table -AutoSize -Wrap
