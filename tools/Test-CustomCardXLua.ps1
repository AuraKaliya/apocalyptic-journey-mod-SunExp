param([string]$UnityPath='D:/UnityFile/6000.0.46f1/Editor/Unity.exe',[string]$GameDataDirectory="D:/Steam/steamapps/common/Witch's Apocalyptic Journey/Witch's Apocalyptic Journey_Data")
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=Join-Path $repo 'output/custom-card-xlua'
$project=Join-Path $output 'project'
$scripts=Join-Path $project 'Assets/Editor'
$plugins=Join-Path $project 'Assets/Plugins'
foreach($dir in @($scripts,$plugins,(Join-Path $plugins 'x86_64'),(Join-Path $project 'Packages'),(Join-Path $project 'ProjectSettings'))){[IO.Directory]::CreateDirectory($dir)|Out-Null}
$managed=Join-Path $repo 'Managed'
foreach($name in @('Witch.dll','Witch.Core.dll','AllScripts.dll')){if((Get-FileHash -LiteralPath (Join-Path $managed $name)).Hash -ne (Get-FileHash -LiteralPath (Join-Path $GameDataDirectory "Managed/$name")).Hash){throw "Game reference mismatch: $name"}}
$files=@('CustomCardDocument.cs','CustomCardCompiler.cs','CustomCardLuaSupport.cs','CustomCardDescription.cs','CardPixelCanvas.cs')
$sources=@($files|ForEach-Object{Join-Path $repo "AuraToolsExp-Dev/Features/CustomCards/$_"})
$sources+=@(Get-ChildItem -LiteralPath (Join-Path $repo 'AuraToolsExp-Dev/Features/CustomCards') -Filter 'CardBlueprint*.cs'|ForEach-Object{$_.FullName})
$sources+=Join-Path $repo 'AuraToolsExp-Dev/Features/PixelEmoji/IndexedPixelCanvas.cs'
$sources+=Join-Path $repo 'AuraToolsExp-Dev/Features/PixelEmoji/PixelEmojiCore.cs'
foreach($source in $sources){
    $text=[IO.File]::ReadAllText($source)
    if([IO.Path]::GetFileName($source) -eq 'PixelEmojiCore.cs'){$text=$text.Substring(0,$text.IndexOf('public enum PixelEmojiPlaybackMode')).Replace('using AuraToolsExp.Dll.Infrastructure;','')}
    if($text -match '(?m)^namespace ([\w.]+);'){$text=[regex]::Replace($text,'(?m)^namespace ([\w.]+);','namespace $1 {')+"`n}`n"}
    [IO.File]::WriteAllText((Join-Path $scripts ([IO.Path]::GetFileName($source))),"#nullable enable`n"+$text,[Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CustomCardXLuaProbe/Probe.cs') -Destination $scripts -Force
Copy-Item -LiteralPath (Join-Path $managed 'Newtonsoft.Json.dll') -Destination $plugins -Force
Copy-Item -LiteralPath (Join-Path $GameDataDirectory 'Plugins/x86_64/xlua.dll') -Destination (Join-Path $plugins 'x86_64/xlua.dll') -Force
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'),'{"dependencies":{}}')
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings/ProjectVersion.txt'),"m_EditorVersion: 6000.0.46f1`n")
$run=[Guid]::NewGuid().ToString('N')
$report=Join-Path $output "report-$run.json"
$log=Join-Path $output "editor-$run.log"
$arguments=@('-batchmode','-quit','-projectPath',$project,'-executeMethod','CustomCardXLuaProbe.Run',"-cardManaged=$managed","-cardProbeOutput=$report",'-logFile',$log)
$process=Start-Process -FilePath $UnityPath -ArgumentList (($arguments|ForEach-Object{'"'+$_+'"'}) -join ' ') -WindowStyle Hidden -PassThru
if(-not $process.WaitForExit(240000)){$process.Kill();throw "XLua probe timed out: $log"}
if($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $report)){throw "XLua probe failed: $log"}
$result=Get-Content -Raw -LiteralPath $report|ConvertFrom-Json
if(-not $result.success -or @($result.cases).Count -lt 13){throw 'XLua cases incomplete.'}
Copy-Item -LiteralPath $report -Destination (Join-Path $output 'latest.json') -Force
Write-Host "Actual game XLua probe passed: $(@($result.cases).Count) cases; $report"
