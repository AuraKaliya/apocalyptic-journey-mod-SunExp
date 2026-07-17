param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraSharedCore.Tests\AuraSharedCore.Tests.csproj"
$sharedProject = Join-Path $repoRoot "AuraSharedRuntime-Dev\Aura.Shared.csproj"
$managedPath = Join-Path $repoRoot "Managed"

function Read-RepoSourceTree {
    param([string]$RelativeDirectory)

    $directory = Join-Path $repoRoot $RelativeDirectory
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Required source directory is missing: $RelativeDirectory"
    }

    $files = @(Get-ChildItem -LiteralPath $directory -Recurse -Filter "*.cs" -File | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Required source directory has no C# files: $RelativeDirectory"
    }

    return (($files | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join [Environment]::NewLine)
}

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraSharedCore test harness failed."
}

dotnet build $sharedProject -c $Configuration /p:ManagedPath="$managedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Aura.Shared runtime build failed."
}

$sharedDll = Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472\Aura.Shared.dll"
if (-not (Test-Path -LiteralPath $sharedDll -PathType Leaf)) {
    throw "Aura.Shared runtime DLL was not produced: $sharedDll"
}

$sharedAssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($sharedDll)
if ($sharedAssemblyName.Name -ne "Aura.Shared") {
    throw "Aura.Shared runtime DLL has the wrong assembly name: $($sharedAssemblyName.Name)"
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

$identityText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedIdentity.cs")
foreach ($required in @("SelectRoleId", "IsRuntimeNumericId", "IsUsableRoleId", "RuntimeNumericIdLength")) {
    if (-not $identityText.Contains($required)) {
        throw "AuraShared identity runtime-role contract is missing: $required"
    }
}

$frameSchedulerText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedFrameScheduler.cs")
foreach ($required in @("AuraSharedFramePhase", "ReadyKeyedBucket", "OwnerQuantum", "CompareDelayed", "NormalizeEstimatedCost", "SortedDictionary<int, SortedDictionary<int, ReadyKeyedBucket>>")) {
    if (-not $frameSchedulerText.Contains($required)) {
        throw "AuraShared frame scheduler phased/fair-queue contract is missing: $required"
    }
}
foreach ($required in @("AuraSharedFrameWorkRequest", "AuraSharedFrameSliceContext", "RunCooperative", "ExecuteCooperativeSlice")) {
    if (-not $frameSchedulerText.Contains($required)) {
        throw "AuraShared cooperative frame-work contract is missing: $required"
    }
}
if ($frameSchedulerText.Contains("CurrentKeyedActions")) {
    throw "AuraShared frame scheduler must not regress to a single FIFO keyed queue."
}
foreach ($required in @("mainThreadId", "EnsureMainThreadRunner", "IsMainThreadOrUninitialized", "AuraSharedBackgroundWorkScheduler.PumpMainThreadCompletions")) {
    if (-not $frameSchedulerText.Contains($required)) {
        throw "AuraShared frame scheduler must retain its main-thread completion boundary: $required"
    }
}

$backgroundWorkText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedBackgroundWorkScheduler.cs")
foreach ($required in @("AuraSharedBackgroundWorkScheduler", "AuraSharedBackgroundWorkRequest", "MaxCpuConcurrency", "MaxIoConcurrency", "MaxPendingPerOwner", "ConcurrentQueue<Completion>", "CancellationToken", "PumpMainThreadCompletions")) {
    if (-not $backgroundWorkText.Contains($required)) {
        throw "AuraShared bounded background-work contract is missing: $required"
    }
}
if ($backgroundWorkText.Contains("ThreadPool.SetMinThreads") -or $backgroundWorkText.Contains("ThreadPool.SetMaxThreads")) {
    throw "AuraShared background work must not mutate the process-wide CLR thread-pool limits."
}

$frameStepText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedFrameStepRunner.cs")
foreach ($required in @("AuraSharedFrameStepResult", "ContinueNextFrame", "WaitFrames", "OwnerId", "Phase", "EstimatedCost")) {
    if (-not $frameStepText.Contains($required)) {
        throw "AuraShared frame step runner cooperative-step contract is missing: $required"
    }
}

$authoritativeSyncText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraAuthoritativeSyncRuntime.cs")
foreach ($required in @("AuraAuthoritativeSyncRuntime", "AuraAuthoritativeSyncDomain", "OwnerModId", "DomainId", "TryBeginSnapshotRequest", "TryClaimToken", "AcceptRemoteSnapshotSession", "ResetSession")) {
    if (-not $authoritativeSyncText.Contains($required)) {
        throw "AuraShared authoritative sync contract is missing: $required"
    }
}
if ($authoritativeSyncText.Contains("SunExp") -or $authoritativeSyncText.Contains("ScorchingCanopy")) {
    throw "AuraShared authoritative sync runtime must remain semantic-free and not mention SunExp field content."
}

$objectPoolText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedObjectPool.cs")
foreach ($required in @("AuraSharedObjectPool<TKey, TValue>", "capacityPerKey", "TryAcquire", "Release", "Clear")) {
    if (-not $objectPoolText.Contains($required)) {
        throw "AuraShared bounded object-pool contract is missing: $required"
    }
}
if ($objectPoolText.Contains("SunExp") -or $objectPoolText.Contains("CardItem") -or $objectPoolText.Contains("DataConfig")) {
    throw "AuraShared object pool must remain semantic-free."
}

$jsonText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedJson.cs")
if (-not $jsonText.Contains("public static class AuraSharedJson")) {
    throw "AuraSharedJson must be public because product Mods now consume it through Aura.Shared.dll."
}

$storageText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedStorageCoordinator.cs")
foreach ($required in @("AuraSharedResourceLockTable", "File.Replace", "FileOptions.WriteThrough", "ExpectedRevision", "CreateWriteMutex", "StorageLockKey")) {
    if (-not $storageText.Contains($required)) {
        throw "AuraShared storage safety contract is missing: $required"
    }
}

$packageText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedPackageCoordinator.cs")
foreach ($required in @("RecoverTransactions", 'State = "Prepared"', 'journal.State = "ContentCommitted"', 'journal.State = "RegistryCommitted"', '"Completed"', '"RolledBack"', "AuraSharedOperationLog")) {
    if (-not $packageText.Contains($required)) {
        throw "AuraShared package transaction contract is missing: $required"
    }
}

$bootstrapText = (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedResourceBootstrapper.cs")) +
    (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraSharedBootstrapResult.cs"))
foreach ($required in @("AuraSharedResourceBootstrapper", "AuraSharedBootstrapResult", "Installed", "Repaired", "Updated", "Deduplicated", "Conflicts", "Failures")) {
    if (-not $bootstrapText.Contains($required)) {
        throw "AuraShared resource bootstrap contract is missing: $required"
    }
}

if ($runtimeText.Contains("&& string.Equals(buildId, CurrentBuildId")) {
    throw "AuraShared compatible global reuse must not depend on exact BuildId equality."
}
foreach ($required in @("BuildIdPrefix", "ManifestModule.ModuleVersionId")) {
    if (-not $runtimeText.Contains($required)) {
        throw "AuraShared BuildId must identify the actual assembly build: $required"
    }
}

foreach ($runtime in @(
    @{ Name = "AudioArbiterShared"; Text = (Read-RepoSourceTree "AudioArbiterShared") },
    @{ Name = "BattleBgmArbiterShared"; Text = (Read-RepoSourceTree "BattleBgmArbiterShared") },
    @{ Name = "AuraCgShared"; Text = (Read-RepoSourceTree "AuraCgShared") },
    @{ Name = "AuraSkinShared\AuraSkinRuntime.cs"; Text = (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSkinShared\AuraSkinRuntime.cs")) }
)) {
    if ($runtime.Text.Contains("&& string.Equals(buildId, CurrentBuildId")) {
        throw "$($runtime.Name) still treats exact BuildId equality as a compatibility requirement."
    }
}

$cgRegistryText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraCgShared\AuraCgRegistry.cs")
foreach ($required in @("AuraCgRegistryRuntime", "RegistryAuthorityId", "AuraCgRegistryDocument", "AuraCgManifest", "AuraCgRegistryEntry", "WriteShared")) {
    if (-not $cgRegistryText.Contains($required)) {
        throw "AuraCgShared registry contract is missing: $required"
    }
}
$cgRuntimeText = Read-RepoSourceTree "AuraCgShared"
$removedCoverMode = (-join @("fullscreen", "Cover", "Fade"))
if ($cgRuntimeText.Contains($removedCoverMode) -or $cgRegistryText.Contains($removedCoverMode)) {
    throw "AuraCgShared must use fullscreenFade plus fit=cover instead of the removed cover-specific mode."
}

$cardUseFxRegistryText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraCardUseFxShared\AuraCardUseFxRegistry.cs")
$cardUseFxRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraCardUseFxShared\AuraCardUseFxRuntime.cs")
$cardUseFxRibbonText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraCardUseFxShared\AuraBezierRibbonGraphic.cs")
foreach ($required in @("AuraCardUseFxRegistryRuntime", "AuraCardUseFxRegistryEntry", "QualifiedEffectId", "WriteShared", "Resolve", "AuraCardUseFxPresentationScopes", "PresentationScope", "CurrentSchemaVersion = 2")) {
    if (-not $cardUseFxRegistryText.Contains($required)) {
        throw "AuraCardUseFxShared registry contract is missing: $required"
    }
}
foreach ($required in @("AuraCardLifecycleRouter", "AuraCombatActionRouter", "LocalCommitted", "FightUI.DoCardUseAnimation", "ICard.SetCardStyle", "DedupeSeconds", "ClearTransient", "Triggered")) {
    if (-not $cardUseFxRuntimeText.Contains($required)) {
        throw "AuraCardUseFxShared trigger contract is missing: $required"
    }
}
foreach ($required in @("ConfigureStrands", "Evaluate", "EvaluateTangent")) {
    if (-not $cardUseFxRibbonText.Contains($required)) {
        throw "AuraCardUseFxShared ribbon contract is missing: $required"
    }
}

$directorModelsText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorModels.cs")
$directorCompilerText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorPlanCompiler.cs")
$directorLayoutText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorPortraitLayout.cs")
$directorProbeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorNativeStartBarrierProbe.cs")
$directorStartGateText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorStartGateContracts.cs")
foreach ($required in @("AuraDirectorRequest", "AuraDirectorActorRef", "AuraDirectorPlanDescriptor", "AuraDirectorSessionStateMachine", "IAuraDirectorNativeStartHold", "IAuraDirectorNativeStartHoldSink")) {
    $allDirectorText = $directorModelsText + $directorCompilerText + $directorStartGateText + (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorSessionState.cs"))
    if (-not $allDirectorText.Contains($required)) {
        throw "AuraDirectorShared contract is missing: $required"
    }
}
foreach ($required in @("alternating-portrait-v1", "side-portrait-v2", "OpeningDelaySeconds = 0.3d", "MaximumActorCount = 32", "ComputeHash", "actor-key-duplicate")) {
    if (-not $directorCompilerText.Contains($required)) {
        throw "AuraDirectorShared deterministic plan compiler is missing: $required"
    }
}
foreach ($required in @("VerticalInsetPixels = 10d", "ResolveAnchoredX", "SourceCenterY", "DisplayHeight")) {
    if (-not $directorLayoutText.Contains($required)) {
        throw "AuraDirectorShared portrait-focus layout contract is missing: $required"
    }
}
foreach ($required in @("native-hook-not-cancellable", "ModHookContext", "Supported = false")) {
    if (-not $directorProbeText.Contains($required)) {
        throw "AuraDirectorShared native capability probe is missing: $required"
    }
}
foreach ($forbidden in @("readyCount", "ActionQueue", "Time.timeScale", "Harmony", "MonoMod")) {
    if ($directorModelsText.Contains($forbidden) -or $directorCompilerText.Contains($forbidden) -or $directorProbeText.Contains($forbidden)) {
        throw "AuraDirectorShared must not use rejected native progression workaround: $forbidden"
    }
}
foreach ($required in @("FocusX", "FocusY", "SafeScale", "CalculateCoverImageOffset")) {
    if (-not $cgRuntimeText.Contains($required)) {
        throw "AuraCgShared cover-focus contract is missing: $required"
    }
}

$contractPath = Join-Path $repoRoot "docs\aura-shared-core-v2-contract.md"
if (-not (Test-Path -LiteralPath $contractPath)) {
    throw "AuraShared Core v2 protocol contract document is missing."
}
$contractText = Get-Content -Raw -LiteralPath $contractPath
foreach ($required in @("Storage Request Template", "Package Install Request Template", "Operation Log", "Lock Keys")) {
    if (-not $contractText.Contains($required)) {
        throw "AuraShared Core v2 protocol contract is missing section: $required"
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
    (-join [char[]](0x666E, 0x901A, 0x4E8B, 0x4EF6)),
    (-join [char[]](0x9996, 0x9886))
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
if (-not $auraConfigText.Contains("ResolveRegisteredRoleDisplayName")) {
    throw "AuraTools registered CG import must resolve role display names separately from CG display names."
}

$fileResourceText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Infrastructure\FileResourceUtil.cs")
if (-not $fileResourceText.Contains("AuraSharedPackageEngine.Install") -or $fileResourceText.Contains("File.Copy")) {
    throw "AuraTools Audio/CG import does not use the shared package engine exclusively."
}

$sunPackagePath = Join-Path $repoRoot "SunExp\SharedResources\package.json"
$sunPackage = Get-Content -Raw -Encoding UTF8 -LiteralPath $sunPackagePath | ConvertFrom-Json
if ($sunPackage.packageId -ne "SunExp.SharedResources" -or $sunPackage.ownerModId -ne "SunExp" -or
    -not ($sunPackage.capabilities -contains "Audio") -or -not ($sunPackage.capabilities -contains "CG")) {
    throw "SunExp shared resource package manifest is invalid."
}
$sunPackageRoot = Split-Path -Parent $sunPackagePath
$sunAudioResource = $sunPackage.resources | Where-Object {
    $_.system -eq "Audio" -and $_.resourceId -eq "SunExp.WuNa.VoicePack" -and $_.kind -eq "Directory"
} | Select-Object -First 1
if ($null -eq $sunAudioResource) {
    throw "SunExp shared Audio package manifest is missing WuNa voice pack."
}
$sunAudioSource = [System.IO.Path]::GetFullPath((Join-Path $sunPackageRoot $sunAudioResource.source))
if (-not (Test-Path -LiteralPath $sunAudioSource -PathType Container)) {
    throw "SunExp shared Audio package source is missing: $sunAudioSource"
}
$requiredSunCgResources = @(
    "SunExp.Loneer.MorningStarPrayer.SkillCg",
    "SunExp.WuNa.WhiteSunPrayer.SkillCg",
    "SunExp.Loneer.FeastCg",
    "SunExp.WuNa.FeastCg"
)
foreach ($resourceId in $requiredSunCgResources) {
    $resource = $sunPackage.resources | Where-Object {
        $_.system -eq "CG" -and $_.resourceId -eq $resourceId -and $_.kind -eq "File"
    } | Select-Object -First 1
    if ($null -eq $resource) {
        throw "SunExp shared CG package manifest is missing resource: $resourceId"
    }

    $source = [System.IO.Path]::GetFullPath((Join-Path $sunPackageRoot $resource.source))
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "SunExp shared CG package source is missing: $source"
    }
}
$sunCardUseCgResource = $sunPackage.resources | Where-Object {
    $_.system -eq "CG" -and $_.resourceId -eq "SunExp.BlazingCrownCollapse.CardUseCgSequence" -and $_.kind -eq "Directory"
} | Select-Object -First 1
if ($null -eq $sunCardUseCgResource -or @($sunCardUseCgResource.tags) -notcontains "card-use-cg" -or
    $sunCardUseCgResource.metadata.cgKind -ne "cardUse") {
    throw "SunExp shared CG package manifest is missing Blazing Crown Collapse card-use CG semantics."
}
$sunCardUseCgSource = [System.IO.Path]::GetFullPath((Join-Path $sunPackageRoot $sunCardUseCgResource.source))
if (-not (Test-Path -LiteralPath $sunCardUseCgSource -PathType Container)) {
    throw "SunExp shared card-use CG package source is missing: $sunCardUseCgSource"
}
$sunCgManifestPath = Join-Path $repoRoot "SunExp\SharedResources\cg.registry.json"
$sunCgManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $sunCgManifestPath | ConvertFrom-Json
if ($sunCgManifest.ownerModId -ne "SunExp" -or @($sunCgManifest.entries).Count -lt 4) {
    throw "SunExp CG registry manifest is invalid."
}
foreach ($cgId in @("loneer.morning-star-prayer", "wuna.white-sun-prayer")) {
    $entry = $sunCgManifest.entries | Where-Object { $_.cgId -eq $cgId } | Select-Object -First 1
    if ($null -eq $entry -or $entry.kind -ne "skill" -or $entry.media.type -ne "image" -or
        [string]::IsNullOrWhiteSpace($entry.media.resource) -or [string]::IsNullOrWhiteSpace($entry.defaultPresentation.mode)) {
        throw "SunExp CG registry manifest is missing a valid entry: $cgId"
    }
}
$wunaCgEntry = $sunCgManifest.entries | Where-Object { $_.cgId -eq "wuna.white-sun-prayer" } | Select-Object -First 1
if ($wunaCgEntry.defaultPresentation.mode -ne "fullscreenFade" -or $wunaCgEntry.defaultPresentation.fit -ne "cover") {
    throw "WuNa skill CG must use fullscreenFade with cover fitting."
}
$blazingCrownCgEntry = $sunCgManifest.entries | Where-Object { $_.cgId -eq "sunexp.blazing-crown-collapse" } | Select-Object -First 1
if ($null -eq $blazingCrownCgEntry -or $blazingCrownCgEntry.kind -ne "cardUse" -or
    $blazingCrownCgEntry.media.type -ne "sequence" -or
    @($blazingCrownCgEntry.tags) -notcontains "card-use-cg" -or
    $blazingCrownCgEntry.media.flashMode -ne "hybridBwPulse") {
    throw "Blazing Crown Collapse must be registered as a card-use CG sequence."
}
foreach ($cgId in @("loneer.feast", "wuna.feast")) {
    $entry = $sunCgManifest.entries | Where-Object { $_.cgId -eq $cgId } | Select-Object -First 1
    if ($null -eq $entry -or $entry.kind -ne "feast" -or $entry.media.type -ne "image" -or
        [string]::IsNullOrWhiteSpace($entry.media.resource) -or
        $entry.defaultPresentation.mode -ne "fullscreenFade" -or $entry.defaultPresentation.fit -ne "cover" -or
        $entry.defaultActivation.consumerMode -ne "toolManaged" -or
        $entry.defaultActivation.consumerModId -ne "AuraToolsExp") {
        throw "SunExp Feast CG registry manifest is missing a valid tool-managed entry: $cgId"
    }
}
$sunEntryText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Entry.cs")
$sunProjectText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "SunExp-Dev\SunExp.Dll.csproj")
if (-not $sunEntryText.Contains("AuraCgRegistryRuntime.RegisterManifest") -or
    -not $sunProjectText.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
    throw "SunExp does not register CG manifests through AuraCgShared."
}
if (Test-Path -LiteralPath (Join-Path $repoRoot "SunExp\ModResource\audio")) {
    throw "SunExp still carries a direct ModResource/audio runtime source."
}

$audioManifestText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "SunExp\audio.registry.json")
if ($audioManifestText.Contains("ModResource/audio") -or -not $audioManifestText.Contains("Shared:Audio/SunExp/WuNa")) {
    throw "SunExp audio registry does not resolve through the shared resource layer."
}

$auraToolsPackagePath = Join-Path $repoRoot "AuraToolsExp\SharedResources\package.json"
$auraToolsPackage = Get-Content -Raw -Encoding UTF8 -LiteralPath $auraToolsPackagePath | ConvertFrom-Json
if ($auraToolsPackage.ownerModId -ne "AuraToolsExp" -or
    @($auraToolsPackage.resources).Count -ne 3 -or
    @($auraToolsPackage.resources | Where-Object { $_.system -eq "Audio" }).Count -ne 1 -or
    @($auraToolsPackage.resources | Where-Object { $_.system -eq "CG" }).Count -ne 2) {
    throw "AuraTools bundled Audio/CG package manifest is invalid."
}
$auraToolsPackageRoot = Split-Path -Parent $auraToolsPackagePath
foreach ($resource in $auraToolsPackage.resources) {
    $source = [System.IO.Path]::GetFullPath((Join-Path $auraToolsPackageRoot $resource.source))
    if (-not (Test-Path -LiteralPath $source)) {
        throw "AuraTools bundled resource source is missing: $source"
    }
    if (-not $resource.destination.StartsWith($resource.system + "/AuraToolsExp/", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "AuraTools bundled resource destination is not owner-qualified: $($resource.destination)"
    }
}

$auraToolsEntryText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Entry.cs")
$auraToolsBootstrapText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Infrastructure\AuraToolsResourceBootstrap.cs")
if (-not $auraToolsEntryText.Contains("AuraToolsResourceBootstrap.Initialize") -or
    -not $auraToolsBootstrapText.Contains("AuraSharedResourceBootstrapper.Bootstrap") -or
    -not $auraToolsEntryText.Contains("AuraCgRegistryRuntime.RegisterManifest")) {
    throw "AuraTools does not consume the shared resource bootstrap infrastructure."
}
$auraToolsCgManifestPath = Join-Path $repoRoot "AuraToolsExp\SharedResources\cg.registry.json"
$auraToolsCgManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $auraToolsCgManifestPath | ConvertFrom-Json
if ($auraToolsCgManifest.ownerModId -ne "AuraToolsExp" -or
    @($auraToolsCgManifest.entries | Where-Object { $_.kind -eq "feast" }).Count -lt 8) {
    throw "AuraTools Feast CG registry manifest is invalid."
}
foreach ($entry in @($auraToolsCgManifest.entries | Where-Object { $_.kind -eq "feast" })) {
    if ($entry.defaultActivation.consumerMode -ne "toolManaged" -or
        $entry.defaultActivation.consumerModId -ne "AuraToolsExp" -or
        $entry.defaultPresentation.mode -ne "fullscreenFade" -or
        $entry.defaultPresentation.fit -ne "cover") {
        throw "AuraTools Feast CG entry is not tool-managed/fullscreen-cover: $($entry.cgId)"
    }
}
$feastRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\Feast\AuraToolsFeastRuntime.cs")
if (-not $feastRuntimeText.Contains("DisableSync = true")) {
    throw "AuraTools Feast CG must remain local-only by disabling CG sync."
}
foreach ($forbidden in @("skip-multiplayer", "IsMultiplayerSession")) {
    if ($feastRuntimeText.Contains($forbidden)) {
        throw "AuraTools Feast must not skip execution merely because a local PlayerManager/multiplayer runtime exists: $forbidden"
    }
}
$skillCgEditorText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\SkillCg\AuraToolsSkillCgRuntime.cs")
foreach ($required in @("OpenRuleImageDirectory", '"本地目录"', '"图片目录"', "BuildPresentationModeOptions", "BuildFitOptions")) {
    if (-not $skillCgEditorText.Contains($required)) {
        throw "AuraTools SkillCG editor is missing unified CG controls: $required"
    }
}
if ($skillCgEditorText.Contains('"打开目录"')) {
    throw "AuraTools SkillCG editor still uses the old generic open-directory button label."
}

foreach ($required in @("AuraSharedIdentity.SelectRoleId", "activation-skip:", "no AuraTools rule emitted")) {
    if (-not $skillCgEditorText.Contains($required)) {
        throw "AuraTools SkillCG runtime is missing trigger diagnostics/role fallback: $required"
    }
}

$sunSkillCgRuntimeText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Features\SkillCg\SunExpSkillCgRuntime.cs")
foreach ($required in @("AuraCombatActionRouter.RegisterBefore", "BuildRequests(trigger)", "BuildRegisteredCardUseRequests", "TrimStart('*')", "no CG request matched")) {
    if (-not $sunSkillCgRuntimeText.Contains($required)) {
        throw "SunExp SkillCG runtime is missing trigger diagnostics/role fallback: $required"
    }
}

$combatActionRouterText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraCombatActionRouter.cs")
foreach ($required in @("AuraSharedIdentity.SelectRoleId", "ReadStatusRoleId", "BuildEventToken", '"FightUI.CallActionAnimation"', "safeInvoke: true")) {
    if (-not $combatActionRouterText.Contains($required)) {
        throw "Shared combat action router is missing trigger diagnostics/role fallback: $required"
    }
}

$cardLifecycleRouterText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedCore\AuraCardLifecycleRouter.cs")
foreach ($required in @("AuraCardLifecyclePhase", "AuraHookRegistry", "RegisteredPhases", "EnsurePhaseRegistrationsNoLock", "EnsureBeforeNoLock", "EnsureAfterNoLock", "CommonCardItemTrueUse", "CardItemInit", "PlayerInfoRandomAddCard", "OrderByDescending", "ThenBy")) {
    if (-not $cardLifecycleRouterText.Contains($required)) {
        throw "Shared card lifecycle router is missing shared hook ownership or deterministic dispatch: $required"
    }
}

$sharedConsumerProjects = @(
    "SunExp-Dev\SunExp.Dll.csproj",
    "SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj",
    "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"
)
$linkedSharedPattern = 'Compile Include="[^"]*(AuraSharedCore|AuraAudioShared|AuraCardUseFxShared|AuraLogShared|AuraJourneyShared|AuraModeShared|AuraSkinShared|AudioArbiterShared|BattleBgmArbiterShared|StarterDeckArbiterShared|UiRaycastSafetyShared|UiTransitionGuardShared|AuraCgShared|AuraDirectorShared|AuraDirectorDetour-Dev|AuraOnlineShared)'
foreach ($relativeProject in $sharedConsumerProjects) {
    $consumerText = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $relativeProject)
    if (-not $consumerText.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
        throw "Shared consumer must reference Aura.Shared.dll through the shared runtime project: $relativeProject"
    }
    if ($consumerText -match $linkedSharedPattern) {
        throw "Shared consumer still links shared source directly: $relativeProject"
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
