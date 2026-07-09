param(
    [string]$SkillRoot = "",
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($SkillRoot)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $SkillRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $scriptDir)) ""
}

$root = [System.IO.Path]::GetFullPath($SkillRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Skill root not found: $root"
}

$self = [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path)
$patterns = @(
    [pscustomobject]@{
        Key = "legacy-project-root"
        Regex = "D:\\\\workfile\\\\project\\\\Mod_1\\\\SunExp(?!\\\\apocalyptic-journey-mod-SunExp)|Mod_1\\\\SunExp(?!\\\\apocalyptic-journey-mod-SunExp)"
        Note = "retired SunExp repository root"
    },
    [pscustomobject]@{
        Key = "retired-mode-name"
        Regex = "TongtianTower|通天塔"
        Note = "retired mode name; use EndlessSea/EndlessAbyss current naming"
    },
    [pscustomobject]@{
        Key = "pure-data-workflow"
        Regex = "纯数据版|pure-data|cardpack burst|burst-balance"
        Note = "old pure-data-only workflow anchor"
    },
    [pscustomobject]@{
        Key = "old-card-balance-anchor"
        Regex = "Solar Radiance|Gathered Flame|Crown Manifestation"
        Note = "old card-pack balance session anchor"
    },
    [pscustomobject]@{
        Key = "old-decompile-folder"
        Regex = "反编译文件夹v1\.0\.23693118"
        Note = "old decompile folder version; route through game-reference-index"
    }
)

$allowedRelative = @(
    "sunexp-skill-evolution\SKILL.md",
    "sunexp-skill-evolution\references\stale-anchor-registry.md",
    "sunexp-skill-evolution\scripts\audit-sunexp-skill-staleness.ps1"
)

$files = Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        $_.Extension -in @(".md", ".yaml", ".yml", ".json", ".ps1") -and
        [System.IO.Path]::GetFullPath($_.FullName) -ne $self
    }

$hits = New-Object System.Collections.Generic.List[object]
foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($root, $file.FullName)
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($pattern in $patterns) {
        foreach ($match in [regex]::Matches($text, $pattern.Regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            $line = ($text.Substring(0, $match.Index) -split "`r?`n").Count
            $allowed = $allowedRelative -contains $relative
            $hits.Add([pscustomobject]@{
                File = $relative
                Line = $line
                Key = $pattern.Key
                Note = $pattern.Note
                Allowed = $allowed
            }) | Out-Null
        }
    }
}

$unexpected = @($hits | Where-Object { -not $_.Allowed })
if ($hits.Count -eq 0) {
    Write-Host "SunExp skill staleness audit passed: no stale anchors found."
    exit 0
}

foreach ($hit in $hits) {
    $prefix = if ($hit.Allowed) { "allowed" } else { "unexpected" }
    Write-Host ("{0}: {1}:{2} [{3}] {4}" -f $prefix, $hit.File, $hit.Line, $hit.Key, $hit.Note)
}

if ($Strict -and $unexpected.Count -gt 0) {
    throw "Unexpected stale SunExp skill anchors found: $($unexpected.Count)"
}

if ($unexpected.Count -eq 0) {
    Write-Host "SunExp skill staleness audit passed: only intentional audit anchors found."
} else {
    Write-Warning "SunExp skill staleness audit found unexpected anchors: $($unexpected.Count)"
}
