param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

$backendProject = Join-Path $repoRoot "AuraDirectorDetour-Dev\Aura.Director.DetourBackend.csproj"
$testProject = Join-Path $repoRoot "AuraDirectorDetour.Tests\AuraDirectorDetour.Tests.csproj"
$backendProjectText = Get-Content -Raw -LiteralPath $backendProject
$backendSource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorDetour-Dev\AuraDirectorReadyToStartDetourBackend.cs")
$registrySource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorDetour-Dev\AuraDirectorOneShotHoldRegistry.cs")
$sharedProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraSharedRuntime-Dev\Aura.Shared.csproj")
$runtimeSource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorRuntime.cs")
$modelsSource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorModels.cs")
$compilerSource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraDirectorShared\AuraDirectorPlanCompiler.cs")
$sunExpSource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Features\Director\SunExpDirectorRuntime.cs")
$sunExpEntry = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Entry.cs")
$sunExpProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "SunExp-Dev\SunExp.Dll.csproj")

foreach ($required in @(
    "FightManager.ReadyToStart",
    "VerifiedWitchSha256",
    "detour-target-build-unverified",
    "harmony.UnpatchAll(HarmonyId)",
    "return true"
)) {
    if (-not $backendSource.Contains($required)) {
        throw "AuraDirector detour backend contract is missing: $required"
    }
}

foreach ($required in @("bypass", "StopAndReleaseAll", "TryRelease", "failed open")) {
    if (-not $registrySource.Contains($required)) {
        throw "AuraDirector one-shot hold registry contract is missing: $required"
    }
}

foreach ($forbidden in @("readyCount", "fightType", "ActionQueue", "Time.timeScale", "UserCode_ReadyToStart", "DOAllAction")) {
    if ($backendSource.Contains($forbidden) -or $registrySource.Contains($forbidden)) {
        throw "AuraDirector detour backend uses a rejected private/progression surface: $forbidden"
    }
}

if ($sharedProject.Contains("Lib.Harmony") -or $sharedProject.Contains("AuraDirectorDetour-Dev")) {
    throw "Aura.Shared must not take a production dependency on the optional detour backend or Harmony."
}
if (-not $backendProjectText.Contains('PackageReference Include="Lib.Harmony" Version="2.4.2"')) {
    throw "The isolated detour backend must pin its reviewed Harmony version."
}

foreach ($required in @(
    "IAuraDirectorNativeStartHoldSink",
    "Time.unscaledTime",
    'Finish(session, "hard-timeout")',
    "session.Hold.TryRelease",
    "NativeBattleSpriteProviderId",
    "SilhouetteSprite",
    "MinimumSupportedRuntimeProtocolVersion",
    "Keyboard.current",
    "Mouse.current",
    "AuraDirectorPortraitLayout.Calculate",
    "AuraDirectorPortraitGraphic",
    "FocusBarRatio",
    "AuraDirectorCueKind.Wait",
    "PrepareInputBlock",
    "PlaybackStartedAt"
)) {
    if (-not $runtimeSource.Contains($required)) {
        throw "AuraDirector local runtime contract is missing: $required"
    }
}
foreach ($required in @(
    "AuraDirectorProtocol",
    "MinimumReaderSchemaVersion",
    "AuraDirectorPlanEnvelope",
    "Extensions"
)) {
    if (-not $modelsSource.Contains($required)) {
        throw "AuraDirector versioned plan envelope contract is missing: $required"
    }
}
foreach ($required in @(
    "contract-id-unsupported",
    "schema-version-unsupported",
    "reader-version-unsupported",
    "NormalizeExtensions",
    "AppendExtensions",
    "SidePortraitStrategyId",
    "OpeningDelaySeconds"
)) {
    if (-not $compilerSource.Contains($required)) {
        throw "AuraDirector compiler compatibility contract is missing: $required"
    }
}
if ($runtimeSource.Contains("Time.timeScale")) {
    throw "AuraDirector local runtime must not mutate global time scale."
}
foreach ($forbidden in @("Input.GetKeyDown", "Input.GetMouseButtonDown", "preserveAspect = true")) {
    if ($runtimeSource.Contains($forbidden)) {
        throw "AuraDirector local runtime regressed to rejected input/layout behavior: $forbidden"
    }
}

foreach ($required in @(
    "CompanionFriendlyRosterService.Snapshot(includeControlled: false)",
    "EnemyManager.Instance?.enemyList",
    "Battle.OpeningDirector",
    "InputAndProgression",
    "NativeBattleSpriteProviderId",
    "SidePortraitStrategyId"
)) {
    if (-not $sunExpSource.Contains($required)) {
        throw "SunExp director request-source contract is missing: $required"
    }
}
if ($sunExpSource.Contains("CreateActor(localPlayer")) {
    throw "SunExp director request source must not collapse the friendly roster to the local player."
}
if (-not $sunExpEntry.Contains('RunStep("director runtime"')) {
    throw "SunExp must initialize the local director runtime."
}
foreach ($required in @(
    "AuraDirectorDetour-Dev\Aura.Director.DetourBackend.csproj",
    "Aura.Director.DetourBackend.dll",
    "0Harmony.dll"
)) {
    if (-not $sunExpProject.Contains($required)) {
        throw "SunExp director packaging contract is missing: $required"
    }
}

$shippedScriptRoots = @(
    "SanGuoShaExp\Scripts",
    "AuraToolsExp\Scripts"
)
foreach ($relative in $shippedScriptRoots) {
    $scriptsPath = Join-Path $repoRoot $relative
    foreach ($technicalBinary in @("0Harmony.dll", "Aura.Director.DetourBackend.dll")) {
        if (Test-Path -LiteralPath (Join-Path $scriptsPath $technicalBinary) -PathType Leaf) {
            throw "AuraDirector provider must remain scoped to SunExp: $relative\$technicalBinary"
        }
    }
}

$sunExpScripts = Join-Path $repoRoot "SunExp\Scripts"
foreach ($runtimeBinary in @("0Harmony.dll", "Aura.Director.DetourBackend.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $sunExpScripts $runtimeBinary) -PathType Leaf)) {
        throw "SunExp director runtime binary is missing: SunExp\Scripts\$runtimeBinary"
    }
}

dotnet build $testProject -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "AuraDirector detour test build failed."
}

$testExe = Join-Path $repoRoot "AuraDirectorDetour.Tests\bin\$Configuration\net472\AuraDirectorDetour.Tests.exe"
& $testExe $ManagedPath
if ($LASTEXITCODE -ne 0) {
    throw "AuraDirector detour tests failed."
}

Write-Host "AuraDirector local runtime and scoped detour validation passed."
