param([string]$PackageDirectory='')
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($PackageDirectory)){$PackageDirectory=Join-Path (Split-Path -Parent $PSScriptRoot) 'AuraToolsExp'}
$artRoot=[IO.Path]::GetFullPath((Join-Path $PackageDirectory 'SharedResources/EventCg')).TrimEnd('\','/')
$catalog=Get-Content -LiteralPath (Join-Path $artRoot 'event-cg.art.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($catalog.schemaVersion -ne 1){throw 'Unsupported CG artwork catalog.'}
$ids=@($catalog.assets.PSObject.Properties.Name)
if($ids.Count -eq 0){throw 'CG artwork catalog has no assets.'}
function Require-Asset([string]$Id){if([string]::IsNullOrWhiteSpace($Id) -or $Id -notin $ids){throw "CG references an unknown asset: $Id"}}
foreach($asset in $catalog.assets.PSObject.Properties){
    $relative=[string]$asset.Value.path
    if([IO.Path]::IsPathRooted($relative)){throw "CG path must be relative: $relative"}
    $path=[IO.Path]::GetFullPath((Join-Path $artRoot $relative))
    if(-not $path.StartsWith($artRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not(Test-Path -LiteralPath $path -PathType Leaf)){throw "CG packaged asset is missing or outside its root: $relative"}
    if((Get-Item -LiteralPath $path).Length -eq 0){throw "CG asset is empty: $relative"}
    foreach($layer in $asset.Value.layers){Require-Asset $layer.asset}
}
foreach($theme in $catalog.themes.PSObject.Properties){
    Require-Asset $theme.Value.background
    foreach($layer in $theme.Value.layers){Require-Asset $layer.asset}
}
foreach($character in $catalog.characters){
    Require-Asset $character.neutral
    foreach($pose in $character.poses.PSObject.Properties){Require-Asset ([string]$pose.Value)}
}
Write-Host "Event CG resource closure passed: assets=$($ids.Count); characters=$(@($catalog.characters).Count); root=$artRoot"
