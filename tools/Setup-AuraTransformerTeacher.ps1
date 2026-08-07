param(
    [ValidateSet("auto", "cpu", "cuda")]
    [string]$Backend = "auto",
    [string]$PythonExecutable = "auto",
    [string]$InstallDirectory = "",
    [string]$TorchVersion = "",
    [string]$TorchIndexUrl = "",
    [bool]$RegisterUserEnvironment = $true,
    [switch]$SkipPythonInstall,
    [switch]$SkipSelfTest
)

$ErrorActionPreference = "Stop"
$script:NvidiaCudaVersion = $null

function Invoke-CheckedPython {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [string]$Operation
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE"
    }
}

function Test-PythonExecutable {
    param([string]$Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }
    $command = Get-Command $Candidate -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $command) {
        return $null
    }
    try {
        $resolved = & $command.Source -c (
            "import os,sys;print(os.path.abspath(sys.executable))") 2>$null |
            Select-Object -Last 1
        if ($LASTEXITCODE -ne 0 -or
            [string]::IsNullOrWhiteSpace($resolved)) {
            return $null
        }
        $resolvedPath = $resolved.Trim()
        if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
            return [System.IO.Path]::GetFullPath($resolvedPath)
        }
    }
    catch {
        return $null
    }
    return $null
}

function Resolve-PythonExecutable {
    param([string]$Requested)

    if (-not [string]::Equals(
            $Requested,
            "auto",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        $resolved = Test-PythonExecutable -Candidate $Requested
        if ($null -eq $resolved) {
            throw "Requested Python executable is unavailable: $Requested"
        }
        return $resolved
    }

    $launcherCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Python\Launcher\py.exe"),
        "py.exe"
    )
    foreach ($launcher in $launcherCandidates) {
        $command = Get-Command $launcher -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $command) {
            continue
        }
        foreach ($selector in @("-3.11", "-3")) {
            try {
                $resolved = & $command.Source $selector -c (
                    "import os,sys;print(os.path.abspath(sys.executable))") `
                    2>$null | Select-Object -Last 1
                if ($LASTEXITCODE -eq 0 -and
                    -not [string]::IsNullOrWhiteSpace($resolved) -and
                    (Test-Path -LiteralPath $resolved.Trim() -PathType Leaf)) {
                    return [System.IO.Path]::GetFullPath($resolved.Trim())
                }
            }
            catch {
                continue
            }
        }
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:AURA_TRANSFORMER_PYTHON)) {
        $candidates.Add($env:AURA_TRANSFORMER_PYTHON)
    }
    $pythonRoot = Join-Path $env:LOCALAPPDATA "Programs\Python"
    if (Test-Path -LiteralPath $pythonRoot -PathType Container) {
        Get-ChildItem -LiteralPath $pythonRoot -Directory -Filter "Python*" |
            Sort-Object Name -Descending |
            ForEach-Object {
                $candidates.Add((Join-Path $_.FullName "python.exe"))
            }
    }
    $candidates.Add("python.exe")
    $candidates.Add("python3.exe")
    foreach ($candidate in $candidates) {
        $resolved = Test-PythonExecutable -Candidate $candidate
        if ($null -ne $resolved) {
            return $resolved
        }
    }
    return $null
}

function Install-BasePython {
    $winget = Get-Command "winget.exe" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -eq $winget) {
        throw (
            "Python was not found and winget is unavailable. Install Python " +
            "3.11 (64-bit), or pass -PythonExecutable with its full path.")
    }

    Write-Host "Python is not installed. Installing Python 3.11 for this user..."
    & $winget.Source install `
        --id Python.Python.3.11 `
        --exact `
        --source winget `
        --scope user `
        --silent `
        --disable-interactivity `
        --accept-package-agreements `
        --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "Python installation failed with exit code $LASTEXITCODE"
    }
}

function Test-NvidiaGpu {
    $candidates = @(
        "nvidia-smi.exe",
        (Join-Path $env:ProgramFiles "NVIDIA Corporation\NVSMI\nvidia-smi.exe")
    )
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $command) {
            continue
        }
        try {
            $gpuNames = & $command.Source `
                --query-gpu=name `
                --format=csv,noheader `
                2>$null
            if ($LASTEXITCODE -eq 0 -and $gpuNames.Count -gt 0) {
                $summary = & $command.Source 2>$null | Out-String
                $cudaMatch = [regex]::Match(
                    $summary,
                    "CUDA Version:\s*(?<version>\d+\.\d+)")
                if ($cudaMatch.Success) {
                    $script:NvidiaCudaVersion = [version]$cudaMatch.Groups[
                        "version"].Value
                }
                Write-Host ("NVIDIA GPU detected: " +
                    (($gpuNames | ForEach-Object { $_.Trim() }) -join ", "))
                if ($null -ne $script:NvidiaCudaVersion) {
                    Write-Host (
                        "NVIDIA driver CUDA capability: " +
                        $script:NvidiaCudaVersion)
                }
                return $true
            }
        }
        catch {
            continue
        }
    }
    return $false
}

