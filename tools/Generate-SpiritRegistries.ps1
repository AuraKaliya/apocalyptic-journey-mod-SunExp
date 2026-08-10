param(
    [string]$BaseEnemyCsv = (Join-Path $PSScriptRoot "..\apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Data\Enemy\enemy.csv"),
    [string]$TerriasEnemyCsv = (Join-Path $PSScriptRoot "..\Terrias\Data\Enemy\terrias.csv"),
    [string]$BaseEnemyCardCsv = (Join-Path $PSScriptRoot "..\apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Data\EnemyCard\enemycard.csv"),
    [string]$TerriasEnemyCardCsv = (Join-Path $PSScriptRoot "..\Terrias\Data\EnemyCard\terrias.csv"),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\Terrias")
)

$ErrorActionPreference = "Stop"

function Get-Rows([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "CSV not found: $Path" }
    @(Import-Csv -LiteralPath $Path | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Id) -and $_.Id -ne "Id"
    })
}

function Get-CardKey([string]$Id) {
    $value = ([string]$Id).Trim().TrimStart('*')
    if ($value -match '(?i)enemycard_(.+)$') { return $Matches[1].ToLowerInvariant() }
    $value.ToLowerInvariant()
}

function Get-SpiritProfileId([string]$EnemyId) {
    $raw = ([string]$EnemyId).Trim()
    if ($raw -eq '*') { return '' }
    $id = $raw.TrimStart('*')
    if ($id -match '^\d+$') { return "base-game.$id" }
    "terrias.$id"
}

function Get-Number([string]$Script, [string]$Name, [int]$Default) {
    if (([string]$Script) -match ('Vars\["' + [regex]::Escape($Name) + '"\]\s*=\s*"(\d+)"')) {
        return [int]$Matches[1]
    }
    $Default
}

function New-Target([string]$Scope, [string]$Mode, [string]$Policy) {
    [ordered]@{ scope = $Scope; mode = $Mode; policy = $Policy }
}

function New-Intent(
    [string]$Id, [string]$SourceCardId, [string]$Pool, [string]$Note,
    [string]$Type, [string]$Handler, $Target, [int]$Cooldown, [int]$Priority,
    [int]$HitCount = 1, [string]$BuffId = '', [int]$BuffStacks = 0,
    [int]$FlatValue = 0, [double]$AttackScale = 0, [double]$ArmorScale = 0, [double]$MagicScale = 0
) {
    [ordered]@{
        id = $Id
        enemyCardId = $SourceCardId
        pool = $Pool
        adaptationNote = $Note
        type = $Type
        cost = 0
        cooldown = [Math]::Max(0, $Cooldown)
        basePriority = [Math]::Max(1, $Priority)
        handlerId = $Handler
        target = $Target
        hitCount = [Math]::Max(1, $HitCount)
        buffId = $BuffId
        buffStacks = [Math]::Max(0, $BuffStacks)
        flatValue = [Math]::Max(0, $FlatValue)
        attackScale = $AttackScale
        armorScale = $ArmorScale
        magicScale = $MagicScale
        priorityBonus = ''
        threat = [ordered]@{ preview = 0; onUse = 0; decay = 4 }
    }
}

function Write-JsonDocument($Document, [string]$Path, [string]$LineEnding) {
    $json = $Document | ConvertTo-Json -Depth 12 -Compress
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) { return }

    @'
import json
import sys
from pathlib import Path

path = Path(sys.argv[1])
data = json.loads(path.read_text(encoding="utf-8-sig"))
newline = "\r\n" if sys.argv[2].lower() == "crlf" else "\n"
text = json.dumps(data, ensure_ascii=False, indent=2).replace("\n", newline) + newline
with path.open("w", encoding="utf-8", newline="") as stream:
    stream.write(text)
'@ | & $python.Source - $Path $LineEnding
    if ($LASTEXITCODE -ne 0) { throw "Failed to format generated JSON: $Path" }
}

$enemyRows = @((Get-Rows $BaseEnemyCsv) + (Get-Rows $TerriasEnemyCsv))
$cardRows = @((Get-Rows $BaseEnemyCardCsv) + (Get-Rows $TerriasEnemyCardCsv))
$cardByKey = @{}
foreach ($row in $cardRows) { $cardByKey[(Get-CardKey ([string]$row.Id))] = $row }

$pvpKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@("dragon'smajesty", 'evilcurse', 'originalsincard', 'plugcards1', 'plugcards2', 'plugcards3',
  'powerlesscurse', 'thief', 'thieves', 'obtainmoney', 'psychologicalshock', 'venomspray') |
    ForEach-Object { [void]$pvpKeys.Add($_) }
$fallbackKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@('charge1', 'charge2', 'come', 'wake', 'whereveryougo') | ForEach-Object { [void]$fallbackKeys.Add($_) }
$intentMap = @{}

function Add-Intent($Intent) {
    $script:intentMap[[string]$Intent.id] = $Intent
}

function Add-SpecialBossIntents([string]$Key, [string]$Source, [int]$Cooldown, [int]$Priority, $Attack, $Defense) {
    $prefix = 'spirit.pve.' + (($Source -replace '[^A-Za-z0-9]+', '_').Trim('_').ToLowerInvariant())
    switch ($Key) {
        'mirror_calibration' {
            $id = "$prefix.burn_all"; Add-Intent (New-Intent $id $Source 'Pve' 'all-enemy burn adapted from mirror calibration' 'Interference' 'buff.apply' (New-Target 'Enemy' 'All' 'enemy.all') $Cooldown $Priority 1 'buff_burn' 5); $Attack.Add($id)
            $id = "$prefix.block"; Add-Intent (New-Intent $id $Source 'Pve' 'self block redirected to the owner' 'Defense' 'block.single' (New-Target 'Friendly' 'Single' 'friendly.owner_or_self_defense') $Cooldown $Priority 1 '' 0 10); $Defense.Add($id)
        }
        'orbit_refraction' {
            $id = "$prefix.damage"; Add-Intent (New-Intent $id $Source 'Pve' 'damage component; burn trigger remains safely omitted' 'Attack' 'damage.single' (New-Target 'Enemy' 'Single' 'enemy.lowest_hp') $Cooldown $Priority 1 '' 0 8 0.9); $Attack.Add($id)
            $id = "$prefix.burn"; Add-Intent (New-Intent $id $Source 'Pve' 'burn component adapted without native boss trigger' 'Interference' 'buff.apply' (New-Target 'Enemy' 'Single' 'enemy.lowest_hp') $Cooldown $Priority 1 'buff_burn' 10); $Attack.Add($id)
        }
        'last_day_morning_prayer' {
            $id = "$prefix.burn_all"; Add-Intent (New-Intent $id $Source 'Pve' 'all-enemy burn adapted from morning prayer' 'Interference' 'buff.apply' (New-Target 'Enemy' 'All' 'enemy.all') $Cooldown $Priority 1 'buff_burn' 5); $Attack.Add($id)
            $id = "$prefix.gathered_flame"; Add-Intent (New-Intent $id $Source 'Pve' 'self gathered flame redirected to the owner' 'Support' 'buff.apply' (New-Target 'Friendly' 'Single' 'friendly.owner_or_self_defense') $Cooldown $Priority 1 'Terrias_terrias_gathered_flame' 10); $Defense.Add($id)
        }
        'last_day_noon_burn' {
            $id = "$prefix.damage"; Add-Intent (New-Intent $id $Source 'Pve' 'primary damage preserved; boss phase trigger omitted' 'Attack' 'damage.single' (New-Target 'Enemy' 'Single' 'enemy.lowest_hp') $Cooldown $Priority 1 '' 0 12 1.0); $Attack.Add($id)
        }
        'saint_purification' {
            $id = "$prefix.damage"; Add-Intent (New-Intent $id $Source 'Pve' 'primary damage preserved; global coronation logic omitted' 'Attack' 'damage.single' (New-Target 'Enemy' 'Single' 'enemy.lowest_hp') $Cooldown $Priority 1 '' 0 10 0.9); $Attack.Add($id)
            $id = "$prefix.body_burn"; Add-Intent (New-Intent $id $Source 'Pve' 'body burn component adapted safely' 'Interference' 'buff.apply' (New-Target 'Enemy' 'Single' 'enemy.lowest_hp') $Cooldown $Priority 1 'Terrias_terrias_body_burn' 2); $Attack.Add($id)
        }
        'saint_return_to_court' {
            $id = "$prefix.damage"; Add-Intent (New-Intent $id $Source 'Pve' 'primary damage preserved; saved-name global mechanic omitted' 'Attack' 'damage.single' (New-Target 'Enemy' 'Single' 'enemy.lowest_hp') $Cooldown $Priority 1 '' 0 9 0.85); $Attack.Add($id)
        }
    }
}

