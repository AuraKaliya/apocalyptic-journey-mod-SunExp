param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AuraCombatAiShared.Tests\AuraCombatAiShared.Tests.csproj"
$arguments = @("run", "--project", $project, "-c", "Release")
if ($NoRestore) {
    $arguments += "--no-restore"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI tests failed with exit code $LASTEXITCODE."
}

$trainerPath = Join-Path $root "tools\train_aura_combat_ai.py"
& python $trainerPath --self-test
if ($LASTEXITCODE -ne 0) {
    throw "Aura combat AI trainer self-test failed with exit code $LASTEXITCODE."
}

$controllerPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleRuntime.cs"
$presenterPath = Join-Path $root "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattlePredictionPresenter.cs"
$interactionPath = Join-Path $root "AuraCombatAiShared\GameApi\WitchCombatInteractionRuntime.cs"
$runtimePath = Join-Path $root "AuraCombatAiShared\GameApi\WitchCombatRuntime.cs"
$plannerPath = Join-Path $root "AuraCombatAiShared\CombatTurnPlanner.cs"
$controller = Get-Content -LiteralPath $controllerPath -Raw
$presenter = Get-Content -LiteralPath $presenterPath -Raw
$interaction = Get-Content -LiteralPath $interactionPath -Raw
$runtime = Get-Content -LiteralPath $runtimePath -Raw
$planner = Get-Content -LiteralPath $plannerPath -Raw
$trainer = Get-Content -LiteralPath $trainerPath -Raw

$requiredControllerAnchors = @(
    "FightUI.ThrowCardScript",
    "FightUI.Burning",
    "CombatActionTransaction",
    "CombatActionTransactionState.HandedOff",
    "CombatActionTransactionState.TimedOut",
    "auto-battle-training-v4.jsonl",
    "CaptureTeacherAction",
    "CaptureTeacherEndTurn",
    "FightUI.onChangeTurnBtn",
    'demonstrator: "human"',
    "[AutoBattle][Training] actor=",
    "PolicyPreselectedCandidateId",
    "policyVisibleToHuman: teacherPolicyVisibleToHuman",
    "UpdateShadowPrediction",
    "ShowPredictionMarkers",
    "AuraToolsAutoBattleModelRuntime.Load",
    "[AutoBattle][ModelShadow]",
    "residualSupport=",
    "AuraSharedJson.SerializeCompact"
)
foreach ($anchor in $requiredControllerAnchors) {
    if (-not $controller.Contains($anchor)) {
        throw "Aura combat AI controller contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "UI/SelectedIcon",
    "AuraToolsResourceCache.Load<GameObject>",
    "raycastTarget = false",
    "blocksRaycasts = false",
    "CombatTargetKind.Enemy",
    "ActionColor",
    "LateUpdate",
    "card.uiElement",
    "SyncEdge",
    "CardBorderThickness = 1.5f",
    "PlaceImmediatelyBehind",
    "ignoreLayout = true"
)) {
    if (-not $presenter.Contains($anchor)) {
        throw "Aura combat AI prediction presenter contract is missing: $anchor"
    }
}
if ($presenter.Contains("Shader.") -or $presenter.Contains("new Material")) {
    throw "The baseline prediction marker must not introduce a shader or per-marker material."
}

if ($controller.Contains("Math.Max(60f, settings.ActionTimeoutSeconds)")) {
    throw "End-turn actions must use the same root transaction deadline."
}

$requiredInteractionAnchors = @(
    "CombatPromptKind.DiscardCards",
    "CombatPromptKind.BurnCards",
    "CombatPromptSelectionTracker",
    "TryBeginAttempt",
    "TryIssueConfirm",
    "card.selectContainer != null",
    'card.Tags.Contains("Froze")'
)
foreach ($anchor in $requiredInteractionAnchors) {
    if (-not $interaction.Contains($anchor)) {
        throw "Aura combat AI interaction contract is missing: $anchor"
    }
}

if (-not $runtime.Contains("targeted card target is stale or defeated") -or
    -not $runtime.Contains("skill target is stale or defeated")) {
    throw "Aura combat AI stale-target execution guards are missing."
}

foreach ($anchor in @(
    "AddEnemiesAndNativeThreat",
    "ExpectedBlockableDamage",
    "ExpectedUnblockableDamage",
    "ExpectedDamageOverTime",
    "CombatAiRegistry.TryResolveThreat"
)) {
    if (-not $runtime.Contains($anchor)) {
        throw "Aura combat AI threat observation contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "BeamWidth",
    "MaxPlanDepth",
    "CostReduction",
    "ApplyDamage"
)) {
    if (-not $planner.Contains($anchor)) {
        throw "Aura combat AI turn planner contract is missing: $anchor"
    }
}

foreach ($anchor in @(
    "aura.combat-ai.sample.v3",
    "aura.combat-ai.sample.v4",
    "aura.combat-ai.selection.v1",
    "aura.combat-ai.training-report.v1",
    "aura.decision-residual.linear.v1",
    "HumanPolicyDisagreementCount",
    "PolicyVisibleToHuman",
    "HumanPolicyVisibleCount",
    '"UsedAsPreferenceLabels": False',
    "MaximumCorrection",
    "ApplicabilityProtocolVersion",
    "CategoryObservationCounts"
)) {
    if (-not $trainer.Contains($anchor)) {
        throw "Aura combat AI trainer contract is missing: $anchor"
    }
}

if ($trainer.Contains("positive, negative = other, chosen")) {
    throw "Policy failures must not invent counterfactual preference labels."
}

Write-Host "Aura combat AI source contracts passed."
