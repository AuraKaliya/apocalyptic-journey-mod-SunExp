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
$growthPath = Join-Path $repoRoot "Terrias\spirit.growth.registry.json"
$trainingPath = Join-Path $repoRoot "Terrias\spirit.training.registry.json"
$intent = Get-Content -LiteralPath $intentPath -Raw | ConvertFrom-Json
$capture = Get-Content -LiteralPath $capturePath -Raw | ConvertFrom-Json
$growth = Get-Content -LiteralPath $growthPath -Raw | ConvertFrom-Json
$training = Get-Content -LiteralPath $trainingPath -Raw | ConvertFrom-Json
Assert-True ($intent.schemaVersion -eq 3) "spirit intent registry schema must be 3."
Assert-True ($capture.schemaVersion -eq 1) "spirit capture registry schema must be 1."
Assert-True ($growth.schemaVersion -eq 2) "spirit growth registry schema must be 2."
Assert-True ($training.schemaVersion -eq 1) "spirit training registry schema must be 1."
Assert-True ($growth.defaults.maxLevel -eq 50) "spirit level cap must remain 50."

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
$explicitIntentProfileIds = @($intent.profiles | Where-Object enemyId -ne "*" | ForEach-Object { [string]$_.profileId })
Assert-True ((@($explicitIntentProfileIds | Where-Object { [string]::IsNullOrWhiteSpace($_) })).Count -eq 0) "every explicit intent profile must expose a stable profileId."
Assert-True ((@($explicitIntentProfileIds | Sort-Object -Unique)).Count -eq $explicitIntentProfileIds.Count) "intent profileId values must be unique."
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
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" -and ([int]$_.cost -lt 1 -or [int]$_.cost -gt 3) })).Count -eq 0) "every native PvE intent must cost between 1 and 3 Magic."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" -and ([int]$_.cooldown -lt 0 -or [int]$_.cooldown -gt 2) })).Count -eq 0) "every native PvE intent cooldown must stay between 0 and 2."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" -and ([string]::IsNullOrWhiteSpace([string]$_.displayName) -or [string]::IsNullOrWhiteSpace([string]$_.description)) })).Count -eq 0) "every native PvE intent must expose a player-facing name and adapted description."
Assert-True ((@($intent.intents | Where-Object { $_.pool -eq "Pve" -and ([string]$_.description -match "buff_|Terrias_") })).Count -eq 0) "native PvE descriptions must not leak internal buff or mod identifiers into the training UI."
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

$tierRanges = @{
    Normal = @(24, 32, 56, 72)
    Elite = @(32, 40, 72, 88)
    Boss = @(40, 48, 88, 108)
    FinalBoss = @(48, 60, 108, 132)
}
$growthProfileIds = @($growth.profiles | ForEach-Object { [string]$_.profileId })
$growthSpeciesIds = @($growth.profiles | ForEach-Object { [string]$_.speciesId })
Assert-True (@($growth.profiles).Count -eq 58) "growth registry must contain 55 base-game forms and 3 Terrias species."
Assert-True ((@($growthProfileIds | Where-Object { [string]::IsNullOrWhiteSpace($_) })).Count -eq 0) "growth profiles require stable profileId values."
Assert-True ((@($growthSpeciesIds | Where-Object { [string]::IsNullOrWhiteSpace($_) })).Count -eq 0) "growth profiles require stable speciesId values."
Assert-True ((@($growthProfileIds | Sort-Object -Unique)).Count -eq $growthProfileIds.Count) "growth profileId values must be unique."
Assert-True ((@($growth.profiles | Where-Object { $_.match.sourceModId -eq "base-game" })).Count -eq 55) "all 55 reviewed base-game forms must be data-backed."
$identityDiff = @(Compare-Object $growthProfileIds $explicitIntentProfileIds)
Assert-True ($identityDiff.Count -eq 1 -and @($identityDiff | Where-Object InputObject -eq "base-game.99999").Count -eq 1) "growth and intent identities may differ only by the uncapturable test profile base-game.99999."

$requiredDefinitions = @(
    @($growth.levelCurves.id),
    @($growth.aptitudeRollProfiles.id),
    @($growth.aptitudeCurves.id),
    @($growth.experienceCurves.id),
    @($growth.battleConversions.id),
    @($growth.radarScaleSets.id)
)
$defaultReferences = @(
    [string]$growth.defaults.levelCurveId,
    [string]$growth.defaults.aptitudeRollProfileId,
    [string]$growth.defaults.aptitudeCurveId,
    [string]$growth.defaults.experienceCurveId,
    [string]$growth.defaults.battleConversionId,
    [string]$growth.defaults.radarScaleId
)
for ($index = 0; $index -lt $defaultReferences.Count; $index++) {
    Assert-True ($requiredDefinitions[$index] -contains $defaultReferences[$index]) "growth default reference $($defaultReferences[$index]) is unresolved."
}
$radar = @($growth.radarScaleSets | Where-Object id -eq $growth.defaults.radarScaleId)[0]
Assert-True ((@($radar.axes.key) -join ",") -eq "magic,perception,spirit,luck") "radar axes must retain the canonical four-origin order."
Assert-True ((@($radar.axes | Where-Object cap -ne 80)).Count -eq 0) "global radar axes must use the stable cap of 80."

