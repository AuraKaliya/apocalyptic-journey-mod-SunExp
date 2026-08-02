param(
    [Parameter(Mandatory = $true)]
    [string]$OwnerLogsDirectory,
    [switch]$IncludeControllerSettings
)

$ErrorActionPreference = "Stop"
$ownerLogs = [System.IO.Path]::GetFullPath($OwnerLogsDirectory)
$standardSuffix = [System.IO.Path]::Combine(
    "ModsData",
    "Logs",
    "AuraToolsExp")
$sharedSuffix = [System.IO.Path]::Combine(
    "ModsData",
    "AuraShared",
    "Logs",
    "AuraToolsExp")
if (-not $ownerLogs.EndsWith(
        $standardSuffix,
        [System.StringComparison]::OrdinalIgnoreCase) `
    -and -not $ownerLogs.EndsWith(
        $sharedSuffix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing combat learning cleanup outside the AuraToolsExp owner log directory: $ownerLogs"
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
        "Refusing combat learning cleanup while trainer processes are running: " +
        (($activeTrainerProcesses |
            ForEach-Object { "$($_.ProcessName)#$($_.Id)" }) -join ", "))
}

$modsDataRoot = if ($ownerLogs.EndsWith(
        $standardSuffix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Split-Path -Parent (Split-Path -Parent $ownerLogs)
} else {
    Split-Path -Parent (
        Split-Path -Parent (
            Split-Path -Parent $ownerLogs))
}
$modsDataRoot = [System.IO.Path]::GetFullPath($modsDataRoot)
$sharedRoot = if ($ownerLogs.EndsWith(
        $sharedSuffix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Split-Path -Parent (Split-Path -Parent $ownerLogs)
} else {
    Join-Path $modsDataRoot "AuraShared"
}
$sharedRoot = [System.IO.Path]::GetFullPath($sharedRoot)
$ownerConfigRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $sharedRoot "Config\Owners\AuraToolsExp"))
$ownerDataRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $sharedRoot "Data\Owners\AuraToolsExp"))

function Test-PathInside {
    param(
        [string]$Candidate,
        [string]$Root
    )
    $rootPrefix = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $resolved = [System.IO.Path]::GetFullPath($Candidate)
    return $resolved.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)
}

$directoryTargets = @(
    "training-snapshots",
    "candidate-archive",
    "champion-history",
    "model-library",
    "training-batches",
    "combat-simulation-results",
    "FoundationTrainer"
) | ForEach-Object {
    [System.IO.Path]::GetFullPath((Join-Path $ownerLogs $_))
}
$directoryTargets += [System.IO.Path]::GetFullPath(
    (Join-Path $ownerDataRoot "FoundationModels"))
$directoryTargets += @(
    [System.IO.Path]::GetFullPath(
        (Join-Path $ownerConfigRoot "AutoBattle"))
)
if ($IncludeControllerSettings) {
    $directoryTargets += [System.IO.Path]::GetFullPath(
        (Join-Path $ownerConfigRoot "FoundationTrainer"))
}

$filePrefixes = @(
    "auto-battle-training-",
    "auto-battle-candidate-",
    "auto-battle-model-candidate-",
    "auto-battle-search-model-candidate-",
    "auto-battle-policy-value-candidate-",
    "foundation-model-bundle-",
    "foundation-cleanup-manifest-",
    "live-combat-episodes-",
    "journey-episodes-"
)
$fileTargets = @(
    foreach ($file in Get-ChildItem -LiteralPath $ownerLogs `
        -File -ErrorAction SilentlyContinue) {
        $owned = $file.Name -eq "foundation-controller-session.json"
        foreach ($prefix in $filePrefixes) {
            if ($file.Name.StartsWith(
                $prefix,
                [System.StringComparison]::OrdinalIgnoreCase)) {
                $owned = $true
                break
            }
        }
        if ($owned) {
            [System.IO.Path]::GetFullPath($file.FullName)
        }
    }
)

foreach ($path in $directoryTargets) {
    if (-not (Test-PathInside $path $ownerLogs) `
        -and -not (Test-PathInside $path $ownerDataRoot) `
        -and -not (Test-PathInside $path $ownerConfigRoot)) {
        throw "Refusing combat learning directory cleanup outside approved roots: $path"
    }
}
foreach ($path in $fileTargets) {
    if (-not (Test-PathInside $path $ownerLogs)) {
        throw "Refusing combat learning file cleanup outside the owner log directory: $path"
    }
}

$existingDirectories = @(
    $directoryTargets |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container }
)
$inventory = @(
    $fileTargets |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        ForEach-Object { Get-Item -LiteralPath $_ }
    $directoryTargets |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } |
        ForEach-Object {
            Get-ChildItem -LiteralPath $_ -File -Recurse -ErrorAction Stop
        }
)
$removedFiles = @($inventory).Count
$removedBytes = (
    @($inventory) |
        Measure-Object -Property Length -Sum).Sum
if ($null -eq $removedBytes) {
    $removedBytes = 0
}

foreach ($path in $fileTargets) {
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        Remove-Item -LiteralPath $path -Force
    }
}
foreach ($path in $directoryTargets |
             Sort-Object { $_.Length } -Descending) {
    if (Test-Path -LiteralPath $path -PathType Container) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}

Write-Host (
    "Aura combat learning cleanup completed: files={0}, directories={1}, bytes={2}" -f
    $removedFiles,
    $existingDirectories.Count,
    [long]$removedBytes)
