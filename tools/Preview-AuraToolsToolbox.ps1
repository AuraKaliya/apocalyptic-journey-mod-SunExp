[CmdletBinding()]
param(
    [string]$OutputDirectory = "",
    [switch]$SkipCapture,
    [switch]$Open
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$previewRoot = Join-Path $PSScriptRoot "AuraToolsUiPreview"
$previewPage = Join-Path $previewRoot "index.html"
$captureScript = Join-Path $previewRoot "capture.cjs"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "output\playwright\aura-tools-toolbox"
}

$node = Get-Command node -ErrorAction SilentlyContinue
if ($null -eq $node) {
    throw "Node.js is required for the AuraTools UI preview. Install Node.js/npm and retry."
}

function Resolve-PlaywrightNodeModules {
    $candidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:AURA_PLAYWRIGHT_NODE_MODULES)) {
        $candidates.Add($env:AURA_PLAYWRIGHT_NODE_MODULES)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:NODE_PATH)) {
        foreach ($item in $env:NODE_PATH.Split([System.IO.Path]::PathSeparator)) {
            if (-not [string]::IsNullOrWhiteSpace($item)) {
                $candidates.Add($item)
            }
        }
    }
    $candidates.Add((Join-Path $previewRoot "node_modules"))
    $candidates.Add((Join-Path $repoRoot "node_modules"))
    $candidates.Add((Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\node\node_modules"))

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $candidate "playwright\package.json") -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return ""
}

$playwrightNodeModules = Resolve-PlaywrightNodeModules
if ([string]::IsNullOrWhiteSpace($playwrightNodeModules)) {
    throw @"
The Playwright Node package was not found.
Install it locally with:
  npm install --no-save playwright
  npx playwright install chromium
or set AURA_PLAYWRIGHT_NODE_MODULES to a node_modules directory containing Playwright.
"@
}
$env:NODE_PATH = $playwrightNodeModules

if (-not $SkipCapture) {
    [System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
    & $node.Source $captureScript "--output=$OutputDirectory"
    if ($LASTEXITCODE -ne 0) {
        throw "AuraTools toolbox Playwright preview failed with exit code $LASTEXITCODE."
    }
}

if ($Open) {
    Start-Process -FilePath $previewPage
}

Write-Host "AuraTools toolbox preview page: $previewPage"
if (-not $SkipCapture) {
    Write-Host "AuraTools toolbox preview output: $OutputDirectory"
}
