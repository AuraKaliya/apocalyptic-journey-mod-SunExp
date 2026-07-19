param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$utf8 = [System.Text.Encoding]::UTF8

function Decode-Text([string]$value) {
    return $utf8.GetString([Convert]::FromBase64String($value))
}

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$career = Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\Career\columbina.csv") -Encoding UTF8 | Select-Object -Last 1
$role = Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\RoleData\columbina.csv") -Encoding UTF8 | Select-Object -Last 1
$skills = @(Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\Card\columbina.csv") -Encoding UTF8 | Select-Object -Skip 1)
$buffs = Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\Buff\sunexp.csv") -Encoding UTF8
$buffText = Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Text\Buff\sunexp.csv") -Encoding UTF8
$cards = Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Data\Card\sunexp.csv") -Encoding UTF8
$cardText = Import-Csv -LiteralPath (Join-Path $repoRoot "SunExp\Text\Card\sunexp.csv") -Encoding UTF8

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
    $relative = $buff.Icon.Replace("Mods/SunExp/", "SunExp\").Replace("/", "\") + ".png"
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $relative)) "Missing dedicated Columbina buff icon: $($buff.Id)"
}

$fateStar = $cards | Where-Object Id -eq "fate_star" | Select-Object -First 1
Assert-True ($null -ne $fateStar) "Fate Star card row is missing."
Assert-True ([int]$fateStar.Expend -eq 1 -and [int]$fateStar.Rarity -eq 3) "Fate Star cost/rarity mismatch."
Assert-True ($fateStar.Tag -eq "Retain,Annihilation") "Fate Star must have Retain and Annihilation."
Assert-True ($fateStar.PackBelong -eq "SunExp_sunexp_cardpack_more_dimensions") "Fate Star must belong to More Dimensions."
$fateStarText = $cardText | Where-Object Id -eq "fate_star" | Select-Object -First 1
Assert-True ($fateStarText.Description -eq (Decode-Text "54K55LquMeWxgntTdW5FeHBfc3VuZXhwX2NvbnN0ZWxsYXRpb25944CC")) "Fate Star description must only describe lighting Constellation."
Assert-True ($fateStarText.Description_en -eq 'Light up 1 level of {SunExp_sunexp_constellation}.') "Fate Star English description must not repeat Retain or Annihilation."

$constellationText = $buffText | Where-Object Id -eq "constellation" | Select-Object -First 1
Assert-True ($constellationText.Description -eq (Decode-Text "5q+P54K55Lqu5LiA6aKX5ZG95pif77yM6YO95Lya6I635b6X5LiA5bGC5LiT5bGe5aKe55uK44CC")) "Constellation description mismatch."
Assert-True ($constellationText.'Description_zh-Hant' -eq (Decode-Text "5q+P6bue5Lqu5LiA6aGG5ZG95pif77yM6YO95pyD542y5b6X5LiA5bGk5bCI5bGs5aKe55uK44CC")) "Constellation Traditional Chinese description mismatch."

foreach ($resource in @(
    "SunExp\ModResource\Images\Character\Columbina.png",
    "SunExp\ModResource\Images\Icon\Columbina.png",
    "SunExp\ModResource\Images\Icon\Columbina2.png",
    "SunExp\ModResource\Images\CareerImage\Columbina.png",
    "SunExp\ModResource\Images\Card\MoreDimension\fate_star.png",
    "SunExp\ModResource\AnimationLib\columbina\Idle\config.json"
)) {
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $resource)) "Missing Columbina resource: $resource"
}

$baseAnimationRoot = Join-Path $repoRoot "SunExp\ModResource\AnimationLib\columbina"
$idleFrame = Join-Path $baseAnimationRoot "Idle\matte_00001.png"
$idleHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $idleFrame).Hash
foreach ($state in @("Attack", "Buff", "Debuff", "Defend", "Hit", "Skill", "Special", "Special1", "Special2")) {
    $stateRoot = Join-Path $baseAnimationRoot $state
    $stateFrame = Join-Path $stateRoot ($state + "_00.png")
    Assert-True (Test-Path -LiteralPath (Join-Path $stateRoot "config.json") -PathType Leaf) "Missing Columbina base animation config: $state"
    Assert-True (Test-Path -LiteralPath $stateFrame -PathType Leaf) "Missing Columbina base animation frame: $state"
    Assert-True ((Get-FileHash -Algorithm SHA256 -LiteralPath $stateFrame).Hash -eq $idleHash) "Columbina placeholder animation must reuse the first Idle frame: $state"
}

