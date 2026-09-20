param([Parameter(Mandatory)][string]$GameDataDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$gameData=[IO.Path]::GetFullPath($GameDataDirectory).TrimEnd('\','/')
$gameRoot=Split-Path -Parent $gameData
$executable=Join-Path $gameRoot "Witch's Apocalyptic Journey.exe"
if(-not(Test-Path -LiteralPath $executable)){throw 'Game executable not found.'}
if(@(Get-CimInstance Win32_Process | Where-Object {$_.ExecutablePath -eq $executable}).Count -gt 0){throw 'Close the running game before native UI acceptance.'}
foreach($product in @('AuraToolsExp','Terrias')){foreach($file in @('Entry.dll','Aura.Shared.dll')){
    $source=Join-Path $repo "$product/Scripts/$file";$installed=Join-Path $gameData "Mods/$product/Scripts/$file"
    if((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $installed).Hash){throw "Install the matching package before native acceptance: $product/$file"}
}}
$modRoot=Join-Path $gameData 'Mods/AuraWorkshopUiProbe'
if(Test-Path -LiteralPath $modRoot){throw 'Temporary UI probe directory already exists; no files overwritten.'}
$output=Join-Path $repo ('output/custom-card-game/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output)|Out-Null
& dotnet build (Join-Path $PSScriptRoot 'CustomCardGameProbe/CustomCardGameProbe.csproj') -c Release /v:minimal
if($LASTEXITCODE -ne 0){throw 'Native UI probe compilation failed.'}
$process=$null
try {
    [IO.Directory]::CreateDirectory((Join-Path $modRoot 'Scripts'))|Out-Null
    [ordered]@{ModName='AuraWorkshopUiProbe';ModVersion='0.0.1';ModAuthor='Local acceptance';ModDescription='Temporary native card workshop acceptance';IconPath='';Enabled=$true;Dependencies=@('AuraToolsExp.Aura');MustSame=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $modRoot 'ModConfig.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CustomCardGameProbe/bin/Release/net472/AuraWorkshopUiProbe.dll') -Destination (Join-Path $modRoot 'Scripts/Entry.dll')
    $arguments=@('-screen-fullscreen','0','-force-d3d11',"-aura-workshop-probe=$output",'-logFile',(Join-Path $output 'player.log'))
    $process=Start-Process -FilePath $executable -ArgumentList (($arguments|ForEach-Object{'"'+$_+'"'}) -join ' ') -WorkingDirectory $gameRoot -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(150000)){$process.Kill();throw "Native game UI probe timed out: $output"}
    $reportPath=Join-Path $output 'report.json'
    if(-not(Test-Path -LiteralPath $reportPath)){throw "Game did not produce a UI acceptance report: $output"}
    $report=Get-Content -Raw -LiteralPath $reportPath|ConvertFrom-Json
    if(-not $report.success -or @($report.cases).Count -ne 3){throw "Native UI acceptance failed: $reportPath"}
    if(-not $report.legacyDescription -or -not $report.scaledNodeText -or @($report.detailWorkspaces).Count -ne 3){throw "Native full-window, node zoom or saved-card presentation acceptance failed: $reportPath"}
    if(-not $report.inputEditing -or -not $report.percentInputs -or -not $report.trialRemoved){throw "Native numeric editor acceptance failed: $reportPath"}
    if(-not $report.stateEditing -or -not $report.guideNavigation -or $report.loadedStates -le 0){throw "Native state picker and guide acceptance failed: $reportPath"}
    if(-not $report.pristineDraftLifecycle){throw "Opening the workshop must not enroll the default blueprint: $reportPath"}
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $repo 'output/custom-card-game/latest.json') -Force
    Write-Host "Native game UI acceptance passed: $reportPath"
} finally {
    if($process -and -not $process.HasExited){$process.Kill();$process.WaitForExit()}
    $resolved=[IO.Path]::GetFullPath($modRoot)
    $expected=[IO.Path]::GetFullPath((Join-Path $gameData 'Mods/AuraWorkshopUiProbe'))
    if($resolved -ne $expected -or -not $resolved.StartsWith([IO.Path]::GetFullPath((Join-Path $gameData 'Mods'))+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe probe cleanup path.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
