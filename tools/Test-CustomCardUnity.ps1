[CmdletBinding()]
param([string]$UnityPath='D:\UnityFile\2022.3.62f3c1\Editor\Unity.exe',[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=Join-Path $repo 'output\unity\custom-cards'
$project=Join-Path $output 'project'
$scripts=Join-Path $project 'Assets\Scripts'
$editor=Join-Path $project 'Assets\Editor'
$plugins=Join-Path $project 'Assets\Plugins'
$captures=Join-Path $output 'captures'
$player=Join-Path $output 'player\CustomCardPreview.exe'
foreach($dir in @($scripts,$editor,$plugins,$captures,(Join-Path $project 'Packages'),(Join-Path $project 'ProjectSettings'))){[IO.Directory]::CreateDirectory($dir)|Out-Null}
$files=@('CustomCardDocument.cs','CustomCardCompiler.cs','CustomCardLuaSupport.cs','CustomCardDescription.cs','CustomCardTrial.cs','CardPixelCanvas.cs','CustomCardFlowLayout.cs','CardPixelEditor.cs','CustomCardWorkshop.cs')
$sources=@($files|ForEach-Object{Join-Path $repo ('AuraToolsExp-Dev\Features\CustomCards\'+$_)})
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
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CustomCardUnityPreview\PreviewAdapter.cs'),(Join-Path $PSScriptRoot 'CustomCardUnityPreview\PreviewBootstrap.cs') -Destination $scripts -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CustomCardUnityPreview\PreviewEditor.cs') -Destination $editor -Force
Copy-Item -LiteralPath (Join-Path $repo 'Managed\Newtonsoft.Json.dll') -Destination $plugins -Force
$resources=Join-Path $project 'Assets\Resources'
[IO.Directory]::CreateDirectory($resources)|Out-Null
$font=Join-Path $env:WINDIR 'Fonts\simhei.ttf'
if(-not (Test-Path -LiteralPath $font)){$font=Join-Path $env:WINDIR 'Fonts\msyh.ttc'}
Copy-Item -LiteralPath $font -Destination (Join-Path $resources 'PreviewFont.ttf') -Force
$essentials=Join-Path $project 'Library\PackageCache\com.unity.textmeshpro@3.0.6\Package Resources\TMP Essential Resources.unitypackage'
if(-not (Test-Path -LiteralPath (Join-Path $project 'Assets\TextMesh Pro\Resources\TMP Settings.asset'))){
    if(-not (Test-Path -LiteralPath $essentials)){
        $essentials=Join-Path $output 'textmeshpro-3.0.6.tgz'
        if(-not (Test-Path -LiteralPath $essentials)){Invoke-WebRequest -Uri 'https://download.packages.unity.com/com.unity.textmeshpro/-/com.unity.textmeshpro-3.0.6.tgz' -OutFile $essentials -TimeoutSec 60}
    }
    & python (Join-Path $PSScriptRoot 'CustomCardUnityPreview\ExtractEssentials.py') $essentials $project
    if($LASTEXITCODE -ne 0){throw 'TMP resource extraction failed.'}
}
[IO.File]::WriteAllText((Join-Path $project 'Packages\manifest.json'),'{"scopedRegistries":[{"name":"Unity Official TMP","url":"https://packages.unity.com","scopes":["com.unity.textmeshpro"]}],"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.screencapture":"1.0.0","com.unity.modules.ui":"1.0.0","com.unity.modules.imgui":"1.0.0"}}')
[IO.File]::WriteAllText((Join-Path $project 'ProjectSettings\ProjectVersion.txt'),"m_EditorVersion: 2022.3.62f3c1`n")
$hashes|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'production-source-hashes.json') -Encoding UTF8
function Run([string]$exe,[string[]]$arguments){
    $quoted=($arguments|ForEach-Object{'"'+$_.Replace('"','\"')+'"'}) -join ' '
    $process=Start-Process -FilePath $exe -ArgumentList $quoted -WindowStyle Hidden -PassThru
    if(-not $process.WaitForExit(300000)){$process.Kill();throw 'Custom card Unity process timed out.'}
    if($process.ExitCode -ne 0){throw "Custom card Unity process failed: $($process.ExitCode); logs: $output"}
}
if(-not $SkipBuild){Run $UnityPath @('-batchmode','-quit','-projectPath',$project,'-executeMethod','CustomCardPreviewEditor.Build',('-cardBuild='+$player),'-logFile',(Join-Path $output 'editor.log'))}
Run $player @('-batchmode','-force-d3d11','-screen-fullscreen','0',('-cardOutput='+$captures),'-logFile',(Join-Path $output 'player.log'))
$report=Get-Content -Raw -LiteralPath (Join-Path $captures 'report.json')|ConvertFrom-Json
if(-not $report.success -or @($report.cases).Count -ne 18){throw 'Custom card Unity acceptance failed.'}
Write-Host "Custom card Unity acceptance: $(@($report.cases).Count) captures; $captures"
