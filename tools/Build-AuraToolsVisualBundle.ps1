param(
    [string]$UnityPath = "",
    [string]$UnityProjectPath = "",
    [switch]$RequireBundle
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$bundlePath = Join-Path $repoRoot "AuraToolsExp\ModResource\VisualBundles\auratools_visuals"
$sourceRoot = Join-Path $repoRoot "AuraToolsExp-Dev\VisualAssets"
$auraCgShaderSource = Join-Path $repoRoot "AuraCgShared\VisualAssets\Shaders"
if ([string]::IsNullOrWhiteSpace($UnityProjectPath)) {
    $UnityProjectPath = Join-Path $sourceRoot "UnityProject"
}

function Find-UnityEditor {
    if (-not [string]::IsNullOrWhiteSpace($UnityPath)) { return $UnityPath }
    foreach ($root in @("C:\Program Files\Unity\Hub\Editor", "D:\UnityFile", "D:\Unity")) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $candidate = Get-ChildItem -LiteralPath $root -Recurse -Filter Unity.exe -ErrorAction SilentlyContinue |
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
Copy-Item -LiteralPath (Join-Path $auraCgShaderSource "AuraCgLumaKeyUI.shader") -Destination (Join-Path $shaderDirectory "AuraCgLumaKeyUI.shader") -Force
Copy-Item -LiteralPath (Join-Path $auraCgShaderSource "AuraCgMaskedInvertFlash.shader") -Destination (Join-Path $shaderDirectory "AuraCgMaskedInvertFlash.shader") -Force
Copy-Item -LiteralPath (Join-Path $auraCgShaderSource "AuraCgScreenBwFlash.shader") -Destination (Join-Path $shaderDirectory "AuraCgScreenBwFlash.shader") -Force
Copy-Item -LiteralPath (Join-Path $sourceRoot "Editor\AuraToolsVisualBundleBuilder.cs.txt") -Destination (Join-Path $editorDirectory "AuraToolsVisualBundleBuilder.cs") -Force
if (-not (Test-Path -LiteralPath (Join-Path $packages "manifest.json"))) {
    Write-Utf8NoBom (Join-Path $packages "manifest.json") '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.uielements":"1.0.0"}}'
}
if (-not (Test-Path -LiteralPath (Join-Path $settings "ProjectVersion.txt"))) {
    Write-Utf8NoBom (Join-Path $settings "ProjectVersion.txt") "m_EditorVersion: 2022.3.62f3c1`nm_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)`n"
}

$unity = Find-UnityEditor
if ([string]::IsNullOrWhiteSpace($unity) -or -not (Test-Path -LiteralPath $unity)) {
    if ($RequireBundle -or -not (Test-Path -LiteralPath $bundlePath)) { throw "Unity Editor was not found and AuraTools visual bundle is missing." }
    Write-Warning "Unity Editor was not found; existing AuraTools visual bundle retained."
    return
}

$envBefore = $env:AURATOOLS_VISUAL_BUNDLE_OUTPUT
try {
    $env:AURATOOLS_VISUAL_BUNDLE_OUTPUT = $bundlePath
    $log = Join-Path $sourceRoot "auratools_visuals.unity-build.log"
    $process = Start-Process -FilePath $unity -WindowStyle Hidden -Wait -PassThru -ArgumentList @(
        "-batchmode", "-nographics", "-quit", "-projectPath", $UnityProjectPath,
        "-executeMethod", "AuraToolsVisualBundleBuilder.BuildVisualBundle", "-logFile", $log)
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $bundlePath)) {
        throw "AuraTools visual bundle build failed. See $log"
    }
}
finally {
    $env:AURATOOLS_VISUAL_BUNDLE_OUTPUT = $envBefore
}

Write-Host "AuraTools visual bundle built: $bundlePath"
