param()

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$utf8 = [System.Text.Encoding]::UTF8

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

$cardRows = @(Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\Card\terrias.csv"))
$ball = $cardRows | Where-Object Id -eq "spirit_ball" | Select-Object -First 1
$template = $cardRows | Where-Object Id -eq "*spirit_card_template" | Select-Object -First 1
$courtPurification = $cardRows | Where-Object Id -eq "afterglow_omen_card" | Select-Object -First 1
Assert-True ($null -ne $ball) "spirit_ball card row is missing."
Assert-True ($ball.Rarity -eq "3" -and $ball.Expend -eq "1") "spirit_ball must be rarity 3 and cost 1."
Assert-True ($ball.Tag -eq "Retain,Annihilation") "spirit_ball must have Retain and Annihilation."
Assert-True ($ball.Icon -eq "Mods/Terrias/ModResource/Images/Card/MoreDimension/spirit_ball") "spirit_ball must use its independent card face."
$runtimeFacePath = Join-Path $repoRoot "Terrias\ModResource\Images\Card\MoreDimension\spirit_ball.png"
$sourceFacePath = Join-Path $repoRoot "Terrias-Dev\VisualAssets\CardSource512\MoreDimension\spirit_ball.png"
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
$courtPurificationTag = $utf8.GetString([Convert]::FromBase64String("UmV0YWluLOeZveabnCxBbm5paGlsYXRpb24="))
Assert-True ($null -ne $courtPurification -and $courtPurification.Tag -eq $courtPurificationTag) "Court Purification must retain its visible Annihilation tag."

$intentPath = Join-Path $repoRoot "Terrias\spirit.intent.registry.json"
$capturePath = Join-Path $repoRoot "Terrias\spirit.capture.registry.json"
$intent = Get-Content -LiteralPath $intentPath -Raw | ConvertFrom-Json
$capture = Get-Content -LiteralPath $capturePath -Raw | ConvertFrom-Json
Assert-True ($intent.schemaVersion -eq 3) "spirit intent registry schema must be 3."
Assert-True ($capture.schemaVersion -eq 1) "spirit capture registry schema must be 1."

$intentProfileListFields = @(
    "sourceEnemyCardIds",
    "pveAttackTendency",
    "pveDefenseTendency",
    "pvpAttackTendency",
    "pvpDefenseTendency",
    "fallbackAttackTendency",
    "fallbackDefenseTendency",
    "pvpSourceEnemyCardIds",
    "fallbackSourceEnemyCardIds"
)
foreach ($profile in @($intent.profiles)) {
    foreach ($field in $intentProfileListFields) {
        $property = $profile.PSObject.Properties[$field]
        Assert-True ($null -ne $property) "spirit profile $($profile.enemyId) is missing list field $field."
        $actualType = if ($null -eq $property.Value) { "null" } else { $property.Value.GetType().Name }
        Assert-True ($property.Value -is [System.Array]) "spirit profile $($profile.enemyId) field $field must be a JSON array, actual=$actualType."
    }
}
foreach ($profile in @($capture.profiles)) {
    Assert-True ($profile.suppressedSuccessorIds -is [System.Array]) "capture profile $($profile.enemyId) suppressedSuccessorIds must be a JSON array."
}

$explicitIntents = @($intent.profiles | Where-Object enemyId -ne "*")
$explicitCapture = @($capture.profiles | Where-Object enemyId -ne "*")
Assert-True ($explicitIntents.Count -ge 59) "expected at least 59 explicit spirit intent profiles."
Assert-True ($explicitCapture.Count -eq $explicitIntents.Count) "intent and capture profile counts must match."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "*" -and $_.variantId -eq "*" })).Count -eq 1) "intent fallback profile is missing."
Assert-True ((@($capture.profiles | Where-Object { $_.enemyId -eq "*" -and $_.variantId -eq "*" })).Count -eq 1) "capture fallback profile is missing."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "10026" -and $_.variantId -eq "*" })).Count -eq 1) "base-game enemy 10026 must retain its canonical dedicated intent profile."
Assert-True ((@($intent.profiles | Where-Object { $_.enemyId -eq "enemy_10026" })).Count -eq 0) "runtime enemy prefixes must be handled by the shared resolver, not duplicated into registry data."

