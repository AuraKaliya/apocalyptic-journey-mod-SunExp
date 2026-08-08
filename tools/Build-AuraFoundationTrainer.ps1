param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory = "",
    [switch]$StopRunningTrainer,
    [ValidateRange(5, 300)]
    [int]$TrainerShutdownTimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraFoundationTrainer.Worker\AuraFoundationTrainer.Worker.csproj"
$controlCenterProject = Join-Path $repoRoot "AuraFoundationTrainer.ControlCenter\AuraFoundationTrainer.ControlCenter.csproj"
$transformerTeacherSource = Join-Path $repoRoot "tools\transformer-teacher"
$transformerSetupSource = Join-Path $repoRoot "tools\Setup-AuraTransformerTeacher.ps1"
$transformerInstallerSource = Join-Path $repoRoot "tools\Install-AuraPyTorch.cmd"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "AuraToolsExp\TrainingWorker"
}
$resolvedRepoRoot = [System.IO.Path]::GetFullPath($repoRoot)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$expectedRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "AuraToolsExp\TrainingWorker"))
if (-not $resolvedOutput.StartsWith($expectedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Foundation trainer output must stay inside $expectedRoot"
}

function Get-NormalizedTrainerPath {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith("\\?\UNC\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "\\" + $fullPath.Substring(8)
    }
    if ($fullPath.StartsWith("\\?\", [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith("\??\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring(4)
    }
    return $fullPath
}

function Get-RunningTrainerProcesses {
    param([string[]]$TargetPaths)

    $targets = @{}
    foreach ($path in $TargetPaths) {
        $targets[(Get-NormalizedTrainerPath -Path $path)] = $true
    }
    $candidates = @(
        Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
            Where-Object {
                $_.Name -eq "AuraFoundationTrainer.Worker.exe" -or
                $_.Name -eq "AuraFoundationTrainer.ControlCenter.exe"
            }
    )
    return @(
        $candidates |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                $targets.ContainsKey(
                    (Get-NormalizedTrainerPath -Path $_.ExecutablePath))
            } |
            Sort-Object ProcessId
    )
}

function Format-TrainerProcesses {
    param([object[]]$Processes)

    return (
        $Processes |
            ForEach-Object {
                $command = if ([string]::IsNullOrWhiteSpace($_.CommandLine)) {
                    $_.ExecutablePath
                }
                else {
                    $_.CommandLine
                }
                "PID=$($_.ProcessId), name=$($_.Name), command=$command"
            }
    ) -join [System.Environment]::NewLine
}

$worker = Join-Path $resolvedOutput "AuraFoundationTrainer.Worker.exe"
$controlCenter = Join-Path $resolvedOutput "AuraFoundationTrainer.ControlCenter.exe"
$trainerTargets = @($worker, $controlCenter)

function Wait-TrainerProcessesExit {
    param(
        [string[]]$TargetPaths,
        [string[]]$Names,
        [int]$TimeoutSeconds
    )

    $deadline = [System.DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $remaining = @(
            Get-RunningTrainerProcesses -TargetPaths $TargetPaths |
                Where-Object { $Names -contains $_.Name }
        )
        if ($remaining.Count -eq 0) {
            return @()
        }
        Start-Sleep -Milliseconds 250
    } while ([System.DateTime]::UtcNow -lt $deadline)
    return @(
        Get-RunningTrainerProcesses -TargetPaths $TargetPaths |
            Where-Object { $Names -contains $_.Name }
    )
}

function Request-WorkerCancellation {
    param([object]$ProcessInfo)

    $match = [System.Text.RegularExpressions.Regex]::Match(
        [string]$ProcessInfo.CommandLine,
        '(?i)(?:^|\s)--job\s+(?:"([^"]+)"|(\S+))')
    if (-not $match.Success) {
        return $false
    }
    $jobPath = if ($match.Groups[1].Success) {
        $match.Groups[1].Value
    }
    else {
        $match.Groups[2].Value
    }
    if (-not (Test-Path -LiteralPath $jobPath -PathType Leaf)) {
        return $false
    }
    try {
        $jobText = Get-Content -LiteralPath $jobPath -Raw
        $cancellationPath = ""
        try {
            $job = $jobText | ConvertFrom-Json
            $cancellationPath = [string]$job.CancellationPath
        }
        catch {
            # Cancellation must remain available even when a legacy or
            # concurrently produced large job document cannot be fully parsed.
            # The path is a top-level scalar written near the document header.
            $pathMatch = [System.Text.RegularExpressions.Regex]::Match(
                $jobText,
                '"CancellationPath"\s*:\s*"((?:\\.|[^"\\])*)"')
            if ($pathMatch.Success) {
                $cancellationPath =
                    [System.Text.RegularExpressions.Regex]::Unescape(
                        $pathMatch.Groups[1].Value)
            }
        }
        if ([string]::IsNullOrWhiteSpace($cancellationPath)) {
            return $false
        }
        if (-not [System.IO.Path]::IsPathRooted($cancellationPath)) {
            $cancellationPath = Join-Path (
                Split-Path -Parent $jobPath) $cancellationPath
        }
        $cancellationPath =
            [System.IO.Path]::GetFullPath($cancellationPath)
        [System.IO.Directory]::CreateDirectory(
            [System.IO.Path]::GetDirectoryName($cancellationPath)) |
            Out-Null
        [System.IO.File]::WriteAllText(
            $cancellationPath,
            [System.DateTime]::UtcNow.ToString("O"))
        Write-Host (
            "Requested graceful foundation worker cancellation: PID=" +
            $ProcessInfo.ProcessId +
            ", cancellation=" +
            $cancellationPath) -ForegroundColor Yellow
        return $true
    }
    catch {
        Write-Warning (
            "Could not request graceful cancellation for worker PID=" +
            $ProcessInfo.ProcessId +
            ": " +
            $_.Exception.Message)
        return $false
    }
}

function Stop-RunningTrainerProcesses {
    param(
        [string[]]$TargetPaths,
        [int]$TimeoutSeconds
    )

    $running = @(Get-RunningTrainerProcesses -TargetPaths $TargetPaths)
    $workers = @(
        $running |
            Where-Object {
                $_.Name -eq "AuraFoundationTrainer.Worker.exe"
            }
    )
    $unrequestedWorkers = @(
        $workers |
            Where-Object {
                -not (Request-WorkerCancellation -ProcessInfo $_)
            }
    )
    if ($unrequestedWorkers.Count -gt 0) {
        throw (
            "Cannot safely stop running foundation workers because their " +
            "job cancellation path could not be resolved. Cancel them from " +
            "the Control Center first." +
            [System.Environment]::NewLine +
            (Format-TrainerProcesses -Processes $unrequestedWorkers))
    }
    $remainingWorkers = @(
        Wait-TrainerProcessesExit `
            -TargetPaths $TargetPaths `
            -Names @("AuraFoundationTrainer.Worker.exe") `
            -TimeoutSeconds $TimeoutSeconds
    )
    if ($remainingWorkers.Count -gt 0) {
        throw (
            "Foundation workers did not exit after graceful cancellation. " +
            "Their checkpoints were not confirmed; no process was killed." +
            [System.Environment]::NewLine +
            (Format-TrainerProcesses -Processes $remainingWorkers))
    }

    $controlCenters = @(
        Get-RunningTrainerProcesses -TargetPaths $TargetPaths |
            Where-Object {
                $_.Name -eq "AuraFoundationTrainer.ControlCenter.exe"
            }
    )
    foreach ($processInfo in $controlCenters) {
        $process = Get-Process `
            -Id $processInfo.ProcessId `
            -ErrorAction SilentlyContinue
        if ($null -ne $process) {
            $null = $process.CloseMainWindow()
        }
    }
    $remainingControlCenters = @(
        Wait-TrainerProcessesExit `
            -TargetPaths $TargetPaths `
            -Names @("AuraFoundationTrainer.ControlCenter.exe") `
            -TimeoutSeconds $TimeoutSeconds
    )
    if ($remainingControlCenters.Count -gt 0) {
        throw (
            "Foundation Control Center did not close. No process was killed; " +
            "close it manually and rebuild." +
            [System.Environment]::NewLine +
            (Format-TrainerProcesses -Processes $remainingControlCenters))
    }
}

$runningTrainers = @(
    Get-RunningTrainerProcesses -TargetPaths $trainerTargets
)
if ($runningTrainers.Count -gt 0) {
    if (-not $StopRunningTrainer) {
        throw (
            "Foundation trainer binaries are currently running and Windows " +
            "will not allow them to be replaced." +
            [System.Environment]::NewLine +
            (Format-TrainerProcesses -Processes $runningTrainers) +
            [System.Environment]::NewLine +
            "Close the Control Center and wait for training to finish, or " +
            "rerun this script with -StopRunningTrainer to request graceful " +
            "worker cancellation and checkpoint retention. No process is " +
            "terminated automatically.")
    }
    Stop-RunningTrainerProcesses `
        -TargetPaths $trainerTargets `
        -TimeoutSeconds $TrainerShutdownTimeoutSeconds
}

function Invoke-FoundationPublish {
    param(
        [string]$ProjectPath,
        [string]$StageDirectory,
        [string]$DisplayName
    )

    & dotnet publish $ProjectPath `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        --nologo `
        -v:minimal `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -o $StageDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "$DisplayName publish failed with exit code $LASTEXITCODE"
    }
}

function Copy-PublishedFileWithRetry {
    param(
        [string]$Source,
        [string]$Destination,
        [int]$Attempts = 20
    )

    $lastFailure = $null
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            [System.IO.File]::Copy($Source, $Destination, $true)
            return
        }
        catch [System.IO.IOException] {
            $lastFailure = $_.Exception
            if ($attempt -lt $Attempts) {
                Start-Sleep -Milliseconds 250
            }
        }
    }
    $holders = @(
        Get-RunningTrainerProcesses -TargetPaths $trainerTargets
    )
    $diagnostic = if ($holders.Count -gt 0) {
        [System.Environment]::NewLine +
        (Format-TrainerProcesses -Processes $holders)
    }
    else {
        [System.Environment]::NewLine +
        "No trainer process owns the target path. Antivirus, indexing, or " +
        "another mapped-file consumer may be holding it temporarily."
    }
    throw [System.IO.IOException]::new(
        "Could not deploy published trainer file: $Destination" +
        $diagnostic,
        $lastFailure)
}

$stageBase = [System.IO.Path]::GetFullPath(
    (Join-Path $resolvedOutput ".publish-staging"))
$stageRoot = Join-Path $stageBase (
    [System.Guid]::NewGuid().ToString("N"))
$workerStage = Join-Path $stageRoot "worker"
$controlCenterStage = Join-Path $stageRoot "control-center"

try {
    New-Item -ItemType Directory -Force -Path $workerStage |
        Out-Null
    New-Item -ItemType Directory -Force -Path $controlCenterStage |
        Out-Null
    Invoke-FoundationPublish `
        -ProjectPath $project `
        -StageDirectory $workerStage `
        -DisplayName "Aura foundation trainer"
    Invoke-FoundationPublish `
        -ProjectPath $controlCenterProject `
        -StageDirectory $controlCenterStage `
        -DisplayName "Aura foundation trainer control center"

    New-Item -ItemType Directory -Force -Path $resolvedOutput |
        Out-Null
    foreach ($file in @(
        Get-ChildItem -LiteralPath $workerStage -File
        Get-ChildItem -LiteralPath $controlCenterStage -File
    )) {
        Copy-PublishedFileWithRetry `
            -Source $file.FullName `
            -Destination (Join-Path $resolvedOutput $file.Name)
    }
    $transformerTeacherOutput = Join-Path `
        $resolvedOutput `
        "TransformerTeacher"
    New-Item `
        -ItemType Directory `
        -Force `
        -Path $transformerTeacherOutput |
        Out-Null
    foreach ($file in @(
        Get-ChildItem `
            -LiteralPath $transformerTeacherSource `
            -Filter "*.py" `
            -File
    )) {
        Copy-PublishedFileWithRetry `
            -Source $file.FullName `
            -Destination (Join-Path $transformerTeacherOutput $file.Name)
    }
    Copy-PublishedFileWithRetry `
        -Source $transformerSetupSource `
        -Destination (Join-Path $resolvedOutput (
            Split-Path -Leaf $transformerSetupSource))
    Copy-PublishedFileWithRetry `
        -Source $transformerInstallerSource `
        -Destination (Join-Path $resolvedOutput (
            Split-Path -Leaf $transformerInstallerSource))
}
finally {
    $resolvedStageRoot = [System.IO.Path]::GetFullPath($stageRoot)
    if ($resolvedStageRoot.StartsWith(
            $stageBase + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedStageRoot -PathType Container)) {
        Remove-Item -LiteralPath $resolvedStageRoot -Recurse -Force
    }
}

if (-not (Test-Path -LiteralPath $worker -PathType Leaf)) {
    throw "Published foundation trainer is missing: $worker"
}
if (-not (Test-Path -LiteralPath $controlCenter -PathType Leaf)) {
    throw "Published foundation trainer control center is missing: $controlCenter"
}
$publishedTeacher = Join-Path `
    $resolvedOutput `
    "TransformerTeacher\train_teacher.py"
if (-not (Test-Path -LiteralPath $publishedTeacher -PathType Leaf)) {
    throw "Published Transformer teacher is missing: $publishedTeacher"
}
$sourceTeacher = Join-Path $transformerTeacherSource "train_teacher.py"
$sourceTeacherSha256 = (Get-FileHash `
    -LiteralPath $sourceTeacher `
    -Algorithm SHA256).Hash
$publishedTeacherSha256 = (Get-FileHash `
    -LiteralPath $publishedTeacher `
    -Algorithm SHA256).Hash
if ($sourceTeacherSha256 -ne $publishedTeacherSha256) {
    throw (
        "Published Transformer teacher SHA256 does not match its source: " `
        + "source=$sourceTeacherSha256, published=$publishedTeacherSha256")
}
$publishedInstaller = Join-Path $resolvedOutput "Install-AuraPyTorch.cmd"
if (-not (Test-Path -LiteralPath $publishedInstaller -PathType Leaf)) {
    throw "Published PyTorch installer is missing: $publishedInstaller"
}
Write-Host "Aura foundation trainer published: $worker"
Write-Host "Aura foundation trainer control center published: $controlCenter"
