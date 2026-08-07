param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Import-Module (Join-Path $repoRoot "tools\modules\ArchitectureBoundaryValidation.psm1") -Force
Invoke-ArchitectureBoundaryValidation -RepoRoot $repoRoot -RuleSet "shared"

Write-Host "Shared architecture guideline assertions passed."
