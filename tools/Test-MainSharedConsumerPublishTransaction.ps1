param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force

$windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) {
    throw "Windows PowerShell 5.1 is required for the publish compatibility fixture."
}

$consumers = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification product -DefaultOnly)
$manifestPath = Join-Path $repoRoot "artifacts\shared-release\$Configuration\shared-package-manifest.json"
$trackedPaths = New-Object System.Collections.Generic.List[string]
foreach ($consumer in $consumers) {
    $packageRoot = Resolve-ConsumerPath `
        -RepoRoot $repoRoot `
        -RelativePath ([string]$consumer.packagePath)
    $trackedPaths.Add((Join-Path $packageRoot "Entry.dll"))
    $trackedPaths.Add((Join-Path $packageRoot "Aura.Shared.dll"))
}
$trackedPaths.Add($manifestPath)

$beforeHashes = @{}
foreach ($path in $trackedPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Publish transaction fixture input is missing: $path"
    }
    $beforeHashes[$path] = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
}

$manifestLock = [System.IO.File]::Open(
    $manifestPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::None)
try {
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $childOutput = @(& $windowsPowerShell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $repoRoot "tools\Publish-MainSharedConsumers.ps1") `
        -Configuration $Configuration 2>&1)
    $childExitCode = $LASTEXITCODE
}
finally {
    $manifestLock.Dispose()
    $ErrorActionPreference = $previousErrorAction
}

$childText = $childOutput | Out-String
$normalizedChildText = $childText -replace '\s', ''
$expectedFailure = ("Publish commit failed: $manifestPath") -replace '\s', ''
if ($childExitCode -eq 0) {
    throw "Publish transaction fixture unexpectedly committed while the manifest was locked."
}
if (-not $normalizedChildText.Contains($expectedFailure)) {
    throw "Publish transaction fixture failed before the locked manifest commit: $childText"
}
if ($normalizedChildText.Contains("Publishrollbackfailed:")) {
    throw "Publish transaction rollback reported a failure: $childText"
}

foreach ($path in $trackedPaths) {
    $afterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($afterHash -ne $beforeHashes[$path]) {
        throw "Publish transaction rollback did not restore: $path"
    }
}

$targetDirectories = @($trackedPaths | ForEach-Object { Split-Path -Parent $_ } | Sort-Object -Unique)
$leftovers = @(
    foreach ($directory in $targetDirectories) {
        Get-ChildItem -LiteralPath $directory -File | Where-Object {
            $_.Name -match '\.publish-[0-9a-f]{32}\.(?:tmp|bak)$'
        }
    }
)
if ($leftovers.Count -gt 0) {
    throw "Publish transaction rollback left temporary files: $($leftovers.FullName -join ', ')"
}

$global:LASTEXITCODE = 0
Write-Host "Main shared consumer publish rollback validation passed."
