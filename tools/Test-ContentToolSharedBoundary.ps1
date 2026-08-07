param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Import-Module (Join-Path $repoRoot "tools\modules\ArchitectureBoundaryValidation.psm1") -Force
Invoke-ArchitectureBoundaryValidation -RepoRoot $repoRoot -RuleSet "content-tool"

Write-Host "Content/tool/shared boundary rules passed."
