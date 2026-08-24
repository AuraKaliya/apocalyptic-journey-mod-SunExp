param(
    [string]$UnityPath = "",
    [string]$UnityProjectPath = "",
    [switch]$RequireBundle
)

$ErrorActionPreference = "Stop"
$requiredUnityVersion = "6000.0.46f1"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$bundlePath = Join-Path $repoRoot "AuraToolsExp\ModResource\VisualBundles\auratools_visuals"
$sourceRoot = Join-Path $repoRoot "AuraToolsExp-Dev\VisualAssets"
$auraCgShaderSource = Join-Path $repoRoot "AuraCgShared\VisualAssets\Shaders"
if ([string]::IsNullOrWhiteSpace($UnityProjectPath)) {
    $UnityProjectPath = Join-Path $sourceRoot "UnityProject"
}

function Find-UnityEditor {
    if (-not [string]::IsNullOrWhiteSpace($UnityPath)) {
        if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { return "" }
        $version = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion
        return $(if ($version -like "$requiredUnityVersion*") { $UnityPath } else { "" })
    }
    foreach ($root in @("C:\Program Files\Unity\Hub\Editor", "D:\UnityFile", "D:\Unity")) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Recurse -Filter Unity.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.VersionInfo.ProductVersion -like "$requiredUnityVersion*" } |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }
    return ""
}

function Write-Utf8NoBom([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}

$assets = Join-Path $UnityProjectPath "Assets"
$shaderDirectory = Join-Path $assets "AuraToolsExp\Visuals\Shaders"
$editorDirectory = Join-Path $assets "Editor"
$packages = Join-Path $UnityProjectPath "Packages"
$settings = Join-Path $UnityProjectPath "ProjectSettings"
New-Item -ItemType Directory -Force -Path $shaderDirectory,$editorDirectory,$packages,$settings | Out-Null
Copy-Item -LiteralPath (Join-Path $sourceRoot "Shaders\CardFaceEffect.shader") -Destination (Join-Path $shaderDirectory "CardFaceEffect.shader") -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot "Shaders\CardFrameEffectUrp.shader") -Destination (Join-Path $shaderDirectory "CardFrameEffectUrp.shader") -Force
Copy-Item -LiteralPath (Join-Path $auraCgShaderSource "AuraCgLumaKeyUI.shader") -Destination (Join-Path $shaderDirectory "AuraCgLumaKeyUI.shader") -Force
Copy-Item -LiteralPath (Join-Path $auraCgShaderSource "AuraCgMaskedInvertFlash.shader") -Destination (Join-Path $shaderDirectory "AuraCgMaskedInvertFlash.shader") -Force
Copy-Item -LiteralPath (Join-Path $auraCgShaderSource "AuraCgScreenBwFlash.shader") -Destination (Join-Path $shaderDirectory "AuraCgScreenBwFlash.shader") -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot "Editor\AuraToolsVisualBundleBuilder.cs.txt") -Destination (Join-Path $editorDirectory "AuraToolsVisualBundleBuilder.cs") -Force
Write-Utf8NoBom (Join-Path $packages "manifest.json") '{"dependencies":{"com.unity.render-pipelines.universal":"17.0.4","com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
Write-Utf8NoBom (Join-Path $settings "ProjectVersion.txt") "m_EditorVersion: 6000.0.46f1`nm_EditorVersionWithRevision: 6000.0.46f1 (fb93bc360d3a)`n"

$unity = Find-UnityEditor
if ([string]::IsNullOrWhiteSpace($unity) -or -not (Test-Path -LiteralPath $unity)) {
    throw "Unity Editor $requiredUnityVersion is required; refusing to retain or rebuild the incompatible legacy visual bundle."
}

$envBefore = $env:AURATOOLS_VISUAL_BUNDLE_OUTPUT
try {
    $env:AURATOOLS_VISUAL_BUNDLE_OUTPUT = $bundlePath
    $log = Join-Path $sourceRoot "auratools_visuals.unity-build.log"
    $process = Start-Process -FilePath $unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        "-batchmode", "-force-d3d11", "-quit", "-projectPath", $UnityProjectPath,
        "-executeMethod", "AuraToolsVisualBundleBuilder.BuildVisualBundle", "-logFile", $log)
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $bundlePath)) {
        throw "AuraTools visual bundle build failed. See $log"
    }
    $logText = Get-Content -Raw -Encoding UTF8 -LiteralPath $log
    foreach ($shader in @("AuraTools/CardFrameEffectUI", "AuraTools/CardFrameEffectURP")) {
        $escaped = [Regex]::Escape($shader)
        if ($logText -notmatch "Serialized binary data for shader $escaped in [^\r\n]*\r?\n\s*d3d11 \(total internal programs: [1-9][0-9]*, unique: [1-9][0-9]*\)") {
            throw "AuraTools visual bundle stripped every D3D11 program for $shader. See $log"
        }
    }
    if ($logText -notmatch 'CardFrameEffectUI render smoke passed: rgba=.*graphics=Direct3D11') {
        throw "AuraTools CardFrameEffectUI did not pass the real Direct3D11 pixel smoke test. See $log"
    }
    if ($logText -notmatch 'CardFrameEffectURP render smoke passed: rgba=.*graphics=Direct3D11') {
        throw "AuraTools CardFrameEffectURP did not pass the real MeshRenderer Direct3D11 pixel smoke test. See $log"
    }
    if ($logText -notmatch 'CardFrameEffectURP material lease smoke passed: restored=.*rebound=.*graphics=Direct3D11') {
        throw "AuraTools CardFrameEffectURP did not pass the pooled material detach/rebind smoke test. See $log"
    }
}
finally {
    $env:AURATOOLS_VISUAL_BUNDLE_OUTPUT = $envBefore
}

Write-Host "AuraTools visual bundle built: $bundlePath"
