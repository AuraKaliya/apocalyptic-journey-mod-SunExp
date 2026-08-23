param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$utf8 = [System.Text.Encoding]::UTF8

function Decode-Text([string]$value) {
    return $utf8.GetString([Convert]::FromBase64String($value))
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$career = Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\Career\columbina.csv") -Encoding UTF8 | Select-Object -Last 1
$role = Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\RoleData\columbina.csv") -Encoding UTF8 | Select-Object -Last 1
$skills = @(Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\Card\columbina.csv") -Encoding UTF8 | Select-Object -Skip 1)
$buffs = Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\Buff\terrias.csv") -Encoding UTF8
$buffText = Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Text\Buff\terrias.csv") -Encoding UTF8
$cards = Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Data\Card\terrias.csv") -Encoding UTF8
$cardText = Import-Csv -LiteralPath (Join-Path $repoRoot "Terrias\Text\Card\terrias.csv") -Encoding UTF8

Assert-True ($career.Id -eq "columbina") "Columbina career row is missing."
Assert-True ([int]$career.SanMax -eq 95) "Columbina SanMax must be 95."
Assert-True ($career.DollIcon.StartsWith("DollAni/") -and $career.DollIcon.EndsWith("_0")) "Columbina must reuse the original witch doll."
Assert-True ([string]::IsNullOrWhiteSpace($career.Dialogue)) "Columbina Dialogue must remain empty."
Assert-True ($career.AttackEffect -eq "Hit") "Columbina attacks must use a target-side role effect."
Assert-True ($career.SkillEffect -eq "Hit") "Columbina skills must use a target-side role effect."
Assert-True ($career.HitEffect -eq "Hit") "Columbina hit reactions must use the native Hit effect."
Assert-True ($career.DefendEffect -eq "HitDefend") "Columbina defend reactions must use the native defend effect."
Assert-True ($skills.Count -eq 2) "Columbina must ship exactly two career skill cards."

foreach ($id in @("gravity_ripple", "gravity_value", "moon_domain", "constellation")) {
    Assert-True ($buffs.Id -contains $id) "Missing Columbina buff row: $id"
}
Assert-True (-not ($buffs.Id -contains "new_moon_law")) "New Moon Law is a career passive and must not ship as a Buff row."
Assert-True (-not ($buffText.Id -contains "new_moon_law")) "New Moon Law is a career passive and must not ship as Buff text."

foreach ($buff in @($buffs | Where-Object Id -in @("gravity_ripple", "gravity_value", "moon_domain"))) {
    $relative = $buff.Icon.Replace("Mods/Terrias/", "Terrias\").Replace("/", "\") + ".png"
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $relative)) "Missing dedicated Columbina buff icon: $($buff.Id)"
}

$fateStar = $cards | Where-Object Id -eq "fate_star" | Select-Object -First 1
Assert-True ($null -ne $fateStar) "Fate Star card row is missing."
Assert-True ([int]$fateStar.Expend -eq 1 -and [int]$fateStar.Rarity -eq 3) "Fate Star cost/rarity mismatch."
Assert-True ($fateStar.Tag -eq "Retain,Annihilation") "Fate Star must have Retain and Annihilation."
Assert-True ($fateStar.PackBelong -eq "Terrias_terrias_cardpack_more_dimensions") "Fate Star must belong to More Dimensions."
$fateStarText = $cardText | Where-Object Id -eq "fate_star" | Select-Object -First 1
Assert-True ($fateStarText.Description -eq (Decode-Text "6Iule1RlcnJpYXNfdGVycmlhc19jb25zdGVsbGF0aW9ufeacqui+vuS4iumZkO+8jOeCueS6rjHlsYLvvJvlkKbliJnlm5vlpKfmnKzmupDkuIrpmZDlop7liqAxMOeCueOAgg==")) "Fate Star description must explain its Constellation and origin-cap branches."
Assert-True ($fateStarText.Description_en -eq 'If {Terrias_terrias_constellation} is not complete, light up 1 level; otherwise increase all four Origin caps by 10.') "Fate Star English description must explain its Constellation and origin-cap branches."

$constellationText = $buffText | Where-Object Id -eq "constellation" | Select-Object -First 1
Assert-True ($constellationText.Description -eq (Decode-Text "5q+P54K55Lqu5LiA6aKX5ZG95pif77yM6YO95Lya6I635b6X5LiA5bGC5LiT5bGe5aKe55uK44CC")) "Constellation description mismatch."
Assert-True ($constellationText.'Description_zh-Hant' -eq (Decode-Text "5q+P6bue5Lqu5LiA6aGG5ZG95pif77yM6YO95pyD542y5b6X5LiA5bGk5bCI5bGs5aKe55uK44CC")) "Constellation Traditional Chinese description mismatch."

foreach ($resource in @(
    "Terrias\ModResource\Images\Character\Columbina.png",
    "Terrias\ModResource\Images\Icon\Columbina.png",
    "Terrias\ModResource\Images\Icon\Columbina2.png",
    "Terrias\ModResource\Images\CareerImage\Columbina.png",
    "Terrias\ModResource\Images\Card\MoreDimension\fate_star.png",
    "Terrias\ModResource\AnimationLib\columbina\Idle\config.json"
)) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $resource)) "Missing Columbina resource: $resource"
}