foreach ($profile in $explicitIntents) {
    Assert-True (@($profile.fallbackAttackTendency).Count -gt 0) "spirit profile $($profile.enemyId) has no attack fallback."
    Assert-True (@($profile.fallbackDefenseTendency).Count -gt 0) "spirit profile $($profile.enemyId) has no defense fallback."
    Assert-True ($profile.attackWeight -gt 0 -and $profile.defenseWeight -gt 0) "spirit profile $($profile.enemyId) has invalid tendency weights."
}

$adaptedSources = @($intent.intents | Where-Object pool -eq "Pve" | ForEach-Object enemyCardId | Sort-Object -Unique)
$pvpSources = @($explicitIntents.pvpSourceEnemyCardIds | ForEach-Object { $_ } | Sort-Object -Unique)
$fallbackSources = @($explicitIntents.fallbackSourceEnemyCardIds | ForEach-Object { $_ } | Sort-Object -Unique)
$allSources = @($explicitIntents.sourceEnemyCardIds | ForEach-Object { $_ } | Sort-Object -Unique)
$classifiedSources = @(($adaptedSources + $pvpSources + $fallbackSources) | Sort-Object -Unique)
Assert-True (@($intent.intents).Count -ge 66) "expected generated PvE composite and PvP reserved spirit intents."
Assert-True ((@($intent.intents | Where-Object pool -eq "Pve").Count) -eq $adaptedSources.Count) "each adapted enemy card must map to exactly one PvE intent."
Assert-True ((@($intent.intents | Where-Object pool -eq "Pve").Count) -eq 54) "expected 54 generated PvE spirit intents."
Assert-True ((@($intent.intents | Where-Object pool -eq "PvpReserved").Count) -eq 12) "expected 12 generated PvP-reserved spirit intents."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" -and @($_.effects).Count -eq 0 })).Count -eq 0) "every PvE spirit intent must declare its authoritative effect list."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" } | ForEach-Object { @($_.effects) } | Where-Object { $_.displayIndex -le 0 })).Count -eq 0) "every PvE effect must bind a positive description placeholder index."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" } | ForEach-Object { @($_.effects) } | Where-Object {
    if ($_.handlerId -eq "buff.apply") { [string]::IsNullOrWhiteSpace([string]$_.buffId) -or [int]$_.buffStacks -le 0 }
    else { [double]$_.flatValue -le 0 -and [double]$_.attackScale -le 0 -and [double]$_.armorScale -le 0 -and [double]$_.magicScale -le 0 }
})).Count -eq 0) "every executable PvE effect must resolve from a positive formula or buff stack count."
foreach ($pveIntent in @($intent.intents | Where-Object pool -eq "Pve")) {
    $indices = @($pveIntent.effects | ForEach-Object { [int]$_.displayIndex } | Sort-Object)
    Assert-True (($indices -join ",") -eq ((1..$indices.Count) -join ",")) "intent $($pveIntent.id) must use contiguous description slots."
}
Assert-True ((@($intent.intents | Where-Object enemyCardId -eq "enemycard_CAR_Shield").effects).Count -eq 2) "multi-buff enemy cards must preserve every supported buff effect."
Assert-True ((@($intent.intents | Where-Object enemyCardId -eq "enemycard_specialAttack").effects).Count -eq 2) "damage-plus-block enemy cards must remain one composite intent."
Assert-True ((Compare-Object $allSources $classifiedSources).Count -eq 0) "every source enemy card must be adapted, PvP-reserved, or explicitly fallback."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "PvpReserved" -and $_.handlerId -ne "pvp.reserved" })).Count -eq 0) "PvP intents must remain on the inert reserved handler."
$expectedPvpSources = @("enemycard_Dragon'sMajesty", "enemycard_EvilCurse", "enemycard_obtainMoney", "enemycard_OriginalSinCard", "enemycard_PlugCards1", "enemycard_PlugCards2", "enemycard_PlugCards3", "enemycard_PowerlessCurse", "enemycard_psychologicalShock", "enemycard_thief", "enemycard_Thieves", "enemycard_VenomSpray") | Sort-Object
$expectedFallbackSources = @("enemycard_Charge1", "enemycard_Charge2", "enemycard_Come", "enemycard_Wake", "enemycard_WhereverYouGo") | Sort-Object
Assert-True (($pvpSources -join "|") -eq ($expectedPvpSources -join "|")) "PvP source reservation set drifted."
Assert-True (($fallbackSources -join "|") -eq ($expectedFallbackSources -join "|")) "unsupported fallback source set drifted."

Write-Host "Spirit content assertions passed: profiles=$($explicitIntents.Count)."
