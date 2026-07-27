param(
    [Parameter(Mandatory = $true)]
    [string]$OwnerLogsDirectory
)

$ErrorActionPreference = "Stop"
$ownerLogs = [System.IO.Path]::GetFullPath($OwnerLogsDirectory)
$expectedSuffix = [System.IO.Path]::Combine(
    "ModsData",
    "AuraShared",
    "Logs",
    "AuraToolsExp")
if (-not $ownerLogs.EndsWith(
        $expectedSuffix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing foundation cleanup outside the AuraToolsExp owner log directory: $ownerLogs"
}
if (-not (Test-Path -LiteralPath $ownerLogs -PathType Container)) {
    throw "AuraToolsExp owner log directory is missing: $ownerLogs"
}

$resultRoot = Join-Path $ownerLogs "combat-simulation-results"
$successArchiveRoot = Join-Path $resultRoot "foundation-success-cases"
$targets = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $ownerLogs -File -Filter "foundation-model-bundle-*.json" |
    ForEach-Object { $targets.Add($_.FullName) }

$exactNames = @(
    "foundation-worker-progress.json",
    "foundation-worker-result.json",
    "foundation-worker-job.json",
    "foundation-worker.cancel"
)
if (Test-Path -LiteralPath $resultRoot -PathType Container) {
    Get-ChildItem -LiteralPath $resultRoot -File |
        Where-Object {
            $_.Name -in $exactNames `
            -or $_.Name -like "foundation-training-checkpoint-v*" `
            -or $_.Name -like "foundation-training-checkpoint-episodes-v*"
        } |
        ForEach-Object { $targets.Add($_.FullName) }
    Get-ChildItem -LiteralPath $resultRoot -Directory -Filter "*-foundation" |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_.FullName -File |
                Where-Object {
                    $_.Name -in $exactNames `
                    -or $_.Name -like "foundation-training-episodes-v*.jsonl" `
                    -or $_.Name -like "foundation-training-checkpoint-v*" `
                    -or $_.Name -like "foundation-training-checkpoint-episodes-v*" `
                    -or $_.Name -like "foundation-success-case-index-v*" `
                    -or $_.Name -like "foundation-success-analysis-v*" `
                    -or $_.Name -like "foundation-case-observations-v*"
                } |
                ForEach-Object { $targets.Add($_.FullName) }
        }
    if (Test-Path -LiteralPath $successArchiveRoot -PathType Container) {
        Get-ChildItem -LiteralPath $successArchiveRoot -File -Recurse |
            ForEach-Object { $targets.Add($_.FullName) }
    }
}

$resolvedTargets = @(
    $targets |
        ForEach-Object { [System.IO.Path]::GetFullPath($_) } |
        Sort-Object -Unique
)
$ownerPrefix = $ownerLogs.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
foreach ($path in $resolvedTargets) {
    if (-not $path.StartsWith(
            $ownerPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing foundation cleanup outside the resolved owner directory: $path"
    }
}

$manifestPath = Join-Path $ownerLogs (
    "foundation-cleanup-manifest-" +
    [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss") +
    ".json")
$manifest = [ordered]@{
    schemaVersion = 1
    cleanupKind = "foundation-training-only"
    createdUtc = [DateTime]::UtcNow
    retainedHistoricalReports = $true
    files = @(
        $resolvedTargets | ForEach-Object {
            $item = Get-Item -LiteralPath $_
            [ordered]@{
                path = $item.FullName
                bytes = $item.Length
                lastWriteUtc = $item.LastWriteTimeUtc
            }
        }
    )
}
[System.IO.File]::WriteAllText(
    $manifestPath,
    ($manifest | ConvertTo-Json -Depth 10),
    (New-Object System.Text.UTF8Encoding($false)))

foreach ($path in $resolvedTargets) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}

$removedBytes = (
    $manifest.files |
        ForEach-Object { [long]$_["bytes"] } |
        Measure-Object -Sum).Sum
Write-Host (
    "Aura foundation cleanup completed: files={0}, bytes={1}, manifest={2}" -f
    $resolvedTargets.Count,
    $removedBytes,
    $manifestPath)