$baseAnimationRoot = Join-Path $repoRoot "Terrias\ModResource\AnimationLib\columbina"
$idleFrame = Join-Path $baseAnimationRoot "Idle\frame_01.png"
$idleHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $idleFrame).Hash
foreach ($state in @("Attack", "Buff", "Debuff", "Defend", "Hit", "Skill", "Special", "Special1", "Special2")) {
    $stateRoot = Join-Path $baseAnimationRoot $state
    $stateFrame = Join-Path $stateRoot ($state + "_00.png")
    Assert-True (Test-Path -LiteralPath (Join-Path $stateRoot "config.json") -PathType Leaf) "Missing Columbina base animation config: $state"
    Assert-True (Test-Path -LiteralPath $stateFrame -PathType Leaf) "Missing Columbina base animation frame: $state"
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $stateFrame).Hash -eq $idleHash) "Columbina placeholder animation must reuse the first Idle frame: $state"
}

foreach ($modPath in @($career.ActionImage1, $career.ActionImage2)) {
    $relative = $modPath.Replace("Mods/Terrias/", "Terrias\").Replace("/", "\") + ".png"
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $relative)) "Missing Columbina skill image from career data."
}

$sharedPackagePath = Join-Path $repoRoot "Terrias\SharedResources\aura.registration.json"
$sharedPackage = Get-Content -Raw -Encoding UTF8 -LiteralPath $sharedPackagePath | ConvertFrom-Json
Assert-True ([int]$sharedPackage.schemaVersion -eq 4) "Columbina resources require AuraShared resource protocol v4."
Assert-True ($sharedPackage.ownerModId -eq "Terrias" -and $sharedPackage.participantKind -eq "Content") "Columbina media must be carried by Terrias and configured by AuraToolsExp."
$sharedPackageRoot = Split-Path -Parent $sharedPackagePath
foreach ($resourceId in @("columbina.homesickness", "columbina.feast")) {
    $resource = $sharedPackage.resources | Where-Object { $_.resourceId -eq $resourceId -and $_.moduleId -eq "CG" -and $_.kind -eq "File" } | Select-Object -First 1
    Assert-True ($null -ne $resource) "Missing Columbina shared CG package resource: $resourceId"
    Assert-True (Test-Path -LiteralPath (Join-Path $sharedPackageRoot $resource.source) -PathType Leaf) "Missing Columbina shared CG source: $($resource.source)"
}

$cgRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "Terrias\SharedResources\cg.registry.json") | ConvertFrom-Json
$homesicknessCg = $cgRegistry.entries | Where-Object { $_.cgId -eq "columbina.homesickness" } | Select-Object -First 1
Assert-True ($null -ne $homesicknessCg -and $homesicknessCg.kind -eq "skill") "Columbina Homesickness skill CG registration is missing."
Assert-True (@($homesicknessCg.targetRoleIds) -contains "Terrias_columbina_columbina") "Columbina Homesickness CG must target the full role id."
Assert-True (@($homesicknessCg.skillIds) -contains "Terrias_columbina_columbina_homesickness") "Columbina Homesickness CG must target the full skill id through schema v3."
Assert-True ($homesicknessCg.defaultActivation.consumerMode -eq "toolManaged" -and $homesicknessCg.defaultActivation.consumerModId -eq "AuraToolsExp") "Columbina Homesickness CG must be managed by AuraToolsExp."
$feastCg = $cgRegistry.entries | Where-Object { $_.cgId -eq "columbina.feast" } | Select-Object -First 1
Assert-True ($null -ne $feastCg -and $feastCg.kind -eq "feast") "Columbina Feast CG registration is missing."
Assert-True ($feastCg.defaultActivation.consumerMode -eq "toolManaged" -and $feastCg.defaultActivation.consumerModId -eq "AuraToolsExp") "Columbina Feast CG must be managed by AuraToolsExp."
Assert-True ([double]$homesicknessCg.defaultPresentation.hold -eq 2.1) "Columbina Homesickness CG must cover the longest voice variant."

$voicePack = $sharedPackage.resources | Where-Object { $_.resourceId -eq "columbina.voice-pack" -and $_.moduleId -eq "Audio" -and $_.kind -eq "Directory" } | Select-Object -First 1
Assert-True ($null -ne $voicePack) "Columbina shared voice pack registration is missing."
$voiceRoot = Join-Path $sharedPackageRoot $voicePack.source
$voiceFiles = @(Get-ChildItem -LiteralPath $voiceRoot -Filter "*.ogg" -File)
Assert-True ($voiceFiles.Count -eq 12) "Columbina voice pack must contain exactly 12 normalized Ogg files."
Assert-True (@(Get-ChildItem -LiteralPath $voiceRoot -Filter "*.mp3" -File).Count -eq 0) "Columbina voice pack must not retain mislabeled MP3 files."

$audioRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "Terrias\SharedResources\audio.registry.json") | ConvertFrom-Json
$expectedVoiceCounts = @{
    "Terrias.Columbina.CareerSelected" = 4
    "Terrias.Columbina.LowHealth" = 2
    "Terrias.Columbina.EternalTide" = 3
    "Terrias.Columbina.Homesickness" = 3
}
foreach ($providerId in $expectedVoiceCounts.Keys) {
    $provider = $audioRegistry.providers | Where-Object { $_.providerId -eq $providerId } | Select-Object -First 1
    Assert-True ($null -ne $provider) "Missing Columbina audio provider: $providerId"
    $paths = @($provider.path) + @($provider.variantPaths)
    Assert-True ($paths.Count -eq $expectedVoiceCounts[$providerId]) "Unexpected Columbina voice variant count: $providerId"
    Assert-True ([double]$provider.gainDb -eq 8) "Columbina voices must use the configured +8 dB provider gain: $providerId"
    foreach ($path in $paths) {
        Assert-True ($path.StartsWith("Shared:Audio/Role/Terrias_columbina_columbina/Voice/Terrias/columbina.voice-pack/content/")) "Columbina voice must resolve through the Terrias content-owned v4 shared scope: $path"
        Assert-True (Test-Path -LiteralPath (Join-Path $voiceRoot ([System.IO.Path]::GetFileName($path))) -PathType Leaf) "Missing declared Columbina voice file: $path"
    }
}

$lowHealthVoice = $audioRegistry.providers | Where-Object { $_.providerId -eq "Terrias.Columbina.LowHealth" } | Select-Object -First 1
Assert-True ([double]$lowHealthVoice.match.hpRatioCrossDown -eq 0.2 -and $lowHealthVoice.match.localOwnerOnly) "Columbina low-health voice must use the local-owner 20% crossing rule."

$behaviorProject = Join-Path $repoRoot "Terrias-Dev.ColumbinaTests\Terrias-Dev.ColumbinaTests.csproj"
dotnet run --project $behaviorProject -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Terrias Columbina behavior tests failed."
}

Write-Host "Terrias Columbina content and behavior assertions passed."
