param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

& (Join-Path $repoRoot "tools\Test-AuraSharedCore.ps1") -Configuration $Configuration
& (Join-Path $repoRoot "tools\Build-MainSharedConsumers.ps1") -Configuration $Configuration

$mainProjects = @(
    "SunExp-Dev\SunExp.Dll.csproj",
    "SanGuoShaExp-Dev\SanGuoShaExp.Dll.csproj",
    "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj"
)

foreach ($project in $mainProjects) {
    $projectPath = Join-Path $repoRoot $project
    $text = Get-Content -Raw -LiteralPath $projectPath
    foreach ($required in @("AuraSharedCore", "AuraAudioShared")) {
        if (-not $text.Contains($required)) {
            throw "Main shared consumer is missing ${required}: $project"
        }
    }
}

$auraToolsProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj")
foreach ($required in @("AuraCgShared", "AuraLogShared")) {
    if (-not $auraToolsProject.Contains($required)) {
        throw "AuraToolsExp service surface is missing $required."
    }
}

foreach ($registry in @("SunExp\audio.registry.json", "SanGuoShaExp\audio.registry.json")) {
    $registryText = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot $registry)
    if ($registryText.Contains("ModResource/audio")) {
        throw "Main audio registry still points at direct ModResource audio: $registry"
    }
    if (-not $registryText.Contains("Shared:Audio/")) {
        throw "Main audio registry does not resolve through AuraShared audio paths: $registry"
    }
}

Write-Host "Main shared framework validation passed."
