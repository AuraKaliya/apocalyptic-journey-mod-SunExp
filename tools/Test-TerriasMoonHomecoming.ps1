param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modRoot = Join-Path $repoRoot 'Terrias'
$packId = 'Terrias_terrias_cardpack_moon_homecoming'

function Assert-Content([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$cards = @(Import-Csv -LiteralPath (Join-Path $modRoot 'Data/Card/terrias.csv') |
    Where-Object PackBelong -eq $packId)
$texts = @(Import-Csv -LiteralPath (Join-Path $modRoot 'Text/Card/terrias.csv'))
$specifications = @(
    @('frostmoon_new_god', 0, 3, 'Burnout'),
    @('flower_sea_moon_night', 2, 1, ''),
    @('moon_offering', 1, 2, ''),
    @('kuutar_morning_mist', 0, 2, ''),
    @('moon_homecoming_night', 1, 3, 'Burnout'),
    @('moon_chronicle_i', 0, 3, 'Retain,Unusable'),
    @('moon_chronicle_ii', 0, 3, 'Retain,Unusable'),
    @('moon_chronicle_iii', 0, 3, 'Retain,Unusable'),
    @('new_moon_blessing', 1, 2, 'Burnout'),
    @('luonnotar', 2, 2, '')
)

Assert-Content ($cards.Count -eq 10) 'The Homecoming Moon must expose all ten cards in its reward pool.'
Add-Type -AssemblyName System.Drawing
foreach ($spec in $specifications) {
    $id = [string]$spec[0]
    $rows = @($cards | Where-Object Id -eq $id)
    Assert-Content ($rows.Count -eq 1) "Expected one reward-pool card: $id"
    $card = $rows[0]
    Assert-Content ([int]$card.Expend -eq $spec[1] -and [int]$card.Rarity -eq $spec[2]) "Cost or rarity mismatch: $id"
    $tags = (($card.Tag -split ',' | Where-Object { $_ } | Sort-Object) -join ',')
    Assert-Content ($tags -eq $spec[3]) "Card tags mismatch: $id"
    $isChronicle = $id -like 'moon_chronicle_*'
    Assert-Content ($isChronicle -eq (-not [string]::IsNullOrWhiteSpace($card.DrawScript))) "Chronicle draw effect missing or attached to an unrelated card: $id"
    Assert-Content ($isChronicle -eq [string]::IsNullOrWhiteSpace($card.UseScript)) "Chronicles must have no playable effect: $id"
    $localized = @($texts | Where-Object Id -eq $id)
    Assert-Content ($localized.Count -eq 1) "Card text missing: $id"
    foreach ($column in @('Name', 'Name_zh-Hant', 'Name_en', 'Name_ja', 'Description', 'Description_zh-Hant', 'Description_en', 'Description_ja')) {
        Assert-Content (-not [string]::IsNullOrWhiteSpace($localized[0].$column)) "Missing $column for $id"
    }
    $icon = Join-Path $modRoot ($card.Icon.Substring('Mods/Terrias/'.Length) + '.png')
    Assert-Content (Test-Path -LiteralPath $icon) "Missing card art: $id"
    $bitmap = [System.Drawing.Image]::FromFile($icon)
    try { Assert-Content ($bitmap.Width -eq 256 -and $bitmap.Height -eq 256) "Card art must be 256x256: $id" }
    finally { $bitmap.Dispose() }
}

$buff = @(Import-Csv -LiteralPath (Join-Path $modRoot 'Data/Buff/terrias.csv') | Where-Object Id -eq 'frostmoon_marrow')
Assert-Content ($buff.Count -eq 1 -and [int]$buff[0].UpperBound -eq 1) 'Frostmoon Marrow must be non-stacking.'
Assert-Content ([int]$buff[0].ReducePerTurn -eq 0 -and [int]$buff[0].ReducePerUse -eq 0 -and [int]$buff[0].ReducePerAttacked -eq 0) 'Frostmoon Marrow must persist through the combat.'
$pack = @(Import-Csv -LiteralPath (Join-Path $modRoot 'Data/CardPack/terrias.csv') | Where-Object Id -eq 'cardpack_moon_homecoming')
Assert-Content ($pack.Count -eq 1 -and $pack[0].Type -eq 'Normal') 'The Homecoming Moon must be a normal selectable pack.'
Assert-Content ($pack[0].Icon -eq 'Mods/Terrias/ModResource/Images/CardPack/cardpack_moon_homecoming') 'The pack cover must use its canonical name.'
Assert-Content (Test-Path -LiteralPath (Join-Path $modRoot 'ModResource/Images/CardPack/cardpack_moon_homecoming.png')) 'The renamed pack cover is missing.'

Write-Host 'Moon Homecoming content passed: ten reward-pool cards, exact costs/rarities/tags, four locales, 256x256 artwork, non-stacking Marrow, and selectable pack.'

dotnet run --project (Join-Path $repoRoot 'Terrias-Dev.MoonHomecomingTests/Terrias-Dev.MoonHomecomingTests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Moon Homecoming production behavior tests failed.' }
