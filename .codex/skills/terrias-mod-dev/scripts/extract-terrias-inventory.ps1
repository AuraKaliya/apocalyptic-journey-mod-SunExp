param(
    [string]$ModRoot = ""
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..\..")).Path
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

$repoRoot = Get-RepoRoot
if (-not $ModRoot) {
    $ModRoot = Join-Path $repoRoot "SunExp"
}
elseif (-not [System.IO.Path]::IsPathRooted($ModRoot)) {
    $ModRoot = Join-Path $repoRoot $ModRoot
}

$modRootPath = (Resolve-Path -LiteralPath $ModRoot).Path
$cards = Read-Rows (Join-Path $modRootPath "Data\Card\sunexp.csv")
$relics = Read-Rows (Join-Path $modRootPath "Data\Relic\sunexp.csv")
$buffs = Read-Rows (Join-Path $modRootPath "Data\Buff\sunexp.csv")
$packs = Read-Rows (Join-Path $modRootPath "Data\CardPack\sunexp.csv")

Write-Host "SunExp inventory"
Write-Host "  Cards:  $($cards.Count)"
Write-Host "  Relics: $($relics.Count)"
Write-Host "  Buffs:  $($buffs.Count)"
Write-Host "  Packs:  $($packs.Count)"

Write-Host ""
Write-Host "Cards by pack:"
$cards | Group-Object PackBelong | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name): $($_.Count)"
}

Write-Host ""
Write-Host "Relics by pack:"
$relics | Group-Object PackBelong | Sort-Object Name | ForEach-Object {
    Write-Host "  $($_.Name): $($_.Count)"
}