foreach ($modPath in @($career.ActionImage1, $career.ActionImage2)) {
    $relative = $modPath.Replace("Mods/SunExp/", "SunExp\").Replace("/", "\") + ".png"
    Assert-True (Test-Path -LiteralPath (Join-Path $repoRoot $relative)) "Missing Columbina skill image from career data."
}

$sharedPackagePath = Join-Path $repoRoot "SunExp\SharedResources\package.json"
$sharedPackage = Get-Content -Raw -Encoding UTF8 -LiteralPath $sharedPackagePath | ConvertFrom-Json
Assert-True ([int]$sharedPackage.packageVersion -ge 10) "Columbina voice resources require SunExp shared package version 10 or newer."
$sharedPackageRoot = Split-Path -Parent $sharedPackagePath
foreach ($resourceId in @("SunExp.Columbina.Homesickness.SkillCg", "SunExp.Columbina.FeastCg")) {
    $resource = $sharedPackage.resources | Where-Object { $_.resourceId -eq $resourceId -and $_.system -eq "CG" -and $_.kind -eq "File" } | Select-Object -First 1
    Assert-True ($null -ne $resource) "Missing Columbina shared CG package resource: $resourceId"
    Assert-True (Test-Path -LiteralPath (Join-Path $sharedPackageRoot $resource.source) -PathType Leaf) "Missing Columbina shared CG source: $($resource.source)"
}

$cgRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "SunExp\SharedResources\cg.registry.json") | ConvertFrom-Json
$homesicknessCg = $cgRegistry.entries | Where-Object { $_.cgId -eq "columbina.homesickness" } | Select-Object -First 1
Assert-True ($null -ne $homesicknessCg -and $homesicknessCg.kind -eq "skill") "Columbina Homesickness skill CG registration is missing."
Assert-True (@($homesicknessCg.targetRoleIds) -contains "SunExp_columbina_columbina") "Columbina Homesickness CG must target the full role id."
Assert-True (@($homesicknessCg.cardIds) -contains "SunExp_columbina_columbina_homesickness") "Columbina Homesickness CG must target the full skill card id."
Assert-True ($homesicknessCg.defaultActivation.consumerMode -eq "contentOwned") "Columbina Homesickness CG must be content-owned."
$feastCg = $cgRegistry.entries | Where-Object { $_.cgId -eq "columbina.feast" } | Select-Object -First 1
Assert-True ($null -ne $feastCg -and $feastCg.kind -eq "feast") "Columbina Feast CG registration is missing."
Assert-True ($feastCg.defaultActivation.consumerMode -eq "toolManaged" -and $feastCg.defaultActivation.consumerModId -eq "AuraToolsExp") "Columbina Feast CG must be managed by AuraToolsExp."
Assert-True ([double]$homesicknessCg.defaultPresentation.hold -eq 2.1) "Columbina Homesickness CG must cover the longest voice variant."

$voicePack = $sharedPackage.resources | Where-Object { $_.resourceId -eq "SunExp.Columbina.VoicePack" -and $_.system -eq "Audio" -and $_.kind -eq "Directory" } | Select-Object -First 1
Assert-True ($null -ne $voicePack) "Columbina shared voice pack registration is missing."
$voiceRoot = Join-Path $sharedPackageRoot $voicePack.source
$voiceFiles = @(Get-ChildItem -LiteralPath $voiceRoot -Filter "*.ogg" -File)
Assert-True ($voiceFiles.Count -eq 12) "Columbina voice pack must contain exactly 12 normalized Ogg files."
Assert-True (@(Get-ChildItem -LiteralPath $voiceRoot -Filter "*.mp3" -File).Count -eq 0) "Columbina voice pack must not retain mislabeled MP3 files."

$audioRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot "SunExp\audio.registry.json") | ConvertFrom-Json
$expectedVoiceCounts = @{
    "SunExp.Columbina.CareerSelected" = 4
    "SunExp.Columbina.LowHealth" = 2
    "SunExp.Columbina.EternalTide" = 3
    "SunExp.Columbina.Homesickness" = 3
}
foreach ($providerId in $expectedVoiceCounts.Keys) {
    $provider = $audioRegistry.providers | Where-Object { $_.providerId -eq $providerId } | Select-Object -First 1
    Assert-True ($null -ne $provider) "Missing Columbina audio provider: $providerId"
    $paths = @($provider.path) + @($provider.variantPaths)
    Assert-True ($paths.Count -eq $expectedVoiceCounts[$providerId]) "Unexpected Columbina voice variant count: $providerId"
    Assert-True ([double]$provider.gainDb -eq 8) "Columbina voices must use the configured +8 dB provider gain: $providerId"
    foreach ($path in $paths) {
        Assert-True ($path.StartsWith("Shared:Audio/Role/SunExp_columbina_columbina/Voice/SunExp/columbina.voice-pack/content/")) "Columbina voice must resolve through the v3 shared scope: $path"
        Assert-True (Test-Path -LiteralPath (Join-Path $voiceRoot ([System.IO.Path]::GetFileName($path))) -PathType Leaf) "Missing declared Columbina voice file: $path"
    }
}

$lowHealthVoice = $audioRegistry.providers | Where-Object { $_.providerId -eq "SunExp.Columbina.LowHealth" } | Select-Object -First 1
Assert-True ([double]$lowHealthVoice.match.hpRatioCrossDown -eq 0.2 -and $lowHealthVoice.match.localOwnerOnly) "Columbina low-health voice must use the local-owner 20% crossing rule."

$reaction = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\LunarReactionService.cs"))
Assert-True ($reaction.Contains("LunarReactionRules.ElectroChargedDamage")) "Lunar Electro-Charged is not wired."
Assert-True ($reaction.Contains("StatusApi.Defence(source)")) "Lunar Crystallize must snapshot current shield."

$cardScripts = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Scripting\CardScripts.cs"))
Assert-True ($cardScripts.Contains('[SunExpIds.FateStarCardShortId] = InitFateStar')) "Fate Star must use its annihilating initializer."
Assert-True ($cardScripts.Contains('private static void InitFateStar')) "Fate Star initializer is missing."
Assert-True ($cardScripts.Contains('CardApi.MarkForAdventureRemoval(self?.dataConfig);')) "Fate Star must set the native adventure-removal marker."

$columbinaMechanics = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\ColumbinaMechanics.cs"))
Assert-True ($columbinaMechanics.Contains('DamageApi.CreateCardSourceExecutor')) "Status-triggered Columbina damage must use a configured native source executor."
Assert-True (-not $columbinaMechanics.Contains('actor!.MirrorSc as ScriptExecutor')) "Columbina damage must not borrow the career MirrorSc executor."
Assert-True ($columbinaMechanics.Contains('BuffApi.Level(actor, SunExpIds.GravityRipple) <= 0')) "Gravity Ripple must be gated by its Buff state."
Assert-True (-not $columbinaMechanics.Contains('ColumbinaPassiveService.IsActive(actor)')) "Gravity Ripple must remain active while its owner is Polymorphed away from Columbina."

$passiveService = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\ColumbinaPassiveService.cs"))
Assert-True ($passiveService.Contains('StatusApi.RoleId(status)')) "Columbina passive identity must resolve from the triggering status."
Assert-True ($passiveService.Contains('PolymorphStateStore.IsRoleSuppressedFor')) "Columbina passive identity must respect Polymorph suppression."

$columbinaScripts = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Scripting\ColumbinaScripts.cs"))
Assert-True (-not $columbinaScripts.Contains('NewMoonLaw')) "Columbina career initialization must not add a New Moon Law Buff."
Assert-True ($columbinaScripts.Contains('AudioApi.PlayColumbinaEternalTide();')) "Eternal Tide must play its voice after a successful cooldown check."
Assert-True ($columbinaScripts.Contains('AudioApi.PlayColumbinaHomesickness();')) "Homesickness must play its voice after a successful cooldown check."

$actionPresentationCatalog = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\RoleActionPresentationCatalog.cs"))
Assert-True ($actionPresentationCatalog.Contains('columbina_homesickness') -and $actionPresentationCatalog.Contains('RoleActionTargetMode.AllOpponents')) "Homesickness must be registered as an all-opponent presentation action."
Assert-True ($actionPresentationCatalog.Contains('columbina_eternal_tide') -and $actionPresentationCatalog.Contains('RoleActionTargetMode.SelfOnly')) "Eternal Tide must be registered as a self-only presentation action."
Assert-True ($actionPresentationCatalog.Contains('IsColumbinaRole')) "The shared role presentation catalog must recognize Columbina."
Assert-True ($actionPresentationCatalog.Contains("return normalized.TrimStart('*');")) "Action card ids must normalize generated-card asterisks after full mod prefixes."

