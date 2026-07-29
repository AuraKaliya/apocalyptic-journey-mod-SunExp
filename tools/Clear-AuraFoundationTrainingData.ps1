param(
    [Parameter(Mandatory = $true)]
    [string]$OwnerLogsDirectory
)

$ErrorActionPreference = "Stop"
$ownerLogs = [System.IO.Path]::GetFullPath($OwnerLogsDirectory)
$expectedSuffixes = @(
    [System.IO.Path]::Combine(
        "ModsData",
        "Logs",
        "AuraToolsExp"),
    [System.IO.Path]::Combine(
        "ModsData",
        "AuraShared",
        "Logs",
        "AuraToolsExp")
)
if (-not ($expectedSuffixes | Where-Object {
        $ownerLogs.EndsWith(
            $_,
            [System.StringComparison]::OrdinalIgnoreCase)
    })) {
    throw "Refusing foundation cleanup outside the AuraToolsExp owner log directory: $ownerLogs"
}
if (-not (Test-Path -LiteralPath $ownerLogs -PathType Container)) {
    throw "AuraToolsExp owner log directory is missing: $ownerLogs"
}
$activeTrainerProcesses = @(
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessName -in @(
                "AuraFoundationTrainer.Worker",
                "AuraFoundationTrainer.ControlCenter")
        }
)
if ($activeTrainerProcesses.Count -gt 0) {
    throw (
        "Refusing foundation cleanup while trainer processes are running: " +
        (($activeTrainerProcesses |
            ForEach-Object { "$($_.ProcessName)#$($_.Id)" }) -join ", "))
}

$resultRoot = Join-Path $ownerLogs "combat-simulation-results"
$successArchiveRoot = Join-Path $resultRoot "foundation-success-cases"
$targets = New-Object System.Collections.Generic.List[string]
$directoryTargets = New-Object System.Collections.Generic.List[string]
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
        $directoryTargets.Add($successArchiveRoot)
        Get-ChildItem -LiteralPath $successArchiveRoot -File -Recurse |
            ForEach-Object { $targets.Add($_.FullName) }
    }
    Get-ChildItem -LiteralPath $resultRoot -Directory |
        Where-Object {
            $_.Name -like "foundation-controller-*" `
            -or $_.Name -eq "foundation-controller-checkpoint"
        } |
        ForEach-Object {
            $directoryTargets.Add($_.FullName)
            Get-ChildItem -LiteralPath $_.FullName -File -Recurse |
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
$resolvedDirectories = @(
    $directoryTargets |
        ForEach-Object { [System.IO.Path]::GetFullPath($_) } |
        Sort-Object -Unique
)
foreach ($path in $resolvedDirectories) {
    if (-not $path.StartsWith(
            $ownerPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing foundation directory cleanup outside the resolved owner directory: $path"
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
foreach ($path in $resolvedDirectories |
             Sort-Object { $_.Length } -Descending) {
    if (Test-Path -LiteralPath $path -PathType Container) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

$sessionPath = Join-Path $ownerLogs "foundation-controller-session.json"
if (Test-Path -LiteralPath $sessionPath -PathType Leaf) {
    Remove-Item -LiteralPath $sessionPath -Force
}

$modsDataRoot = if ($ownerLogs.EndsWith(
        [System.IO.Path]::Combine("ModsData", "Logs", "AuraToolsExp"),
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Split-Path -Parent (Split-Path -Parent $ownerLogs)
} else {
    Split-Path -Parent (
        Split-Path -Parent (
            Split-Path -Parent $ownerLogs))
}
$controllerSettingsPath = Join-Path $modsDataRoot (
    "Config\Owners\AuraToolsExp\FoundationTrainer\controller-settings.json")
if (Test-Path -LiteralPath $controllerSettingsPath -PathType Leaf) {
    $controllerSettings = Get-Content -Raw -Encoding UTF8 `
        -LiteralPath $controllerSettingsPath | ConvertFrom-Json
    $controllerSettings.LastRunDirectory = ""
    $controllerSettings.ContinueGeneration = 0
    [System.IO.File]::WriteAllText(
        $controllerSettingsPath,
        ($controllerSettings | ConvertTo-Json -Depth 20),
        (New-Object System.Text.UTF8Encoding($false)))
}

$removedBytes = (
    $manifest.files |
        ForEach-Object { [long]$_["bytes"] } |
        Measure-Object -Sum).Sum
Write-Host (
    "Aura foundation cleanup completed: files={0}, directories={1}, bytes={2}, manifest={3}" -f
    $resolvedTargets.Count,
    $resolvedDirectories.Count,
    $removedBytes,
    $manifestPath)
