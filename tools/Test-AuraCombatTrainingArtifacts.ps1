param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$trainer = Join-Path $root "tools\train_aura_combat_ai.py"
$manifestPath = Join-Path $root (
    "AuraToolsExp\Config\combat-programs.base-game.manifest.json")
$generatedProgramsPath = Join-Path $root (
    "AuraToolsExp-Dev\Features\AutoBattle\Generated\AuraToolsNativePrograms.g.cs")
$workerProject = Join-Path $root (
    "AuraFoundationTrainer.Worker\AuraFoundationTrainer.Worker.csproj")
$installer = Join-Path $root "tools\Install-AuraPyTorch.cmd"

foreach ($requiredPath in @(
    $trainer,
    $manifestPath,
    $generatedProgramsPath,
    $workerProject,
    $installer
)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Aura combat training artifact is missing: $requiredPath"
    }
}

& python $trainer --self-test
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI trainer self-test failed with exit code $LASTEXITCODE."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.runtimeProtocol -ne "aura.native-programs.precompiled.v1" `
        -or $manifest.programCount -lt 400 `
        -or [string]::IsNullOrWhiteSpace($manifest.programSetSha256)) {
    throw "Aura precompiled native program manifest is invalid."
}

$bundledModelDirectory = Join-Path $root "AuraToolsExp\ModResource\Model"
$bundledModels = @(Get-ChildItem -LiteralPath $bundledModelDirectory `
    -Filter "*.json" -File)
foreach ($modelFile in $bundledModels) {
    $package = Get-Content -LiteralPath $modelFile.FullName -Raw `
        -Encoding UTF8 | ConvertFrom-Json
    $model = if ($null -ne $package.Model) {
        $package.Model
    } else {
        $package.ModelArtifact
    }
    if ($package.ArtifactKind -ne "aura.foundation-model-package" `
            -or $model.ModelProtocol -ne "aura.combat-policy-value.mlp.v2" `
            -or [int]$model.ProtocolVersion -ne 2 `
            -or [int]$model.FeatureSchemaVersion -ne 26) {
        throw "Bundled foundation model is incompatible: $($modelFile.Name)"
    }
}

Write-Host (
    "Aura combat training artifacts passed: programs={0}, models={1}." -f `
        $manifest.programCount,
        $bundledModels.Count)
