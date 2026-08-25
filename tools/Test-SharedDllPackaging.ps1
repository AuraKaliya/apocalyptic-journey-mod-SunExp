param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
$sharedDll = Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472\Aura.Shared.dll"

if (-not (Test-Path -LiteralPath $sharedDll -PathType Leaf)) {
    throw "Aura.Shared.dll is missing. Build shared consumers before packaging validation: $sharedDll"
}

$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($sharedDll)
if ($assemblyName.Name -ne "Aura.Shared") {
    throw "Packaged shared runtime has wrong assembly name: $($assemblyName.Name)"
}

$expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sharedDll).Hash
$consumers = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification product -DefaultOnly)
if ($consumers.Count -ne 2 `
    -or @($consumers.id) -notcontains "Terrias" `
    -or @($consumers.id) -notcontains "AuraToolsExp") {
    throw "Product shared consumers must be exactly Terrias and AuraToolsExp."
}
$packagedDlls = @($consumers | ForEach-Object {
    ([string]$_.packagePath).Replace('/', '\') + "\Aura.Shared.dll"
})

foreach ($relative in $packagedDlls) {
    $path = Join-Path $repoRoot $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Packaged Aura.Shared.dll is missing: $relative"
    }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($hash -ne $expectedHash) {
        throw "Packaged Aura.Shared.dll hash mismatch: $relative"
    }
}

$sharedSourcePattern = 'Compile Include="[^"]*(AuraSharedCore|AuraAudioShared|AuraCardUseFxShared|AuraDecisionShared|AuraCombatAiShared|AuraLogShared|AuraJourneyShared|AuraModeShared|AuraSkinShared|AudioArbiterShared|BattleBgmArbiterShared|StarterDeckArbiterShared|UiRaycastSafetyShared|UiTransitionGuardShared|AuraCgShared|AuraDirectorShared|AuraDirectorDetour-Dev|AuraOnlineShared|AuraRoleShared)'
$consumerProjects = @($consumers | ForEach-Object { ([string]$_.projectPath).Replace('/', '\') })

foreach ($relative in $consumerProjects) {
    $text = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $relative)
    if (-not $text.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
        throw "Consumer does not reference Aura.Shared runtime project: $relative"
    }

    if ($text -match $sharedSourcePattern) {
        throw "Consumer still compiles private shared source: $relative"
    }

    if ($text -match 'CopyAuraSharedDllToMod|CopyEntryDllToMod') {
        throw "Product consumer project must not write packaged DLLs directly: $relative"
    }
}

$publishManifestPath = Join-Path $repoRoot "artifacts\shared-release\$Configuration\shared-package-manifest.json"
if (-not (Test-Path -LiteralPath $publishManifestPath -PathType Leaf)) {
    throw "Product package publish manifest is missing: $publishManifestPath"
}
$publishManifest = Get-Content -Raw -LiteralPath $publishManifestPath | ConvertFrom-Json
if ($publishManifest.schemaVersion -ne 1 -or $publishManifest.sharedSha256 -ne $expectedHash) {
    throw "Product package publish manifest does not match the canonical shared DLL."
}
$publishedIds = @($publishManifest.consumers | ForEach-Object { [string]$_.id } | Sort-Object)
$expectedIds = @($consumers | ForEach-Object { [string]$_.id } | Sort-Object)
if ([string]::Join('|', $publishedIds) -ne [string]::Join('|', $expectedIds)) {
    throw "Product package publish manifest consumer set is stale."
}

$sanGuo = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification test -Id "SanGuoShaExp")
if ($sanGuo.Count -ne 1 `
    -or -not ([string]$sanGuo[0].projectPath).StartsWith("TestMods/", [System.StringComparison]::Ordinal) `
    -or [bool]$sanGuo[0].defaultBuild) {
    throw "SanGuoShaExp must remain an explicit-only TestMods consumer."
}

Write-Host "Aura.Shared DLL packaging validation passed."
