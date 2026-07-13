param(
    [string]$BaseEnemyCsv = (Join-Path $PSScriptRoot "..\apocalyptic-journey-mod-tutorial\ModTemplate\Scripts\Lib\DataConfigs\Data\Enemy\enemy.csv"),
    [string]$SunExpEnemyCsv = (Join-Path $PSScriptRoot "..\SunExp\Data\Enemy\sunexp.csv"),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\SunExp")
)

$ErrorActionPreference = "Stop"

function Get-EnemyRows([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Enemy CSV not found: $Path"
    }

    return @(Import-Csv -LiteralPath $Path | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Id) -and $_.Id -ne "Id"
    })
}

function Get-IntentProfile($Row) {
    $id = ([string]$Row.Id).Trim().TrimStart('*')
    $cards = @(([string]$Row.CardList).Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $fingerprint = (($cards -join ' ') + ' ' + $id).ToLowerInvariant()
    $attack = [System.Collections.Generic.List[string]]::new()
    $defense = [System.Collections.Generic.List[string]]::new()
    $attack.Add('staff_tap')
    $defense.Add('shield_blessing')

    if ($fingerprint -match 'fivehit|combo|multi|sword|spear|hammer|burn|fire|purification|noon|refraction') {
        $attack.Add('staff_combo')
    }
    if ($fingerprint -match 'weak|vulner|toxin|poison|interference|thief|charm|calibration|prayer') {
        $attack.Add('magic_interference')
    }
    if ($fingerprint -match 'charge|frenzy|overrun|nerve|fearless|extraordinary|witness') {
        $defense.Add('charge')
    }
    if ($fingerprint -match 'heal|rejuven|recover|regener|return_to_court') {
        $defense.Add('holy_heal')
    }
    if ($fingerprint -match 'observe|witness|bless|enhance|support') {
        $defense.Add('you_are_enhanced')
    }

    $rarity = 1
    [void][int]::TryParse([string]$Row.Rarity, [ref]$rarity)
    $scale = switch ($rarity) { 3 { 1.15 } 2 { 1.0 } default { 0.9 } }
    $attackWeight = if ($attack.Count -gt 1) { 65 } else { 55 }

    [ordered]@{
        enemyId = $id
        variantId = '*'
        sourceEnemyCardIds = $cards
        attackTendency = @($attack)
        defenseTendency = @($defense)
        attackWeight = $attackWeight
        defenseWeight = 100 - $attackWeight
        hpMultiplier = $scale
        magicMultiplier = $scale
        attackMultiplier = $scale
        armorMultiplier = $scale
    }
}

$rows = @((Get-EnemyRows $BaseEnemyCsv) + (Get-EnemyRows $SunExpEnemyCsv))
$profiles = @($rows | ForEach-Object { Get-IntentProfile $_ } | Sort-Object { $_.enemyId })
$profiles += [ordered]@{
    enemyId = '*'
    variantId = '*'
    sourceEnemyCardIds = @()
    attackTendency = @()
    defenseTendency = @()
    attackWeight = 60
    defenseWeight = 40
    hpMultiplier = 1.0
    magicMultiplier = 1.0
    attackMultiplier = 1.0
    armorMultiplier = 1.0
}

$intentDocument = [ordered]@{
    schemaVersion = 1
    intents = @()
    profiles = $profiles
}

$captureProfiles = @($rows | ForEach-Object {
    $id = ([string]$_.Id).Trim().TrimStart('*')
    $rarity = 1
    [void][int]::TryParse([string]$_.Rarity, [ref]$rarity)
    [ordered]@{
        enemyId = $id
        variantId = '*'
        resolutionMode = if ($rarity -ge 3) { 'AdaptedTerminal' } else { 'GuardedTerminal' }
        suppressedSuccessorIds = @()
        runNativeDeath = $true
        allowRewards = $true
    }
} | Sort-Object { $_.enemyId })
$captureProfiles += [ordered]@{
    enemyId = '*'
    variantId = '*'
    resolutionMode = 'GuardedTerminal'
    suppressedSuccessorIds = @()
    runNativeDeath = $true
    allowRewards = $true
}

$captureDocument = [ordered]@{
    schemaVersion = 1
    profiles = $captureProfiles
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$intentDocument | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'spirit.intent.registry.json') -Encoding utf8
$captureDocument | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'spirit.capture.registry.json') -Encoding utf8
Write-Host "Generated $($profiles.Count - 1) explicit spirit intent profiles and $($captureProfiles.Count - 1) capture profiles."
