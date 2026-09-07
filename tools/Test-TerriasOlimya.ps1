param([string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$modRoot = Join-Path $repoRoot 'Terrias'
function Assert-Olimya([bool]$condition, [string]$message) { if (-not $condition) { throw $message } }
function Rows([string]$relative) { @(Import-Csv -LiteralPath (Join-Path $modRoot $relative) | Select-Object -Skip 1) }

$career = @(Rows 'Data/Career/olimya.csv')
$role = @(Rows 'Data/RoleData/olimya.csv')
$skill = @(Rows 'Data/Card/olimya.csv')
Assert-Olimya ($career.Count -eq 1 -and $career[0].Id -eq 'olimya' -and [int]$career[0].SanMax -eq 80) 'Olimya must have one career with 80 base HP.'
Assert-Olimya ($career[0].Skill1 -eq 'Terrias_olimya_olimya_golden_touch' -and [string]::IsNullOrWhiteSpace($career[0].Skill2)) 'Olimya must have exactly one active skill.'
Assert-Olimya ($role.Count -eq 1 -and $role[0].Id -eq 'olimya') 'Olimya RoleData is missing.'
Assert-Olimya ($skill.Count -eq 1 -and $skill[0].Id -eq '*olimya_golden_touch' -and [int]$skill[0].Expend -eq 0) 'Golden Touch must be a zero-cost internal career skill.'

foreach ($table in @('Career', 'RoleData', 'Card')) {
    $text = @(Rows "Text/$table/olimya.csv")
    Assert-Olimya ($text.Count -eq 1) "Olimya $table text is missing."
    foreach ($column in @('Name', 'Name_zh-Hant', 'Name_en', 'Name_ja')) {
        Assert-Olimya (-not [string]::IsNullOrWhiteSpace($text[0].$column)) "Olimya $table $column is missing."
    }
}
$careerText = @(Rows 'Text/Career/olimya.csv')[0]
foreach ($suffix in @('', '_zh-Hant', '_en', '_ja')) {
    foreach ($field in @('Description', 'Action1', 'Passive1', 'Passive2')) {
        $column = $field + $suffix
        Assert-Olimya (-not [string]::IsNullOrWhiteSpace($careerText.$column)) "Missing Olimya $column."
    }
}
$buff = @(Rows 'Data/Buff/terrias.csv' | Where-Object Id -eq '*olimya_goldenized')
Assert-Olimya ($buff.Count -eq 1 -and $buff[0].Type -eq '负面' -and [int]$buff[0].UpperBound -eq 1) 'Goldenization must be an internal, single-layer debuff.'
Assert-Olimya ([int]$buff[0].ReducePerTurn -eq 0) 'Goldenization expires on its caster turn, not its target turn.'

$animationRoot = Join-Path $modRoot 'ModResource/AnimationLib/Olimya'
$idle = @(Get-ChildItem -LiteralPath (Join-Path $animationRoot 'Idle') -File -Filter '*.png')
Assert-Olimya ($idle.Count -eq 24) 'Olimya must retain all 24 supplied Idle frames.'
Add-Type -AssemblyName System.Drawing
for ($frame = 1; $frame -le 24; $frame++) {
    $file = Join-Path $animationRoot ('Idle/frame{0:d4}.png' -f $frame)
    Assert-Olimya (Test-Path -LiteralPath $file) "Missing idle frame $frame."
    $image = [System.Drawing.Image]::FromFile($file)
    try { Assert-Olimya ($image.Width -eq 256 -and $image.Height -eq 256) "Idle frame $frame has inconsistent dimensions." }
    finally { $image.Dispose() }
}
$firstHash = (Get-FileHash -LiteralPath (Join-Path $animationRoot 'Idle/frame0001.png')).Hash
foreach ($action in @('Idle','Attack','Skill','Hit','Defend','Buff','Debuff','Special','Special1','Special2')) {
    $config = Get-Content -LiteralPath (Join-Path $animationRoot "$action/config.json") -Raw | ConvertFrom-Json
    Assert-Olimya ($config.Direction -eq 'Right' -and $config.AnimationPerFrame -eq 0.1) "Invalid animation configuration: $action."
    Assert-Olimya ($config.isLoop -eq ($action -eq 'Idle')) "Only Idle should loop: $action."
    if ($action -ne 'Idle') {
        Assert-Olimya ((Get-FileHash -LiteralPath (Join-Path $animationRoot "$action/frame0001.png")).Hash -eq $firstHash) "Fallback $action must preserve the supplied first Idle frame."
    }
}

$roles = Get-Content -LiteralPath (Join-Path $modRoot 'SharedResources/role.registry.json') -Raw | ConvertFrom-Json
Assert-Olimya (@($roles.entries | Where-Object roleId -eq 'Terrias_olimya_olimya').Count -eq 1) 'Olimya must be registered once as a playable role.'
$archive = Get-Content -LiteralPath (Join-Path $modRoot 'witch.archive.registry.json') -Raw | ConvertFrom-Json
$entry = @($archive.entries | Where-Object id -eq 'olimya')
Assert-Olimya ($entry.Count -eq 1 -and $entry[0].roleId -eq 'Terrias_olimya_olimya') 'Olimya archive entry is missing.'
$story = Join-Path $modRoot $entry[0].backgroundFiles.'zh-Hans'
Assert-Olimya ((Get-Item -LiteralPath $story).Length -gt 0) 'Olimya background text is missing.'

$resources = Get-Content -LiteralPath (Join-Path $modRoot 'SharedResources/aura.registration.json') -Raw | ConvertFrom-Json
$cg = Get-Content -LiteralPath (Join-Path $modRoot 'SharedResources/cg.registry.json') -Raw | ConvertFrom-Json
$feast = @($resources.resources | Where-Object resourceId -eq 'olimya.feast')
$match = @($cg.entries | Where-Object cgId -eq 'olimya.feast')
Assert-Olimya ($feast.Count -eq 1 -and $feast[0].scopeId -eq 'Terrias_olimya_olimya' -and $feast[0].source -eq 'CG/Olimya/Olimya_美餐.png') 'Olimya feast media ownership or source is wrong.'
Assert-Olimya ($match.Count -eq 1 -and $match[0].subjectIds -contains 'Terrias_olimya_olimya' -and $match[0].signals -contains 'aura.role.feast.completed') 'Olimya feast CG must match her feast-completed signal.'

dotnet run --project (Join-Path $repoRoot 'Terrias-Dev.OlimyaTests/Terrias-Dev.OlimyaTests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Olimya production behavior tests failed.' }
Write-Host 'Olimya content passed: career, skill, four locales, mark, 24 Idle frames, fallbacks, archive and feast CG.'
