param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\RepositoryPath.psm1") -Force

$publishScript = Join-Path $repoRoot "tools\Publish-MainSharedConsumers.ps1"
$relative = Get-RepositoryRelativePath -RepoRoot $repoRoot -Path $publishScript
if ($relative -ne "tools/Publish-MainSharedConsumers.ps1") {
    throw "Repository-relative path normalization failed: $relative"
}

$rootRelative = Get-RepositoryRelativePath -RepoRoot ($repoRoot + "\") -Path $repoRoot
if ($rootRelative -ne ".") {
    throw "Repository root must normalize to '.': $rootRelative"
}

$outsidePaths = @(
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot "..\outside.txt")),
    [System.IO.Path]::GetFullPath($repoRoot + "-prefix-collision\outside.txt")
)
foreach ($outsidePath in $outsidePaths) {
    $rejected = $false
    try {
        [void](Get-RepositoryRelativePath -RepoRoot $repoRoot -Path $outsidePath)
    }
    catch {
        $rejected = $_.Exception.Message -like "Path escapes the repository:*"
    }
    if (-not $rejected) {
        throw "Repository boundary did not reject an outside path: $outsidePath"
    }
}

Write-Host "Repository path compatibility validation passed."
