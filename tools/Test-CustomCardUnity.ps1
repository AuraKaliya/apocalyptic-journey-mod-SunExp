[CmdletBinding()]
param([string]$UnityPath='D:\UnityFile\6000.0.46f1\Editor\Unity.exe',[switch]$SkipBuild,[switch]$ClipProbeOnly,[string]$GameDataDirectory='')
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=Join-Path $repo 'output\unity\custom-cards'
$project=Join-Path $output 'project-unity6'
$scripts=Join-Path $project 'Assets\Scripts'
$editor=Join-Path $project 'Assets\Editor'
$plugins=Join-Path $project 'Assets\Plugins'
$captures=Join-Path $output 'captures'
$player=Join-Path $output 'player-unity6\CustomCardPreview.exe'
foreach($dir in @($scripts,$editor,$plugins,$captures,(Join-Path $project 'Packages'),(Join-Path $project 'ProjectSettings'))){[IO.Directory]::CreateDirectory($dir)|Out-Null}
$files=@('CustomCardDocument.cs','CustomCardCompiler.cs','CustomCardLuaSupport.cs','CustomCardDescription.cs','CardPixelCanvas.cs','CustomCardFlowLayout.cs','CardPixelEditor.cs','CustomCardWorkshop.cs','CustomCardVisuals.cs','CustomCardWorkspaceLayout.cs','CustomCardControls.cs','CustomCardPreview.cs','CustomCardPresentationData.cs','CustomCardUi.cs','CustomCardIcon.cs','CustomCardInputFeedback.cs','CustomCardStatePicker.cs','CustomCardGuide.cs')
foreach($retired in @('CustomCardModalLayer.cs','CustomCardTrial.cs')) {
    $retiredSource=Join-Path $scripts $retired
    if(Test-Path -LiteralPath $retiredSource){Remove-Item -LiteralPath $retiredSource}
}
$sources=@($files|ForEach-Object{Join-Path $repo ('AuraToolsExp-Dev\Features\CustomCards\'+$_)})
$sources+=@(Get-ChildItem -LiteralPath (Join-Path $repo 'AuraToolsExp-Dev\Features\CustomCards') -Filter 'CardBlueprint*.cs' | ForEach-Object {$_.FullName})
$sources+=Join-Path $repo 'AuraToolsExp-Dev\Features\CustomCards\CustomCardGraphEditor.cs'
$sources+=Join-Path $repo 'AuraToolsExp-Dev\Features\Settings\AuraToolsWindowHost.cs'
$sources+=Join-Path $repo 'AuraToolsExp-Dev\Features\PixelEmoji\IndexedPixelCanvas.cs'
$sources+=Join-Path $repo 'AuraToolsExp-Dev\Features\PixelEmoji\PixelEmojiCore.cs'
$hashes=@()
foreach($source in $sources){
    $text=[IO.File]::ReadAllText($source)
    if([IO.Path]::GetFileName($source) -eq 'PixelEmojiCore.cs'){$text=$text.Substring(0,$text.IndexOf('public enum PixelEmojiPlaybackMode'))}
    if($text -match '(?m)^namespace ([\w.]+);'){$text=[regex]::Replace($text,'(?m)^namespace ([\w.]+);','namespace $1 {')+"`n}`n"}
    [IO.File]::WriteAllText((Join-Path $scripts ([IO.Path]::GetFileName($source))),"#nullable enable`n"+$text,[Text.UTF8Encoding]::new($false))
    $hashes+=@{path=$source;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CustomCardUnityPreview\PreviewAdapter.cs'),(Join-Path $PSScriptRoot 'CustomCardUnityPreview\PreviewBootstrap.cs'),(Join-Path $PSScriptRoot 'CustomCardUnityPreview\NativeUiRenderProbe.cs') -Destination $scripts -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CustomCardUnityPreview\PreviewEditor.cs') -Destination $editor -Force
Copy-Item -LiteralPath (Join-Path $repo 'Managed\Newtonsoft.Json.dll') -Destination $plugins -Force
$resources=Join-Path $project 'Assets\Resources'
[IO.Directory]::CreateDirectory($resources)|Out-Null
if(-not [string]::IsNullOrWhiteSpace($GameDataDirectory)){
    & python (Join-Path $PSScriptRoot 'CustomCardUnityPreview/ExtractHostUiFixture.py') --game-data $GameDataDirectory --output (Join-Path $output 'host-fixture')
    if($LASTEXITCODE -ne 0){throw 'Installed UI fixture extraction failed.'}
}
$font=Join-Path $env:WINDIR 'Fonts\simhei.ttf'
if(-not (Test-Path -LiteralPath $font)){$font=Join-Path $env:WINDIR 'Fonts\msyh.ttc'}
if(Test-Path -LiteralPath (Join-Path $output 'host-fixture/HostFont.ttf')){
    $fixture=Get-Content -Raw -LiteralPath (Join-Path $output 'host-fixture/host-ui.json')|ConvertFrom-Json
    if($fixture.witchSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repo 'Managed/Witch.dll')).Hash){throw 'Native font fixture belongs to different game references.'}
    $font=Join-Path $output 'host-fixture/HostFont.ttf'
}
Copy-Item -LiteralPath $font -Destination (Join-Path $resources 'PreviewFont.ttf') -Force
[IO.File]::WriteAllText((Join-Path $project 'Packages\manifest.json'),'{"dependencies":{"com.unity.ugui":"2.0.0","com.unity.render-pipelines.universal":"17.0.4","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.screencapture":"1.0.0","com.unity.modules.ui":"1.0.0","com.unity.modules.imgui":"1.0.0"}}')
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings\ProjectVersion.txt'),"m_EditorVersion: 6000.0.46f1`n")
$hashes|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'production-source-hashes.json') -Encoding UTF8
function Run([string]$exe,[string[]]$arguments){
    $quoted=($arguments|ForEach-Object{'"'+$_.Replace('"','\"')+'"'}) -join ' '
    $process=Start-Process -FilePath $exe -ArgumentList $quoted -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(300000)){$process.Kill();throw 'Custom card Unity process timed out.'}
    if($process.ExitCode -ne 0){throw "Custom card Unity process failed: $($process.ExitCode); logs: $output"}
}
if(-not $SkipBuild){
    $essential=@(Get-ChildItem -LiteralPath (Join-Path $project 'Library/PackageCache') -Directory -Filter 'com.unity.ugui@*' -ErrorAction SilentlyContinue | ForEach-Object { Join-Path $_.FullName 'Package Resources/TMP Essential Resources.unitypackage' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
    if($essential.Count -eq 0){
        Run $UnityPath @('-batchmode','-quit','-projectPath',$project,'-logFile',(Join-Path $output 'resolve.log'))
        $essential=@(Get-ChildItem -LiteralPath (Join-Path $project 'Library/PackageCache') -Directory -Filter 'com.unity.ugui@*' | ForEach-Object { Join-Path $_.FullName 'Package Resources/TMP Essential Resources.unitypackage' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1)
    }
    if($essential.Count -ne 1){throw 'Unity 6 UGUI TMP essential resources are missing.'}
    & python (Join-Path $PSScriptRoot 'CustomCardUnityPreview/ExtractEssentials.py') $essential[0] $project
    if($LASTEXITCODE -ne 0){throw 'Unity 6 TMP resource preparation failed.'}
    Run $UnityPath @('-batchmode','-quit','-projectPath',$project,'-executeMethod','CustomCardPreviewEditor.Build',('-cardBuild='+$player),'-logFile',(Join-Path $output 'editor.log'))
}
$playerArguments=@('-batchmode','-force-d3d11','-screen-fullscreen','0',('-cardOutput='+$captures),'-logFile',(Join-Path $output 'player.log'))
if($ClipProbeOnly){$playerArguments+='-cardClipProbe'}
Run $player $playerArguments
if($ClipProbeOnly){Get-Content -Raw -LiteralPath (Join-Path $captures 'clip-probe.json');return}
$report=Get-Content -Raw -LiteralPath (Join-Path $captures 'report.json')|ConvertFrom-Json
if(-not $report.success -or @($report.interactions).Count -ne 4 -or @($report.lifecycle).Count -ne 4){throw 'Custom card Unity acceptance failed.'}
$expectedCases=@('card-1280-0','card-1280-1','card-1280-2','card-960-0','card-960-1','card-960-2','card-760-0','card-760-1','card-760-2','block-1280','block-960','block-760','pixels-1280','pixels-960','pixels-760','graph-group-1280','graph-group-960','graph-group-760','graph-edited-1280','graph-edited-960','graph-edited-760')
$missingCases=@($expectedCases|Where-Object{$_ -notin @($report.cases.name)})
$expectedStates=@(1600,1280,960,760)|ForEach-Object{"annotations-$_";"picker-design-$_";"node-error-$_"}
$expectedStates+=@(0,1,2,6)|ForEach-Object{"card-1600-$_"}
$expectedStates+=@('block','pixels','graph-group','graph-edited')|ForEach-Object{"$_-1600"}
$expectedStates+=@(1600,1280,960,760)|ForEach-Object{"craft-cost-$_"}
$expectedStates+=@(1600,1280,960,760)|ForEach-Object{"preview-$_";"menu-$_"}
$expectedStates+=@(1600,1280,960,760)|ForEach-Object{"guide-index-$_";"state-picker-$_";"state-diagnostics-$_";"state-guide-$_";"state-group-$_"}
if(-not $report.stateWorkflow -or -not $report.guideContextPreserved){throw 'State workflow or guide edit-buffer preservation failed.'}
if(-not $report.draftLifecycle){throw 'Default draft lifecycle and intentional autosave acceptance failed.'}
if(-not $report.designGeometry -or -not $report.scaledClipping){throw 'Design geometry or scaled clipping acceptance did not pass.'}
if(-not $report.fullWindow -or -not $report.nodeZoomBounds){throw 'Full-window layout or scaled-node text bounds did not pass.'}
if(-not $report.inputEditing -or -not $report.numericFormats -or -not $report.trialRemoved){throw 'Numeric input editing or trial removal contract did not pass.'}
$missingCases+=@($expectedStates|Where-Object{$_ -notin @($report.cases.name)})
if($missingCases.Count -gt 0){throw ('Missing custom card UI cases: '+($missingCases -join ', '))}
Write-Host "Custom card Unity acceptance: $(@($report.cases).Count) captures; $captures"