$actionAnimationRuntime = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Hooks\RoleActionAnimationRuntime.cs"))
$allOpponentBranch = $actionAnimationRuntime.IndexOf('targetMode == RoleActionTargetMode.AllOpponents', [StringComparison]::Ordinal)
$existingTargetFastPath = $actionAnimationRuntime.IndexOf('var hasNonSelfTarget', [StringComparison]::Ordinal)
Assert-True ($allOpponentBranch -ge 0 -and $allOpponentBranch -lt $existingTargetFastPath) "All-opponent presentation rules must run before the existing-target fast path."
Assert-True ($actionAnimationRuntime.Contains('executor.SetStatus("AllTarget")')) "All-opponent actions must restore the native enemy target set before presentation."
Assert-True ($actionAnimationRuntime.Contains('currentTargets.Add(executor.Target)')) "Single-target actions must restore the selected native target before presentation."
Assert-True ($actionAnimationRuntime.Contains('currentTargets.RemoveAll(target => !IsValidNonSelfTarget(self, target))')) "Target-side effects must remove the actor from mixed presentation target sets."
Assert-True ($actionAnimationRuntime.Contains('targetMode == RoleActionTargetMode.SelfOnly') -and $actionAnimationRuntime.Contains('currentTargets.Clear();')) "Self-only actions must not target their own hit effect."

$runtimeHooks = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Hooks\RuntimeHooks.cs"))
Assert-True ($runtimeHooks.Contains('RoleActionAnimationRuntime.Initialize(modConfig)')) "The shared role action animation runtime must be initialized."

$audioApi = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\GameApi\AudioApi.cs"))
Assert-True ($audioApi.Contains('public static void PlayColumbinaEternalTide()')) "AudioApi must expose Columbina Eternal Tide playback."
Assert-True ($audioApi.Contains('public static void PlayColumbinaHomesickness()')) "AudioApi must expose Columbina Homesickness playback."

$constellation = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\ConstellationService.cs"))
Assert-True ($constellation.Contains('SetExactLevelWithNativeRefresh')) "Constellation levels must use the native refresh path."
Assert-True (-not $constellation.Contains('ColumbinaPassiveService.IsActive(status)')) "Columbina constellation effects must follow the bound adventure role, not the temporary Polymorph role."
Assert-True ($constellation.Contains('BindAdventureRole(status, roleId, overwrite: false)')) "Constellation application must bind the adventure role before native Buff creation."
Assert-True ($constellation.Contains('PolymorphStateStore.ActiveFor(status)')) "Constellation identity must recover the original role from an active Polymorph."
Assert-True ($constellation.Contains('activePolymorph?.OriginalCareerId')) "Constellation identity must not bind to the temporary Polymorph role."
Assert-True ($constellation.Contains('StorageKeyForPool(poolId)')) "Constellation persistence must be keyed by pool rather than the current combat role."
Assert-True ($constellation.Contains('LegacyStorageKeyForRole(roleId)')) "Constellation persistence must migrate valid legacy role progress."
Assert-True ($constellation.Contains('ConstellationPoolCatalog.ColumbinaPoolId')) "Columbina constellation effects must verify the bound adventure pool."
Assert-True ($constellation.Contains('MatchesAdventureRole')) "Constellation identity must expose an authoritative snapshot-role check."
Assert-True ($constellation.Contains('SunExpStatusOwnershipPolicy.SenderOwnsStatus')) "Constellation requests must validate the bound sender against the submitted status."
Assert-True ($constellation.Contains('SyncDomain.TryClaimToken(sender.PlayerId, token)')) "Constellation requests must suppress duplicate command tokens per sender."
Assert-True ($constellation.Contains('PlayerApi.SetScopedGameVarForScope')) "The host must persist remote constellation progress in the owning status scope."
Assert-True ($constellation.Contains('ApplyRoundReward')) "Traveler constellation six must be applied by each player's local owner."
Assert-True ($constellation.Contains('local!.AddBuff(SunExpIds.Extraordinary, 300)')) "Traveler constellation six must grant 300 Extraordinary to the local player."
Assert-True ($constellation.Contains('BuffApi.ApplyRuntimePresentation')) "Constellation application must update its live Buff instance presentation."
Assert-True ($constellation.Contains('BuffApi.PrepareRuntimePresentation')) "Constellation creation must prepare a per-instance native Buff presentation."

