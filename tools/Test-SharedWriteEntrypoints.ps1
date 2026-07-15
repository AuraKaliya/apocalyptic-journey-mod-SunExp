param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

$roots = @(
    "AuraAudioShared",
    "AuraCgShared",
    "AuraDirectorShared",
    "AuraDirectorDetour-Dev",
    "AuraJourneyShared",
    "AuraLogShared",
    "AuraSkinShared",
    "AuraToolsExp-Dev",
    "SanGuoShaExp-Dev",
    "SunExp-Dev",
    "TestMods"
)

$allowed = @(
    "AuraSharedCore\AuraSharedStorageCoordinator.cs",
    "AuraSharedCore\AuraSharedOperationLog.cs",
    "AuraSharedCore\AuraSharedLogStore.cs",
    "AuraLogShared\AuraLogRuntime.cs",
    "AuraToolsExp-Dev\Features\Logging\AuraToolsLogFileWriter.cs",
    "SunExp-Dev\GameApi\FamiliarGrowthApi.cs",
    "SunExp-Dev\Mechanics\EndlessAbyssRunLedger.cs",
    "SunExp-Dev\Mechanics\EndlessAbyssShockService.cs",
    "SunExp-Dev\Mechanics\EndlessSeaFloorPlanStore.cs",
    "TestMods\ChatExp-Dev\GameApi\AuraChatHostModSyncService.cs",
    "TestMods\LogExp-Dev\Infrastructure\LogFileWriter.cs"
)

$patterns = @(
    "File\.WriteAllText",
    "File\.WriteAllBytes",
    "JsonConvert\.SerializeObject",
    "new FileStream",
    "File\.Move",
    "Directory\.Move"
)

$violations = New-Object System.Collections.Generic.List[string]
foreach ($root in $roots) {
    $path = Join-Path $repoRoot $root
    if (-not (Test-Path -LiteralPath $path)) {
        continue
    }

    $files = Get-ChildItem -LiteralPath $path -Recurse -File -Filter "*.cs" | Where-Object {
        $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\"
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/').Replace('/', '\')
        if ($allowed -contains $relative) {
            continue
        }

        $text = Get-Content -Raw -LiteralPath $file.FullName
        foreach ($pattern in $patterns) {
            if ($text -match $pattern) {
                $violations.Add("${relative}: uses ${pattern}")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ }
    throw "Shared write entrypoint scan failed: $($violations.Count) violation(s)."
}

$settlementCgCache = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\DamageMeter\SettlementCg\DamageSettlementCgAssetCache.cs")
if ($settlementCgCache -notmatch "AuraSharedStorageCoordinator") {
    throw "Settlement CG idle cache writes must use AuraSharedStorageCoordinator."
}

if ($settlementCgCache -notmatch "WriteTextAtomic") {
    throw "Settlement CG idle cache metadata writes must use atomic shared-storage writes."
}

Write-Host "Shared write entrypoint scan passed."
