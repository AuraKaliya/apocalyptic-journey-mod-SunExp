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

$shippedScriptRoots = @(
    "SunExp\Scripts",
    "SanGuoShaExp\Scripts",
    "AuraToolsExp\Scripts",
    "TestMods\SkinExp\Scripts",
    "TestMods\BackgroundAudioReplaceExp\Scripts",
    "TestMods\CardUseCialloExp\Scripts",
    "TestMods\ChatExp\Scripts",
    "TestMods\SkillCGExp\Scripts"
)
foreach ($relative in $shippedScriptRoots) {
    $scriptsPath = Join-Path $repoRoot $relative
    foreach ($technicalBinary in @("0Harmony.dll", "Aura.Director.DetourBackend.dll")) {
        if (Test-Path -LiteralPath (Join-Path $scriptsPath $technicalBinary) -PathType Leaf) {
            throw "Technical Detour binary must not be packaged before production approval: $relative\$technicalBinary"
        }
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

Write-Host "AuraDirector isolated detour validation passed."
