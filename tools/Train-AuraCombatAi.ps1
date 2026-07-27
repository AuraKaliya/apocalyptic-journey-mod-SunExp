param(
    [string]$InputPath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Logs\AuraToolsExp\auto-battle-training-v6.jsonl",
    [string]$OutputPath = "",
    [string]$ReportPath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Logs\AuraToolsExp\auto-battle-training-report.json",
    [ValidateSet("balanced", "aggressive", "defensive")]
    [string]$Profile = "balanced",
    [switch]$ReportOnly
)

$ErrorActionPreference = "Stop"
$trainer = Join-Path $PSScriptRoot "train_aura_combat_ai.py"
if (-not (Test-Path -LiteralPath $InputPath)) {
    throw "Training data does not exist: $InputPath"
}
if (-not (Test-Path -LiteralPath $trainer)) {
    throw "Trainer does not exist: $trainer"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $InputPath) "auto-battle-model-candidate-$Profile.json"
}

$arguments = @(
    $trainer,
    "--input", $InputPath,
    "--output", $OutputPath,
    "--report", $ReportPath,
    "--profile", $Profile
)
if ($ReportOnly) {
    $arguments += "--report-only"
}
& python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura Combat AI trainer failed with exit code $LASTEXITCODE"
}
