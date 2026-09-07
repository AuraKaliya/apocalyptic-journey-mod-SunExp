param(
    [string]$ModRoot = ""
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
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

function Read-TableRows {
    param([string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory)) {
        return @()
    }

    $rows = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $Directory -Filter "*.csv" -File | Sort-Object Name)) {
        $rows += @(Read-Rows $file.FullName)
    }
    return @($rows)
}

$repoRoot = Get-RepoRoot
if (-not $ModRoot) {
    $ModRoot = Join-Path $repoRoot "Terrias"
}
elseif (-not [System.IO.Path]::IsPathRooted($ModRoot)) {
    $ModRoot = Join-Path $repoRoot $ModRoot
}

$modRootPath = (Resolve-Path -LiteralPath $ModRoot).Path
$cards = Read-TableRows (Join-Path $modRootPath "Data\Card")
$relics = Read-TableRows (Join-Path $modRootPath "Data\Relic")
$buffs = Read-TableRows (Join-Path $modRootPath "Data\Buff")
$packs = Read-TableRows (Join-Path $modRootPath "Data\CardPack")

Write-Host "Terrias inventory"
Write-Host "  Cards:  $($cards.Count)"
Write-Host "  Relics: $($relics.Count)"
Write-Host "  Buffs:  $($buffs.Count)"
Write-Host "  Packs:  $($packs.Count)"

Write-Host ""
Write-Host "Cards by pack:"
$cards | Group-Object { if ([string]::IsNullOrWhiteSpace($_.PackBelong)) { "<unpacked>" } else { $_.PackBelong } } | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name): $($_.Count)"
}

Write-Host ""
Write-Host "Relics by pack:"
$relics | Group-Object { if ([string]::IsNullOrWhiteSpace($_.PackBelong)) { "<unpacked>" } else { $_.PackBelong } } | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name): $($_.Count)"
}