$constellationIdentity = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\ConstellationIdentityRules.cs"))
Assert-True ($constellationIdentity.Contains('boundAdventureRole')) "Constellation identity rules must prefer an immutable adventure-role binding."
Assert-True ($constellationIdentity.Contains('polymorphOriginalRole')) "Constellation identity rules must expose the original Polymorph role fallback."

$elementalReaction = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\ElementalReactionService.cs"))
Assert-True ($elementalReaction.Contains('ShouldAttachIncomingElement(plan.HasReaction)')) "Elemental hits must use the post-hit attachment rule."
Assert-True (-not $elementalReaction.Contains('if (!plan.HasReaction && StatusApi.IsAlive(target))')) "Lethal elemental hits must not lose attachment before native Rebirth."

$constellationRpc = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Network\RpcConstellationStateCommit.cs"))
Assert-True ($constellationRpc.Contains('ConstellationService.TryResolveLightUpRequest')) "Fate Star must submit a host-resolved increment request."
Assert-True (-not $constellationRpc.Contains('snapshot.Level > ConstellationService.Level')) "Fate Star RPC must not trust a client-provided absolute constellation level."
Assert-True ($constellationRpc.Contains('RpcConstellationRosterSnapshot')) "Constellation must expose a battle-start roster repair snapshot."
Assert-True ($constellationRpc.Contains('RpcConstellationRoundReward')) "Traveler constellation six must use a host-authorized round reward event."

$ownershipPolicy = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Network\SunExpStatusOwnershipPolicy.cs"))
Assert-True ($ownershipPolicy.Contains('string.Equals(playerId, ownerStatusId, StringComparison.Ordinal)')) "Player status ids that equal the bound sender id must be accepted directly."
Assert-True ($ownershipPolicy.Contains('RoleStatusMap')) "Status ownership must retain the native ownership-map fallback."

$directorRuntime = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Features\Director\SunExpDirectorRuntime.cs"))
Assert-True ($directorRuntime.Contains('CompanionFriendlyRosterService.Snapshot(includeControlled: false)')) "Battle opening must enumerate the complete player roster without controlled companions."
Assert-True (-not $directorRuntime.Contains('CreateActor(localPlayer')) "Battle opening must not render only the local player."

$constellationCatalog = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Mechanics\ConstellationPoolCatalog.cs"))
Assert-True ($constellationCatalog.Contains('RoleToPoolId')) "Constellation pools must be anchored by an explicit role lookup table."
Assert-True ($constellationCatalog.Contains('TravelerPoolId = "traveler"')) "The generic constellation fallback must use the Traveler pool."
Assert-True ($constellationCatalog.Contains('ColumbinaPoolId = "columbina"')) "Columbina must have a dedicated constellation pool."
Assert-True ($constellationCatalog.Contains('Constellation - Traveler') -and $constellationCatalog.Contains('Constellation - Lunar Dove')) "Constellation Buff names must include both localized pool names."
Assert-True ($constellationCatalog.Contains('Name_zh-Hant') -and $constellationCatalog.Contains('Description_en') -and $constellationCatalog.Contains('Description_ja')) "Constellation pool presentation must cover all shipped locales."
Assert-True ($constellationCatalog.Contains('string.Join(Environment.NewLine')) "Constellation tier descriptions must render one tier per line."
Assert-True (-not $constellationCatalog.Contains('IndexOf("columbina"')) "Constellation role matching must not use substring identity checks."

$buffApiSource = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\GameApi\BuffApi.cs"))
Assert-True ($buffApiSource.Contains('ApplyRuntimePresentation(')) "BuffApi must own live Buff presentation mutation."
Assert-True ($buffApiSource.Contains('DictionaryUtil.Set(config.Vars')) "Dynamic Buff presentation must write through instance Vars."
Assert-True ($buffApiSource.Contains('new DataConfig(') -and $buffApiSource.Contains('mergedData')) "Dynamic Buff names must use a merged per-instance DataConfig instead of mutating base data."

$columbinaRuntime = [System.IO.File]::ReadAllText((Join-Path $repoRoot "SunExp-Dev\Hooks\ColumbinaRuntime.cs"))
Assert-True ($columbinaRuntime.Contains('SunExpHookRegistry.Before(') -and $columbinaRuntime.Contains('"BuffItem.Init"') -and $columbinaRuntime.Contains('ConstellationService.PreparePresentation')) "Constellation presentation must be prepared before native Buff UI initialization."

Write-Host "SunExp Columbina assertions passed."
