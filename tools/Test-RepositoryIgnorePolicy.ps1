$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$ignorePath = Join-Path $repoRoot ".gitignore"
$ignoreLines = Get-Content -LiteralPath $ignorePath |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith("#") }

foreach ($forbidden in @("*.dll", "*.exe", "*.meta", "Assets/", "Packages/", "ProjectSettings/")) {
    if ($ignoreLines -contains $forbidden) {
        throw "Repository ignore policy is too broad: $forbidden"
    }
}

function Test-Ignored {
    param([string]$Path)
    & git -C $repoRoot check-ignore -q -- $Path
    return $LASTEXITCODE -eq 0
}

$mustRemainAddable = @(
    "AuraToolsExp-Dev/Features/Settings/ToolboxVisualSpec.cs",
    "AuraToolsExp/ModResource/Images/UI/ToolboxV2/toolbox-surface-9slice.png",
    "AuraToolsExp/TrainingWorker/AuraFoundationTrainer.SimulationViewer.exe",
    "AuraToolsUnityUiPreview/Assets/AuraToolsUnityUiPreview/Scripts/ToolboxPreviewPage.cs",
    "AuraToolsUnityUiPreview/Assets/AuraToolsUnityUiPreview/Scripts/ToolboxPreviewPage.cs.meta",
    "AuraToolsUnityUiPreview/Packages/manifest.json",
    "AuraToolsUnityUiPreview/ProjectSettings/ProjectVersion.txt",
    "tools/AuraToolsUiPreview/index.html",
    "docs/AuraToolsExp/toolbox-ui-component-redesign-plan.md"
)
foreach ($path in $mustRemainAddable) {
    if (Test-Ignored $path) {
        throw "Required repository surface is ignored: $path"
    }
}

$mustBeIgnored = @(
    "AuraToolsExp-Dev/bin/Release/net472/AuraToolsExp.Aura.dll",
    "AuraToolsExp-Dev/obj/project.assets.json",
    "AuraToolsUnityUiPreview/Library/ArtifactDB",
    "AuraToolsUnityUiPreview/Logs/AssetImportWorker0.log",
    "AuraToolsUnityUiPreview/Assets/AuraToolsUnityUiPreview/Resources/ToolboxV2/toolbox-surface-9slice.png",
    "AuraToolsUnityUiPreview/Assets/AuraToolsUnityUiPreview/Scenes/SettingsUiPreview.unity",
    "output/unity/aura-tools-ui-preview/AuraToolsUiPreview.exe",
    "output/playwright/aura-tools-toolbox/report.json",
    "outputs/new-report.xlsx",
    "artifacts/AuraToolsExp/toolbox-v2-components-contact-sheet.png",
    "tools/AuraToolsUiPreview/node_modules/playwright/package.json",
    "tools/__pycache__/preview.cpython-312.pyc",
    "ModsData/AuraShared/Logs/preview.log",
    "tmp/preview.tmp"
)
foreach ($path in $mustBeIgnored) {
    if (-not (Test-Ignored $path)) {
        throw "Generated or local repository surface is not ignored: $path"
    }
}

Write-Host "Repository ignore policy passed: addable=$($mustRemainAddable.Count), ignored=$($mustBeIgnored.Count)."