function Get-DefaultCudaTorchIndexUrl {
    if ($null -ne $script:NvidiaCudaVersion -and
        $script:NvidiaCudaVersion -ge [version]"13.0") {
        return "https://download.pytorch.org/whl/cu130"
    }
    return "https://download.pytorch.org/whl/cu126"
}

function Get-InstallDirectory {
    param([string]$EffectiveBackend)

    if (-not [string]::IsNullOrWhiteSpace($InstallDirectory)) {
        return [System.IO.Path]::GetFullPath($InstallDirectory)
    }
    $localAppData = $env:LOCALAPPDATA
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = $env:USERPROFILE
    }
    return [System.IO.Path]::GetFullPath(
        (Join-Path $localAppData "AuraTF\$EffectiveBackend"))
}

function Install-TeacherEnvironment {
    param(
        [string]$BasePython,
        [ValidateSet("cpu", "cuda")]
        [string]$EffectiveBackend,
        [switch]$ForceTorchReinstall
    )

    $resolvedInstall = Get-InstallDirectory -EffectiveBackend $EffectiveBackend
    $installParent = [System.IO.Path]::GetDirectoryName($resolvedInstall)
    [System.IO.Directory]::CreateDirectory($installParent) | Out-Null
    $venvPython = Join-Path $resolvedInstall "Scripts\python.exe"

    if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
        $venvArguments = @("-m", "venv", $resolvedInstall)
        if (Test-Path -LiteralPath $resolvedInstall -PathType Container) {
            $venvArguments = @("-m", "venv", "--clear", $resolvedInstall)
        }
        Write-Host "Creating isolated Python environment: $resolvedInstall"
        Invoke-CheckedPython `
            -Executable $BasePython `
            -Arguments $venvArguments `
            -Operation "Python virtual environment creation"
    }
    if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
        throw "Virtual-environment Python is missing: $venvPython"
    }

    Write-Host "Updating pip and installing NumPy..."
    Invoke-CheckedPython `
        -Executable $venvPython `
        -Arguments @(
            "-m", "pip", "install", "--disable-pip-version-check",
            "--no-input", "--upgrade", "pip") `
        -Operation "pip upgrade"
    Invoke-CheckedPython `
        -Executable $venvPython `
        -Arguments @(
            "-m", "pip", "install", "--disable-pip-version-check",
            "--no-input", "--upgrade", "numpy") `
        -Operation "NumPy installation"

    $torchPackage = if ([string]::IsNullOrWhiteSpace($TorchVersion)) {
        "torch"
    }
    else {
        "torch==$($TorchVersion.Trim())"
    }
    $installArguments = @(
        "-m", "pip", "install", "--disable-pip-version-check",
        "--no-input", "--upgrade")
    if ($ForceTorchReinstall) {
        $installArguments += "--force-reinstall"
    }
    $installArguments += $torchPackage

    $effectiveIndexUrl = $TorchIndexUrl
    if ($EffectiveBackend -eq "cpu" -and
        ([string]::IsNullOrWhiteSpace($effectiveIndexUrl) -or
         $Backend -eq "auto")) {
        $effectiveIndexUrl = "https://download.pytorch.org/whl/cpu"
    }
    elseif ($EffectiveBackend -eq "cuda" -and
        [string]::IsNullOrWhiteSpace($effectiveIndexUrl)) {
        $effectiveIndexUrl = Get-DefaultCudaTorchIndexUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($effectiveIndexUrl)) {
        Write-Host "PyTorch wheel index: $effectiveIndexUrl"
        $installArguments += @("--index-url", $effectiveIndexUrl.Trim())
    }

    Write-Host "Installing PyTorch backend: $EffectiveBackend"
    Invoke-CheckedPython `
        -Executable $venvPython `
        -Arguments $installArguments `
        -Operation "PyTorch installation"

    $probe = @"
import json
import numpy
import torch
backend = "$EffectiveBackend"
cuda_available = bool(torch.cuda.is_available())
print(json.dumps({
    "python": __import__("sys").version.split()[0],
    "torch": torch.__version__,
    "numpy": numpy.__version__,
    "cuda_available": cuda_available,
    "device": torch.cuda.get_device_name(0) if cuda_available else "CPU"
}, ensure_ascii=True))
if backend == "cuda" and not cuda_available:
    raise SystemExit(3)
"@
    $probeOutput = & $venvPython -c $probe
    if ($LASTEXITCODE -ne 0) {
        throw (
            "PyTorch validation failed for backend $EffectiveBackend " +
            "with exit code $LASTEXITCODE")
    }
    Write-Host "Runtime probe: $($probeOutput | Select-Object -Last 1)"

    if (-not $SkipSelfTest) {
        $teacherScript = Join-Path `
            $PSScriptRoot `
            "transformer-teacher\train_teacher.py"
        if (-not (Test-Path -LiteralPath $teacherScript -PathType Leaf)) {
            $teacherScript = Join-Path `
                $PSScriptRoot `
                "TransformerTeacher\train_teacher.py"
        }
        if (-not (Test-Path -LiteralPath $teacherScript -PathType Leaf)) {
            throw "Transformer teacher self-test script is missing."
        }
        Write-Host "Running the Transformer teacher self-test..."
        Invoke-CheckedPython `
            -Executable $venvPython `
            -Arguments @(
                $teacherScript,
                "--self-test",
                "--backend", $EffectiveBackend,
                "--epochs", "2",
                "--cpu-threads", "0") `
            -Operation "Transformer teacher self-test"
    }

    return $venvPython
}

$basePython = Resolve-PythonExecutable -Requested $PythonExecutable
if ($null -eq $basePython) {
    if ($SkipPythonInstall) {
        throw "Python is unavailable and automatic Python installation is disabled."
    }
    Install-BasePython
    $basePython = Resolve-PythonExecutable -Requested "auto"
    if ($null -eq $basePython) {
        throw "Python 3.11 was installed but could not be resolved in this session."
    }
}
Write-Host "Base Python: $basePython"

$requestedBackend = $Backend
$effectiveBackend = $Backend
if ($Backend -eq "auto") {
    $effectiveBackend = if (Test-NvidiaGpu) { "cuda" } else { "cpu" }
}

try {
    $venvPython = Install-TeacherEnvironment `
        -BasePython $basePython `
        -EffectiveBackend $effectiveBackend
}
catch {
    if ($requestedBackend -ne "auto" -or $effectiveBackend -ne "cuda") {
        throw
    }
    Write-Warning (
        "CUDA setup did not pass validation. Falling back to CPU. " +
        $_.Exception.Message)
    $forceReinstall = -not [string]::IsNullOrWhiteSpace($InstallDirectory)
    $effectiveBackend = "cpu"
    $venvPython = Install-TeacherEnvironment `
        -BasePython $basePython `
        -EffectiveBackend $effectiveBackend `
        -ForceTorchReinstall:$forceReinstall
}

if ($RegisterUserEnvironment) {
    [System.Environment]::SetEnvironmentVariable(
        "AURA_TRANSFORMER_PYTHON",
        $venvPython,
        [System.EnvironmentVariableTarget]::User)
    $env:AURA_TRANSFORMER_PYTHON = $venvPython
}

Write-Host ""
Write-Host "Aura Transformer teacher is ready."
Write-Host "Python: $venvPython"
Write-Host "Effective backend: $effectiveBackend"
if ($RegisterUserEnvironment) {
    Write-Host "Registered AURA_TRANSFORMER_PYTHON for the current user."
}
