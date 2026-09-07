[CmdletBinding()]
param(
    [string]$UnityPath = 'D:\UnityFile\2022.3.62f3c1\Editor\Unity.exe',
    [string]$OutputDirectory = '',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot 'output\unity\event-cg-poster' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$projectPath = Join-Path $OutputDirectory 'project'
$scriptsPath = Join-Path $projectPath 'Assets\Scripts'
$editorPath = Join-Path $projectPath 'Assets\Editor'
$pluginPath = Join-Path $projectPath 'Assets\Plugins'
$playerPath = Join-Path $OutputDirectory 'player\EventCgPreview.exe'
$captures = Join-Path $OutputDirectory 'captures'
foreach ($path in @($scriptsPath,$editorPath,$pluginPath,(Join-Path $projectPath 'Packages'),(Join-Path $projectPath 'ProjectSettings'),$captures)) {
    [System.IO.Directory]::CreateDirectory($path) | Out-Null
}

function Quote-NativeArgument([string]$Argument) { return '"' + $Argument.Replace('"','\"') + '"' }
function Invoke-PreviewProcess([string]$Executable,[string[]]$Arguments,[int]$TimeoutMilliseconds) {
    $argumentText = ($Arguments | ForEach-Object { Quote-NativeArgument $_ }) -join ' '
    $process = Start-Process -FilePath $Executable -ArgumentList $argumentText -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutMilliseconds)) {
        $process.Kill()
        throw "CG preview process timed out: $Executable"
    }
    if ($process.ExitCode -ne 0) { throw "CG preview process failed with exit code $($process.ExitCode): $Executable" }
}

$sources = @(
    'AuraCgShared\AuraCgSceneContracts.cs',
    'AuraCgShared\AuraCgSceneFraming.cs',
    'AuraCgShared\AuraCgSceneArtContracts.cs',
    'AuraCgShared\AuraCgScenePresentation.cs',
    'AuraCgShared\AuraCgSceneCompositionRenderer.cs',
    'AuraToolsExp-Dev\Features\Cg\AuraToolsEventCgArtCatalog.cs'
)
$sourceHashes = @()
foreach ($relative in $sources) {
    $source = Join-Path $repoRoot $relative
    $text = [System.IO.File]::ReadAllText($source)
    # Unity 2022 uses C# 9; wrapping a file-scoped namespace preserves the production code semantics.
    if ($text -match '(?m)^namespace ([\w.]+);') {
        $text = [regex]::Replace($text,'(?m)^namespace ([\w.]+);','namespace $1 {') + "`n}`n"
    }
    [System.IO.File]::WriteAllText((Join-Path $scriptsPath ([System.IO.Path]::GetFileName($source))),("#nullable enable`n" + $text),[System.Text.UTF8Encoding]::new($false))
    $sourceHashes += [pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EventCgUnityPreview\PreviewBootstrap.cs') -Destination $scriptsPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EventCgUnityPreview\PreviewUiAdapter.cs') -Destination $scriptsPath -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EventCgUnityPreview\PreviewEditor.cs') -Destination $editorPath -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'Managed\Newtonsoft.Json.dll') -Destination $pluginPath -Force
[System.IO.File]::WriteAllText((Join-Path $projectPath 'Packages\manifest.json'),'{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.imageconversion":"1.0.0","com.unity.modules.screencapture":"1.0.0","com.unity.modules.ui":"1.0.0","com.unity.modules.imgui":"1.0.0"}}')
[System.IO.File]::WriteAllText((Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt'),"m_EditorVersion: 2022.3.62f3c1`nm_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)`n")
$sourceHashes | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'production-source-hashes.json') -Encoding UTF8
if (-not $SkipBuild) {
    if (-not (Test-Path -LiteralPath $UnityPath)) { throw "Unity editor missing: $UnityPath" }
    Write-Host 'Building isolated Unity acceptance player from production CG sources.'
    Invoke-PreviewProcess $UnityPath @('-batchmode','-quit','-projectPath',$projectPath,'-executeMethod','EventCgPreview.PreviewEditor.Build',('-cgBuildOutput=' + $playerPath),'-logFile',(Join-Path $OutputDirectory 'editor.log')) 300000
}
Write-Host 'Capturing production poster layouts and checking reuse, input, and reduced motion.'
Invoke-PreviewProcess $playerPath @('-batchmode','-force-d3d11','-screen-fullscreen','0',('-cgArtRoot=' + (Join-Path $repoRoot 'AuraToolsExp\SharedResources\EventCg')),('-cgOutput=' + $captures),'-logFile',(Join-Path $OutputDirectory 'player.log')) 180000
$report = Get-Content -Raw -LiteralPath (Join-Path $captures 'report.json') | ConvertFrom-Json
if (-not $report.success -or @($report.cases).Count -ne 20 -or -not $report.destroyedHostDisposePass) { throw 'CG Unity acceptance failed.' }
Write-Host "CG Unity acceptance passed: $(@($report.cases).Count) captures; $captures"
