[CmdletBinding()]
param(
    [string]$UnityPath = "",
    [string]$OutputDirectory = "",
    [switch]$SkipBuild,
    [switch]$SkipCapture,
    [switch]$Open
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "AuraToolsUnityUiPreview"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "output\unity\aura-tools-ui-preview"
}
$playerPath = Join-Path $OutputDirectory "AuraToolsUiPreview.exe"
$captureDirectory = Join-Path $OutputDirectory "captures"
$editorLog = Join-Path $OutputDirectory "unity-editor-build.log"
$playerLog = Join-Path $OutputDirectory "unity-player-capture.log"

function Sync-PreviewIcons {
    $source = Join-Path $repoRoot "AuraToolsExp\ModResource\Images\UI\ToolboxIcons"
    $target = Join-Path $projectPath "Assets\AuraToolsUnityUiPreview\Resources\ToolboxIcons"
    [System.IO.Directory]::CreateDirectory($target) | Out-Null
    $icons = @(Get-ChildItem -LiteralPath $source -Filter "*.png" -File)
    if ($icons.Count -ne 35) {
        throw "Expected 35 AuraTools toolbox icons, found $($icons.Count)."
    }
    $icons | Copy-Item -Destination $target -Force

    $nativeTarget = Join-Path $projectPath "Assets\AuraToolsUnityUiPreview\Resources\NativeUi"
    [System.IO.Directory]::CreateDirectory($nativeTarget) | Out-Null
    $auraUiAssets = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp\ui-img") -Filter "*.png" -File)
    $terriasUiAssets = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "Terrias\ModResource\Images\UI") -Filter "*.png" -File)
    $nativeSources = @{
        "native-button.png" = $auraUiAssets | Where-Object Length -eq 884 | Select-Object -First 1
        "native-panel-small.png" = $auraUiAssets | Where-Object Length -eq 280 | Select-Object -First 1
        "native-panel-large.png" = $terriasUiAssets | Where-Object Length -eq 479455 | Select-Object -First 1
        "native-selector.png" = $terriasUiAssets | Where-Object Length -eq 1658 | Select-Object -First 1
        "native-selector-selected.png" = $terriasUiAssets | Where-Object Length -eq 37762 | Select-Object -First 1
    }
    foreach ($entry in $nativeSources.GetEnumerator()) {
        if ($null -eq $entry.Value) {
            throw "Native UI source asset is missing for $($entry.Key)."
        }
        Copy-Item -LiteralPath $entry.Value.FullName -Destination (Join-Path $nativeTarget $entry.Key) -Force
    }

    $toolboxV2Source = Join-Path $repoRoot "AuraToolsExp\ModResource\Images\UI\ToolboxV2"
    $toolboxV2Target = Join-Path $projectPath "Assets\AuraToolsUnityUiPreview\Resources\ToolboxV2"
    [System.IO.Directory]::CreateDirectory($toolboxV2Target) | Out-Null
    $toolboxV2Assets = @(Get-ChildItem -LiteralPath $toolboxV2Source -Filter "*.png" -File)
    if ($toolboxV2Assets.Count -ne 5) {
        throw "Expected 5 Aura Toolbox V2 assets, found $($toolboxV2Assets.Count)."
    }
    $toolboxV2Assets | Copy-Item -Destination $toolboxV2Target -Force

    $cgPreviewTarget = Join-Path $projectPath "Assets\AuraToolsUnityUiPreview\Resources\CgPreview"
    [System.IO.Directory]::CreateDirectory($cgPreviewTarget) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot "AuraToolsExp\ModResource\DPSCG\DPS-CG.png") `
        -Destination (Join-Path $cgPreviewTarget "event-default.png") -Force
}

function Assert-PreviewSourceContract {
    $moduleIdsSource = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolModuleIds.cs")
    $previewModels = Get-Content -Raw -LiteralPath (Join-Path $projectPath "Assets\AuraToolsUnityUiPreview\Scripts\PreviewModels.cs")
    $moduleIds = [regex]::Matches($moduleIdsSource, 'public const string \w+ = "([^"]+)";') |
        ForEach-Object { $_.Groups[1].Value }
    if ($moduleIds.Count -ne 23) {
        throw "Expected 23 production AuraTools module ids, found $($moduleIds.Count)."
    }
    foreach ($moduleId in $moduleIds) {
        if ($previewModels -notmatch [regex]::Escape('"' + $moduleId + '"')) {
            throw "Unity preview module inventory is missing: $moduleId"
        }
    }
    $themeSource = Get-Content -Raw -LiteralPath (Join-Path $projectPath "Assets\AuraToolsUnityUiPreview\Scripts\PreviewTheme.cs")
    foreach ($token in @("CategoryWidth = 168f", "ToolboxHeaderHeight = 60f", "ModuleRowHeight = 78f")) {
        if ($themeSource -notmatch [regex]::Escape($token)) {
            throw "Unity preview layout token drifted: $token"
        }
    }
    $previewSource = (Get-ChildItem -LiteralPath (Join-Path $projectPath "Assets\AuraToolsUnityUiPreview") -Filter "*.cs" -Recurse -File |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
    foreach ($token in @(
            "PreviewAssets.NativeButton",
            "PreviewAssets.NativePanelLarge",
            "PreviewBooleanControl",
            "PreviewAssets.ToolboxSurface",
            "PreviewToolboxCheckboxControl",
            "PreviewUi.ToolboxIconButton")) {
        if ($previewSource -notmatch [regex]::Escape($token)) {
            throw "Unity preview native visual contract is missing: $token"
        }
    }
    if ($previewSource -match 'using\s+(?:Witch|AuraToolsExp\.Dll|Terrias\.Dll)' -or
        $previewSource -match 'Managed[/\\].*\.dll') {
        throw "Unity preview must remain independent from game and mod runtime assemblies."
    }
}

function Resolve-UnityEditor {
    param([string]$Requested)
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($Requested)) {
        $candidates.Add($Requested)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:UNITY_EDITOR_PATH)) {
        $candidates.Add($env:UNITY_EDITOR_PATH)
    }
    $candidates.Add("D:\UnityFile\2022.3.62f3c1\Editor\Unity.exe")
    $candidates.Add("C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe")
    $candidates.Add("C:\Program Files\Unity Hub\Editor\2022.3.62f3c1\Editor\Unity.exe")
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return ""
}

function Invoke-HiddenProcess {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [int]$TimeoutSeconds
    )
    $argumentLine = ($Arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' '
    $process = Start-Process -FilePath $FilePath -ArgumentList $argumentLine -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try { $process.Kill() } catch { }
        throw "Process timed out after $TimeoutSeconds seconds: $FilePath"
    }
    return $process.ExitCode
}

[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

if (-not $SkipBuild) {
    Sync-PreviewIcons
    Assert-PreviewSourceContract
    $unity = Resolve-UnityEditor $UnityPath
    if ([string]::IsNullOrWhiteSpace($unity)) {
        throw "Unity 2022.3.62f3c1 was not found. Pass -UnityPath or set UNITY_EDITOR_PATH."
    }
    $previousOutput = $env:AURA_TOOLS_UNITY_PREVIEW_OUTPUT
    try {
        $env:AURA_TOOLS_UNITY_PREVIEW_OUTPUT = $playerPath
        $exitCode = Invoke-HiddenProcess $unity @(
            "-batchmode",
            "-quit",
            "-projectPath", $projectPath,
            "-executeMethod", "AuraTools.UnityUiPreview.Editor.AuraToolsUiPreviewBuilder.BuildWindowsPlayer",
            "-logFile", $editorLog
        ) 600
    }
    finally {
        $env:AURA_TOOLS_UNITY_PREVIEW_OUTPUT = $previousOutput
    }
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $playerPath -PathType Leaf)) {
        throw "Unity preview player build failed with exit code $exitCode. See $editorLog"
    }
}

if (-not (Test-Path -LiteralPath $playerPath -PathType Leaf)) {
    throw "AuraTools Unity UI preview player is missing: $playerPath"
}

if (-not $SkipCapture) {
    [System.IO.Directory]::CreateDirectory($captureDirectory) | Out-Null
    $exitCode = Invoke-HiddenProcess $playerPath @(
        "-previewAutoCapture",
        "-previewCaptureOutput=$captureDirectory",
        "-screen-fullscreen", "0",
        "-screen-width", "1280",
        "-screen-height", "720",
        "-logFile", $playerLog
    ) 180
    $reportPath = Join-Path $captureDirectory "report.json"
    if ($exitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "Unity preview capture failed with exit code $exitCode. See $playerLog"
    }
    $report = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if (-not $report.passed -or $report.captures -ne 22) {
        throw "Unity preview validation failed: $($report.errors -join '; ')"
    }
    foreach ($result in $report.results) {
        if (-not (Test-Path -LiteralPath $result.file -PathType Leaf)) {
            throw "Unity preview capture is missing: $($result.file)"
        }
    }
    Write-Host "AuraTools Unity UI preview passed: $($report.captures) captures."
}

if ($Open) {
    Start-Process -FilePath $playerPath
}

Write-Host "AuraTools Unity UI preview player: $playerPath"
if (-not $SkipCapture) {
    Write-Host "AuraTools Unity UI preview captures: $captureDirectory"
}
