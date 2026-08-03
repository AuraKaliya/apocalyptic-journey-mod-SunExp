param(
    [string]$InputPath = "",
    [string]$OutputPath = "",
    [string]$ReportPath = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Logs\AuraToolsExp\auto-battle-training-report.json",
    [ValidateSet("balanced", "aggressive", "defensive")]
    [string]$Profile = "balanced",
    [switch]$ReportOnly
)

$ErrorActionPreference = "Stop"
$trainer = Join-Path $PSScriptRoot "train_aura_combat_ai.py"
if ([string]::IsNullOrWhiteSpace($InputPath)) {
    $datasetRoot = "D:\Steam\steamapps\common\Witch's Apocalyptic Journey\Witch's Apocalyptic Journey_Data\ModsData\AuraShared\Data\AuraToolsExp\AuraCombatAI\Datasets\Live"
    $latest = Get-ChildItem -LiteralPath $datasetRoot -Filter "auto-battle-training-v7.jsonl" -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -ne $latest) {
        $InputPath = $latest.FullName
    }
}
if (-not (Test-Path -LiteralPath $InputPath)) {
    throw "Training data does not exist. Pass -InputPath or capture v7 data under AuraShared/Data/AuraToolsExp/AuraCombatAI/Datasets/Live/<content-set-hash>."
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
