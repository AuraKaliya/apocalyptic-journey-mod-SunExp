param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AudioArbiterShared.Tests\AudioArbiterShared.Tests.csproj"

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AudioArbiterShared test project is missing: $project"
}

$runtimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AudioArbiterShared\AudioArbiterRuntime.cs")
$adapterSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AudioArbiterShared\AudioHookAdapter.cs")
$factorySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AudioArbiterShared\AudioRequestFactory.cs")
$stateReaderSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AudioArbiterShared\AudioGameStateReader.cs")
if ($factorySource -match 'CreateCombatActionBatch' `
        -or $factorySource -notmatch 'CreateCardUse' `
        -or $factorySource -notmatch 'CreateSkillVoice' `
        -or $adapterSource -notmatch 'AuraSkillActionTransactionRouter\.Register' `
        -or $adapterSource -notmatch 'AuraSkillActionPhase\.Committed' `
        -or $runtimeSource -notmatch 'OnSkillActionCommitted' `
        -or $runtimeSource -notmatch 'AudioRequestFactory\.CreateSkillVoice' `
        -or $stateReaderSource -notmatch 'AudioSkillSlotResolver\.Resolve' `
        -or $stateReaderSource -notmatch 'DataType\.Career') {
    throw "AudioArbiter skill voice must use only the committed skill transaction and configured role skill slot."
}

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AudioArbiterShared tests failed."
}
