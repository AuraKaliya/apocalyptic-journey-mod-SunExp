param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraSharedCore.Tests\AuraSharedCore.Tests.csproj"

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraSharedCore test harness failed."
}

$runtimeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedRuntime.cs")
foreach ($required in @(
    "CurrentProtocolVersion = 2",
    '"ReadStorageJson"',
    '"WriteStorageJson"',
    '"InstallResourceJson"',
    '"GetInstalledResourcesJson"',
    '"GetChangesJson"'
)) {
    if (-not $runtimeText.Contains($required)) {
        throw "AuraShared Core v2 runtime contract is missing: $required"
    }
}

$storageText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedStorageCoordinator.cs")
foreach ($required in @("ReaderWriterLockSlim", "File.Replace", "FileOptions.WriteThrough", "ExpectedRevision", "CreateWriteMutex")) {
    if (-not $storageText.Contains($required)) {
        throw "AuraShared storage safety contract is missing: $required"
    }
}

$packageText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedPackageCoordinator.cs")
foreach ($required in @("RecoverTransactions", 'State = "Prepared"', 'journal.State = "ContentCommitted"', 'journal.State = "RegistryCommitted"')) {
    if (-not $packageText.Contains($required)) {
        throw "AuraShared package transaction contract is missing: $required"
    }
}

$journeyRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraJourneyShared\AuraJourneyRuntime.cs")
foreach ($required in @("Only the authoritative side may advance journey state", "AuraJourneyStateReducer.Apply", "WriteRuntime")) {
    if (-not $journeyRuntimeText.Contains($required)) {
        throw "AuraJourneyShared authority/storage contract is missing: $required"
    }
}

$journeyModelsText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraJourneyShared\AuraJourneyRouteModels.cs")
foreach ($required in @("AuraJourneyMapNodeSpec", "AuraJourneyRouteGraph", "AuraJourneySlotRule", "DicePolicy")) {
    if (-not $journeyModelsText.Contains($required)) {
        throw "AuraJourneyShared route/native-node contract is missing: $required"
    }
}

$journeyBridgeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraJourneyShared\AuraJourneyGameBridge.cs")
foreach ($required in @("CreateMapNode", "EnsureNodeDice", "RepairSyncArrays", "RestoreCurrentNodeFromSyncArrays", "AuraJourneyMapIdAliasRegistry.Expand")) {
    if (-not $journeyBridgeText.Contains($required)) {
        throw "AuraJourneyShared game bridge contract is missing: $required"
    }
}

$sharedContentForbidden = @(
    "SunExp",
    "SanGuoShaExp",
    "solar_memory",
    "SolarMemory",
    "普通事件",
    "首领"
)
$sharedSourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraJourneyShared") -File -Filter "*.cs"
foreach ($file in $sharedSourceFiles) {
    $text = Get-Content -Raw -LiteralPath $file.FullName
    foreach ($forbidden in $sharedContentForbidden) {
        if ($text.Contains($forbidden)) {
            throw "AuraJourneyShared code contains main-Mod content marker '$forbidden': $($file.FullName)"
        }
    }
}

$auraConfigText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsConfigService.cs")
if (-not $auraConfigText.Contains("AuraSharedConfigStore.ReadOwner") -or
    -not $auraConfigText.Contains("AuraSharedConfigStore.WriteOwner") -or
    $auraConfigText.Contains("File.WriteAllText")) {
    throw "AuraTools owner configuration does not use Core v2 storage exclusively."
}

$fileResourceText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Infrastructure\FileResourceUtil.cs")
if (-not $fileResourceText.Contains("AuraSharedPackageEngine.Install") -or $fileResourceText.Contains("File.Copy")) {
    throw "AuraTools Audio/CG import does not use the shared package engine exclusively."
}

$sunPackagePath = Join-Path $repoRoot "SunExp\SharedResources\package.json"
$sunPackage = Get-Content -Raw -Encoding UTF8 -LiteralPath $sunPackagePath | ConvertFrom-Json
if ($sunPackage.packageId -ne "SunExp.SharedResources" -or $sunPackage.resources.Count -ne 1 -or
    $sunPackage.resources[0].system -ne "Audio" -or $sunPackage.resources[0].kind -ne "Directory") {
    throw "SunExp shared Audio package manifest is invalid."
}
$sunPackageRoot = Split-Path -Parent $sunPackagePath
$sunAudioSource = [System.IO.Path]::GetFullPath((Join-Path $sunPackageRoot $sunPackage.resources[0].source))
if (-not (Test-Path -LiteralPath $sunAudioSource -PathType Container)) {
    throw "SunExp shared Audio package source is missing: $sunAudioSource"
}
if (Test-Path -LiteralPath (Join-Path $repoRoot "SunExp\ModResource\audio")) {
    throw "SunExp still carries a direct ModResource/audio runtime source."
}

$audioManifestText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "SunExp\audio.registry.json")
if ($audioManifestText.Contains("ModResource/audio") -or -not $audioManifestText.Contains("Shared:Audio/SunExp/WuNa")) {
    throw "SunExp audio registry does not resolve through the shared resource layer."
}

$audioConsumers = Get-ChildItem -LiteralPath $repoRoot -Recurse -File -Filter "*.csproj" | Where-Object {
    (Get-Content -Raw -LiteralPath $_.FullName).Contains("AudioArbiterShared")
}
foreach ($project in $audioConsumers) {
    $projectText = Get-Content -Raw -LiteralPath $project.FullName
    if (-not $projectText.Contains("AuraSharedCore")) {
        throw "AudioArbiter consumer does not compile AuraSharedCore v2: $($project.FullName)"
    }
}

$logStoreText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedLogStore.cs")
$logWriterText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\Logging\AuraToolsLogFileWriter.cs")
if (-not $logStoreText.Contains("Enumerate") -or
    -not $logWriterText.Contains("FileShare.Read") -or
    $logWriterText.Contains("FileShare.ReadWrite")) {
    throw "Owner-only log writing and shared log aggregation contract is incomplete."
}

Write-Host "AuraSharedCore structural validation passed."
