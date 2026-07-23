param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$sharedDll = Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472\Aura.Shared.dll"

if (-not (Test-Path -LiteralPath $sharedDll -PathType Leaf)) {
    throw "Aura.Shared.dll is missing. Build shared consumers before packaging validation: $sharedDll"
}

$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($sharedDll)
if ($assemblyName.Name -ne "Aura.Shared") {
    throw "Packaged shared runtime has wrong assembly name: $($assemblyName.Name)"
}

$expectedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sharedDll).Hash
$packagedDlls = @(
    "Terrias\Scripts\Aura.Shared.dll",
    "SanGuoShaExp\Scripts\Aura.Shared.dll",
    "AuraToolsExp\Scripts\Aura.Shared.dll"
)

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
$consumerProjects = @(
    "Terrias-Dev\Terrias.Dll.csproj",
    "SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj",
    "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"
)

foreach ($relative in $consumerProjects) {
    $text = Get-Content -Raw -LiteralPath (Join-Path $repoRoot $relative)
    if (-not $text.Contains("AuraSharedRuntime-Dev\Aura.Shared.csproj")) {
        throw "Consumer does not reference Aura.Shared runtime project: $relative"
    }

    if ($text -match $sharedSourcePattern) {
        throw "Consumer still compiles private shared source: $relative"
    }
}

Write-Host "Aura.Shared DLL packaging validation passed."