foreach ($profile in @($growth.profiles)) {
    Assert-True ($tierRanges.ContainsKey([string]$profile.tier)) "growth profile $($profile.profileId) has an invalid tier."
    $range = $tierRanges[[string]$profile.tier]
    $baseValues = @([int]$profile.baseOrigins.magic, [int]$profile.baseOrigins.spirit, [int]$profile.baseOrigins.luck, [int]$profile.baseOrigins.perception)
    $growthValues = @([int]$profile.growthOrigins.magic, [int]$profile.growthOrigins.spirit, [int]$profile.growthOrigins.luck, [int]$profile.growthOrigins.perception)
    $baseTotal = ($baseValues | Measure-Object -Sum).Sum
    $growthTotal = ($growthValues | Measure-Object -Sum).Sum
    Assert-True ($baseTotal -ge $range[0] -and $baseTotal -le $range[1]) "growth profile $($profile.profileId) base budget is outside its tier."
    Assert-True ($growthTotal -ge $range[2] -and $growthTotal -le $range[3]) "growth profile $($profile.profileId) growth budget is outside its tier."
    foreach ($value in $baseValues) { Assert-True ($value / $baseTotal -ge 0.10 -and $value / $baseTotal -le 0.45) "growth profile $($profile.profileId) base origin share is invalid." }
    foreach ($value in $growthValues) { Assert-True ($value / $growthTotal -ge 0.10 -and $value / $growthTotal -le 0.45) "growth profile $($profile.profileId) growth origin share is invalid." }
}
Assert-True ((@($growth.profiles | Where-Object { $_.match.enemyId -eq "boss_second_sun_last_day" -and $_.tier -eq "FinalBoss" })).Count -eq 1) "Second Sun must use the explicit final-boss tier."
Assert-True ((@($growth.profiles | Where-Object { $_.match.enemyId -eq "boss_saint_wuna" -and $_.tier -eq "FinalBoss" })).Count -eq 1) "Saint Wuna must use the explicit final-boss tier."

$multiFormSpecies = @($growth.profiles | Group-Object speciesId | Where-Object Count -gt 1)
Assert-True ($multiFormSpecies.Count -eq 5) "the five reviewed multi-form species must share speciesId while keeping fixed profileId forms."
foreach ($group in $multiFormSpecies) {
    Assert-True ((@($group.Group.formKey | Sort-Object -Unique)).Count -eq $group.Count) "multi-form species $($group.Name) has duplicate form keys."
    Assert-True ((@($group.Group.formOrder | Sort-Object -Unique)).Count -eq $group.Count) "multi-form species $($group.Name) has duplicate form order values."
}

$commonIntents = @($training.commonIntents)
$commonPassives = @($training.passives | Where-Object pool -like "Common.*")
$speciesPassives = @($training.passives | Where-Object pool -eq "Species")
Assert-True ($commonIntents.Count -eq 15) "training registry must contain exactly 15 common intents."
Assert-True ((@($commonIntents | Group-Object pool | Where-Object { $_.Count -ne 5 })).Count -eq 0) "basic, tactical, and advanced common intent pools must each contain five intents."
Assert-True ((@($commonIntents.pool | Sort-Object -Unique) -join ",") -eq "Common.Advanced,Common.Basic,Common.Tactical") "common intent pool names drifted."
Assert-True ($commonPassives.Count -eq 12) "training registry must contain exactly 12 common passives."
Assert-True ((@($commonPassives | Where-Object pool -eq "Common.Core").Count) -eq 8) "common core passive pool must contain eight passives."
Assert-True ((@($commonPassives | Where-Object pool -eq "Common.Advanced").Count) -eq 4) "common advanced passive pool must contain four passives."
Assert-True ($speciesPassives.Count -eq (@($growth.profiles.speciesId | Sort-Object -Unique)).Count) "every species must own exactly one inherent passive."
Assert-True ((@($training.speciesProfiles).Count) -eq @($growth.profiles).Count) "every growth profile must own one training profile."
Assert-True ((Compare-Object @($training.speciesProfiles.profileId | Sort-Object) @($growth.profiles.profileId | Sort-Object)).Count -eq 0) "training profile identities must match growth profile identities."
Assert-True ((@($training.speciesProfiles | Where-Object { $_.profileId -ne "base-game.99999" -and (@($_.defaultIntentIds).Count -lt 1 -or @($_.defaultIntentIds).Count -gt 3) })).Count -eq 0) "every capturable training profile must equip one to three default native intents."
Assert-True ((@($commonIntents | Where-Object { [int]$_.cost -lt 0 -or [int]$_.cost -gt 3 -or [int]$_.cooldown -lt 0 -or [int]$_.cooldown -gt 2 })).Count -eq 0) "common intent cost and cooldown values are out of contract."
Assert-True ((@($commonIntents | Where-Object id -eq "spirit.common.advanced.swift-pierce.intent").speedScale) -eq 0.08) "Swift Pierce must remain the only speed-scaled common intent baseline."
Assert-True ((@($commonIntents | Where-Object { $_.id -ne "spirit.common.advanced.swift-pierce.intent" -and [double]$_.speedScale -ne 0 })).Count -eq 0) "only Swift Pierce may use SpeedScale in the first release."

$enemyCardData = @(Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\EnemyCard\terrias.csv"))
$enemyCardText = @(Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Text\EnemyCard\terrias.csv"))
foreach ($common in $commonIntents) {
    $shortId = ([string]$common.enemyCardId).Replace("Terrias_terrias_", "")
    Assert-True ((@($enemyCardData | Where-Object Id -eq $shortId).Count) -eq 1) "common intent $($common.id) is missing its EnemyCard data row."
    Assert-True ((@($enemyCardText | Where-Object Id -eq $shortId).Count) -eq 1) "common intent $($common.id) is missing its EnemyCard text row."
}

Write-Host "Spirit content assertions passed: profiles=$($explicitIntents.Count), growthProfiles=$(@($growth.profiles).Count), commonIntents=$($commonIntents.Count), passives=$(@($training.passives).Count)."
