param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$cardRows = @(Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\Card\sunexp.csv"))
$ball = $cardRows | Where-Object Id -eq "spirit_ball" | Select-Object -First 1
$template = $cardRows | Where-Object Id -eq "*spirit_card_template" | Select-Object -First 1
Assert-True ($null -ne $ball) "spirit_ball card row is missing."
Assert-True ($ball.Rarity -eq "3" -and $ball.Expend -eq "1") "spirit_ball must be rarity 3 and cost 1."
Assert-True ($ball.Tag -eq "Retain,Annihilation") "spirit_ball must have Retain and Annihilation."
Assert-True ($ball.Icon -eq "Mods/SunExp/ModResource/Images/Card/MoreDimension/spirit_ball") "spirit_ball must use its independent card face."
$runtimeFacePath = Join-Path $repoRoot "SunExp\ModResource\Images\Card\MoreDimension\spirit_ball.png"
$sourceFacePath = Join-Path $repoRoot "SunExp-Dev\VisualAssets\CardSource512\MoreDimension\spirit_ball.png"
Assert-True (Test-Path -LiteralPath $runtimeFacePath) "spirit_ball card face is missing."
Assert-True (Test-Path -LiteralPath $sourceFacePath) "spirit_ball 512 source is missing."
Add-Type -AssemblyName System.Drawing
$runtimeFace = [System.Drawing.Image]::FromFile($runtimeFacePath)
$sourceFace = [System.Drawing.Image]::FromFile($sourceFacePath)
try {
    Assert-True ($runtimeFace.Width -eq 256 -and $runtimeFace.Height -eq 256) "spirit_ball runtime card face must be 256x256."
    Assert-True ($sourceFace.Width -eq 512 -and $sourceFace.Height -eq 512) "spirit_ball source card face must remain 512x512."
}
finally {
    $runtimeFace.Dispose()
    $sourceFace.Dispose()
}
Assert-True ($null -ne $template) "spirit card template row is missing."
Assert-True ($template.Tag -eq "Retain,Burnout") "spirit card must have Retain and Burnout only."
Assert-True ($template.Icon -eq $ball.Icon) "spirit card template must fall back to the spirit_ball card face."

$intentPath = Join-Path $repoRoot "SunExp\spirit.intent.registry.json"
$capturePath = Join-Path $repoRoot "SunExp\spirit.capture.registry.json"
$intent = Get-Content -LiteralPath $intentPath -Raw | ConvertFrom-Json
$capture = Get-Content -LiteralPath $capturePath -Raw | ConvertFrom-Json
Assert-True ($intent.schemaVersion -eq 1) "spirit intent registry schema must be 1."
Assert-True ($capture.schemaVersion -eq 1) "spirit capture registry schema must be 1."

$explicitIntents = @($intent.profiles | Where-Object enemyId -ne "*")
$explicitCapture = @($capture.profiles | Where-Object enemyId -ne "*")
Assert-True ($explicitIntents.Count -ge 59) "expected at least 59 explicit spirit intent profiles."
Assert-True ($explicitCapture.Count -eq $explicitIntents.Count) "intent and capture profile counts must match."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "*" -and $_.variantId -eq "*" })).Count -eq 1) "intent fallback profile is missing."
Assert-True ((@($capture.profiles | Where-Object { $_.enemyId -eq "*" -and $_.variantId -eq "*" })).Count -eq 1) "capture fallback profile is missing."

foreach ($profile in $explicitIntents) {
    Assert-True (@($profile.attackTendency).Count -gt 0) "spirit profile $($profile.enemyId) has no attack intent."
    Assert-True (@($profile.defenseTendency).Count -gt 0) "spirit profile $($profile.enemyId) has no defense intent."
    Assert-True ($profile.attackWeight -gt 0 -and $profile.defenseWeight -gt 0) "spirit profile $($profile.enemyId) has invalid tendency weights."
}

function Get-Chance([int]$CurrentHp, [int]$MaxHp) {
    $missing = 10000 - [int][Math]::Round($CurrentHp * 10000.0 / $MaxHp, [MidpointRounding]::AwayFromZero)
    return [Math]::Max(1000, [Math]::Min(9000, 1000 + [int][Math]::Round($missing * 0.8, [MidpointRounding]::AwayFromZero)))
}

Assert-True ((Get-Chance 100 100) -eq 1000) "full-health capture chance must be 10%."
Assert-True ((Get-Chance 50 100) -eq 5000) "half-health capture chance must be 50%."
Assert-True ((Get-Chance 0 100) -eq 9000) "zero-health formula cap must be 90%."

$requiredSources = @(
    "SunExp-Dev\GameApi\EnemyCatalogApi.cs",
    "SunExp-Dev\GameApi\EnemyCaptureSettlementApi.cs",
    "SunExp-Dev\Mechanics\SpiritCaptureService.cs",
    "SunExp-Dev\Mechanics\SpiritSummonService.cs",
    "SunExp-Dev\Hooks\SpiritRuntime.cs",
    "SunExp-Dev\Network\RpcSpiritCapture.cs",
    "SunExp-Dev\Network\RpcSpiritCompanion.cs"
)
foreach ($relative in $requiredSources) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $relative)) "required spirit source missing: $relative"
}

$factorySource = Get-Content -LiteralPath (Join-Path $repoRoot "SunExp-Dev\Mechanics\SpiritCardFactory.cs") -Raw
Assert-True ($factorySource.Contains("RoleTable.Instance?.cardList")) "spirit cards must persist into the current adventure deck."
Assert-True ($factorySource.Contains('config.Vars["RawData"]')) "spirit cards must persist dynamic data for safe-box restoration."

$hotspotSources = @(
    "SunExp-Dev\GameApi\EnemyCatalogApi.cs",
    "SunExp-Dev\GameApi\EnemyCaptureSettlementApi.cs",
    "SunExp-Dev\Hooks\Visual\SpiritCardFaceRuntime.cs",
    "SunExp-Dev\Mechanics\SpiritCardFactory.cs",
    "SunExp-Dev\Mechanics\SpiritSummonService.cs",
    "SunExp-Dev\Mechanics\CompanionIntentPlanner.cs"
) | ForEach-Object { Get-Content -LiteralPath (Join-Path $repoRoot $_) -Raw }
$hotspotText = $hotspotSources -join "`n"
foreach ($name in @(
    "Spirit.Catalog.Inspect",
    "Spirit.Catalog.DictProbe",
    "Spirit.Catalog.IdleProbe",
    "Spirit.CardFace.Load",
    "Spirit.Card.GrantToHand",
    "Spirit.Capture.Settlement",
    "Spirit.Summon.CanSummon",
    "Spirit.Summon.IdleProbe",
    "Spirit.Summon.Spawn",
    "Spirit.Intent.Plan"
)) {
    Assert-True ($hotspotText.Contains($name)) "spirit hotspot instrumentation is missing: $name"
}

Write-Host "Spirit capture assertions passed: profiles=$($explicitIntents.Count)."