function Get-IntentProfile($Row) {
    $enemyId = ([string]$Row.Id).Trim().TrimStart('*')
    $cards = @(([string]$Row.CardList).Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $pveAttack = [System.Collections.Generic.List[string]]::new()
    $pveDefense = [System.Collections.Generic.List[string]]::new()
    $pvpAttack = [System.Collections.Generic.List[string]]::new()
    $pvpDefense = [System.Collections.Generic.List[string]]::new()
    $pvpSources = [System.Collections.Generic.List[string]]::new()
    $fallbackSources = [System.Collections.Generic.List[string]]::new()

    foreach ($source in $cards) {
        $key = Get-CardKey $source
        $row = $cardByKey[$key]
        if ($null -eq $row) { $fallbackSources.Add($source); continue }
        $init = [string]$row.InitScript
        $targetScript = [string]$row.TargetScript
        $use = [string]$row.UseScript
        $cooldown = Get-Number $init 'CD' 0
        $priority = Get-Number $init 'priority' 1
        $safe = (($source -replace '[^A-Za-z0-9]+', '_').Trim('_').ToLowerInvariant())
        $prefix = "spirit.pve.$safe"
        $allTarget = $targetScript -match '(?i)(SetStatus|Target)\s*\([^\)]*"All"'

        if ($key -in @('mirror_calibration','orbit_refraction','last_day_morning_prayer','last_day_noon_burn','saint_purification','saint_return_to_court')) {
            Add-SpecialBossIntents $key $source $cooldown $priority $pveAttack $pveDefense
        }
        elseif (-not $fallbackKeys.Contains($key)) {
            if ($use -match '(?i)(?<!Add)Damage\s*\(') {
                $hits = 1
                if ($use -match '(?i)for\s*\([^;]+;[^<]+<\s*(\d+)') { $hits = [int]$Matches[1] }
                $handler = if ($allTarget) { 'damage.all' } elseif ($hits -gt 1) { 'damage.multi' } else { 'damage.single' }
                $target = if ($allTarget) { New-Target 'Enemy' 'All' 'enemy.all' } else { New-Target 'Enemy' 'Single' 'enemy.lowest_hp' }
                $id = "$prefix.damage"
                Add-Intent (New-Intent $id $source 'Pve' 'native damage shape adapted to spirit attack scaling' 'Attack' $handler $target $cooldown $priority $hits '' 0 2 ($(if ($hits -gt 1) { 0.35 } else { 0.8 })))
                $pveAttack.Add($id)
            }
            if ($use -match '(?i)ChangeDefence\s*\(') {
                $handler = if ($allTarget) { 'block.all' } else { 'block.single' }
                $target = if ($allTarget) { New-Target 'Friendly' 'All' 'friendly.all' } else { New-Target 'Friendly' 'Single' 'friendly.owner_or_self_defense' }
                $id = "$prefix.block"
                Add-Intent (New-Intent $id $source 'Pve' 'native defense redirected to the friendly roster' 'Defense' $handler $target $cooldown $priority 1 '' 0 4 0 0.8)
                $pveDefense.Add($id)
            }
            if ($use -match '(?i)ChangeHp\s*\(') {
                $id = "$prefix.heal"
                Add-Intent (New-Intent $id $source 'Pve' 'native self-heal redirected to the most wounded ally' 'Recovery' 'heal.single' (New-Target 'Friendly' 'Single' 'friendly.most_wounded') $cooldown $priority 1 '' 0 4 0 0 0.6)
                $pveDefense.Add($id)
            }
            if ($use -match '(?i)AddBuff\s*\(') {
                $buffMatches = @([regex]::Matches($use, '(?i)AddBuff\s*\(\s*(?:DataId\.)?"?([A-Za-z0-9_:]+)"?'))
                $buffEntries = [ordered]@{}
                foreach ($buffMatch in $buffMatches) {
                    $buffId = [string]$buffMatch.Groups[1].Value
                    if ([string]::IsNullOrWhiteSpace($buffId)) { continue }
                    $tailLength = [Math]::Min(160, $use.Length - $buffMatch.Index)
                    $tail = $use.Substring($buffMatch.Index, $tailLength)
                    $stacks = 1
                    if ($tail -match '(?i)AddBuff\s*\([^,]+,\s*"?(\d+)') { $stacks = [int]$Matches[1] }
                    if (-not $buffEntries.Contains($buffId) -or [int]$buffEntries[$buffId] -lt $stacks) {
                        $buffEntries[$buffId] = $stacks
                    }
                }
                $buffNumber = 0
                foreach ($buffEntry in $buffEntries.GetEnumerator()) {
                    $buffNumber++
                    $hostile = $targetScript -match '(?i)"Target"|"All"'
                    $target = if ($hostile -and $allTarget) { New-Target 'Enemy' 'All' 'enemy.all' } elseif ($hostile) { New-Target 'Enemy' 'Single' 'enemy.lowest_hp' } else { New-Target 'Friendly' 'Single' 'friendly.owner_or_self_defense' }
                    $id = if ($buffEntries.Count -eq 1) { "$prefix.buff" } else { "$prefix.buff$buffNumber" }
                    $type = if ($hostile) { 'Interference' } else { 'Support' }
                    Add-Intent (New-Intent $id $source 'Pve' 'native buff/debuff preserved with a safe target adapter' $type 'buff.apply' $target $cooldown $priority 1 ([string]$buffEntry.Key) ([int]$buffEntry.Value))
                    if ($hostile) { $pveAttack.Add($id) } else { $pveDefense.Add($id) }
                }
            }
        }

        if ($pvpKeys.Contains($key)) {
            $pvpSources.Add($source)
            $id = "spirit.pvp.$safe.reserved"
            Add-Intent (New-Intent $id $source 'PvpReserved' 'reserved original card/deck/currency interference; inactive in PvE' 'Interference' 'pvp.reserved' (New-Target 'OpponentPlayer' 'Single' 'pvp.opponent') $cooldown $priority)
            $pvpAttack.Add($id)
        }

        if ($fallbackKeys.Contains($key) -or ($pveAttack.Count + $pveDefense.Count + $pvpAttack.Count -eq 0)) {
            $fallbackSources.Add($source)
        }
    }

    $rarity = 1; [void][int]::TryParse([string]$Row.Rarity, [ref]$rarity)
    $scale = switch ($rarity) { 3 { 1.15 } 2 { 1.0 } default { 0.9 } }
    $attackWeight = if ($pveAttack.Count -gt $pveDefense.Count) { 65 } elseif ($pveDefense.Count -gt $pveAttack.Count) { 45 } else { 55 }
    [ordered]@{
        profileId = (Get-SpiritProfileId $enemyId); enemyId = $enemyId; variantId = '*'; sourceEnemyCardIds = $cards
        pveAttackTendency = @($pveAttack | Select-Object -Unique); pveDefenseTendency = @($pveDefense | Select-Object -Unique)
        pvpAttackTendency = @($pvpAttack | Select-Object -Unique); pvpDefenseTendency = @($pvpDefense | Select-Object -Unique)
        fallbackAttackTendency = @('staff_tap'); fallbackDefenseTendency = @('shield_blessing')
        pvpSourceEnemyCardIds = @($pvpSources | Select-Object -Unique)
        fallbackSourceEnemyCardIds = @($fallbackSources | Select-Object -Unique)
        attackWeight = $attackWeight; defenseWeight = 100 - $attackWeight
        hpMultiplier = $scale; magicMultiplier = $scale; attackMultiplier = $scale; armorMultiplier = $scale
    }
}

function Get-EffectRank($Intent) {
    switch -Regex ([string]$Intent.handlerId) {
        '^damage\.' { return 10 }
        '^block\.' { return 20 }
        '^heal\.' { return 20 }
        '^buff\.' { return 30 }
        default { return 40 }
    }
}

function ConvertTo-CompositePveIntents {
    $legacyToComposite = @{}
    $composites = [System.Collections.Generic.List[object]]::new()
    $pve = @($script:intentMap.Values | Where-Object { $_.pool -eq 'Pve' })
    foreach ($group in @($pve | Group-Object { [string]$_.enemyCardId })) {
        $source = [string]$group.Name
        $safe = (($source -replace '[^A-Za-z0-9]+', '_').Trim('_').ToLowerInvariant())
        $id = "spirit.pve.$safe.intent"
        $ordered = @($group.Group | Sort-Object @{ Expression = { Get-EffectRank $_ } }, @{ Expression = { [string]$_.id } })
        $effects = [System.Collections.Generic.List[object]]::new()
        for ($index = 0; $index -lt $ordered.Count; $index++) {
            $entry = $ordered[$index]
            $effects.Add([ordered]@{
                handlerId = [string]$entry.handlerId
                target = $entry.target
                hitCount = [int]$entry.hitCount
                buffId = [string]$entry.buffId
                buffStacks = [int]$entry.buffStacks
                flatValue = [int]$entry.flatValue
                attackScale = [double]$entry.attackScale
                armorScale = [double]$entry.armorScale
                magicScale = [double]$entry.magicScale
                displayIndex = $index + 1
            })
            $legacyToComposite[[string]$entry.id] = $id
        }

        $primary = $ordered[0]
        $composites.Add([ordered]@{
            id = $id
            enemyCardId = $source
            pool = 'Pve'
            adaptationNote = (@($ordered.adaptationNote | Select-Object -Unique) -join '; ')
            type = [string]$primary.type
            cost = [int]$primary.cost
            cooldown = [int]$primary.cooldown
            basePriority = [int]$primary.basePriority
            handlerId = [string]$primary.handlerId
            target = $primary.target
            hitCount = [int]$primary.hitCount
            buffId = [string]$primary.buffId
            buffStacks = [int]$primary.buffStacks
            flatValue = [int]$primary.flatValue
            attackScale = [double]$primary.attackScale
            armorScale = [double]$primary.armorScale
            magicScale = [double]$primary.magicScale
            priorityBonus = [string]$primary.priorityBonus
            threat = $primary.threat
            effects = @($effects)
        })
    }

    [pscustomobject]@{ Intents = @($composites); IdMap = $legacyToComposite }
}

function Remap-Tendency($Values, $IdMap) {
    @($Values | ForEach-Object {
        $id = [string]$_
        if ($IdMap.ContainsKey($id)) { $IdMap[$id] } else { $id }
    } | Select-Object -Unique)
}

$profiles = @($enemyRows | ForEach-Object { Get-IntentProfile $_ } | Sort-Object { $_.enemyId })
$profiles += [ordered]@{
    profileId = ''; enemyId = '*'; variantId = '*'; sourceEnemyCardIds = @()
    pveAttackTendency = @(); pveDefenseTendency = @(); pvpAttackTendency = @(); pvpDefenseTendency = @()
    fallbackAttackTendency = @('staff_tap'); fallbackDefenseTendency = @('shield_blessing')
    pvpSourceEnemyCardIds = @(); fallbackSourceEnemyCardIds = @()
    attackWeight = 60; defenseWeight = 40
    hpMultiplier = 1.0; magicMultiplier = 1.0; attackMultiplier = 1.0; armorMultiplier = 1.0
}
$compositeResult = ConvertTo-CompositePveIntents
$intentProfileListFields = @(
    'sourceEnemyCardIds',
    'pveAttackTendency',
    'pveDefenseTendency',
    'pvpAttackTendency',
    'pvpDefenseTendency',
    'fallbackAttackTendency',
    'fallbackDefenseTendency',
    'pvpSourceEnemyCardIds',
    'fallbackSourceEnemyCardIds'
)
foreach ($profile in $profiles) {
    $profile.pveAttackTendency = @(Remap-Tendency $profile.pveAttackTendency $compositeResult.IdMap)
    $profile.pveDefenseTendency = @(Remap-Tendency $profile.pveDefenseTendency $compositeResult.IdMap)
    foreach ($field in $intentProfileListFields) {
        $profile[$field] = @($profile[$field])
    }
}
$reservedIntents = @($intentMap.Values | Where-Object { $_.pool -ne 'Pve' })
$intents = @(($compositeResult.Intents + $reservedIntents) | Sort-Object { [string]$_.id })
$intentDocument = [ordered]@{ schemaVersion = 3; intents = $intents; profiles = $profiles }

$captureProfiles = @($enemyRows | ForEach-Object {
    $id = ([string]$_.Id).Trim().TrimStart('*'); $rarity = 1; [void][int]::TryParse([string]$_.Rarity, [ref]$rarity)
    [ordered]@{ enemyId = $id; variantId = '*'; resolutionMode = $(if ($rarity -ge 3) { 'AdaptedTerminal' } else { 'GuardedTerminal' }); suppressedSuccessorIds = @(); runNativeDeath = $true; allowRewards = $true }
} | Sort-Object { $_.enemyId })
$captureProfiles += [ordered]@{ enemyId = '*'; variantId = '*'; resolutionMode = 'GuardedTerminal'; suppressedSuccessorIds = @(); runNativeDeath = $true; allowRewards = $true }
$captureDocument = [ordered]@{ schemaVersion = 1; profiles = $captureProfiles }

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Write-JsonDocument $intentDocument (Join-Path $OutputDirectory 'spirit.intent.registry.json') 'lf'
Write-JsonDocument $captureDocument (Join-Path $OutputDirectory 'spirit.capture.registry.json') 'crlf'
Write-Host "Generated $($profiles.Count - 1) spirit profiles, $($intents.Count) adapted intents, and $($captureProfiles.Count - 1) capture profiles."
