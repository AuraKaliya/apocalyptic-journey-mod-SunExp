param(
    [ValidateSet("cpu", "cuda")]
    [string]$Backend = "cpu",
    [string]$PythonExecutable = "python",
    [string]$InstallDirectory = "",
    [string]$TorchVersion = "",
    [string]$TorchIndexUrl = "",
    [bool]$RegisterUserEnvironment = $true,
    [switch]$SkipSelfTest
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path `
        $env:LOCALAPPDATA `
        "AuraTF\$Backend"
}
$resolvedInstall = [System.IO.Path]::GetFullPath($InstallDirectory)
$installParent = [System.IO.Path]::GetDirectoryName($resolvedInstall)
[System.IO.Directory]::CreateDirectory($installParent) | Out-Null

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

if (-not (Test-Path -LiteralPath $resolvedInstall -PathType Container)) {
    Invoke-CheckedPython `
        -Executable $PythonExecutable `
        -Arguments @("-m", "venv", $resolvedInstall) `
        -Operation "Python virtual environment creation"
}

$venvPython = Join-Path $resolvedInstall "Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    throw "Virtual-environment Python is missing: $venvPython"
}

Invoke-CheckedPython `
    -Executable $venvPython `
    -Arguments @("-m", "pip", "install", "--upgrade", "pip") `
    -Operation "pip upgrade"
Invoke-CheckedPython `
    -Executable $venvPython `
    -Arguments @("-m", "pip", "install", "numpy") `
    -Operation "NumPy installation"

$torchPackage = if ([string]::IsNullOrWhiteSpace($TorchVersion)) {
    "torch"
}
else {
    "torch==$($TorchVersion.Trim())"
}
$installArguments = @("-m", "pip", "install", $torchPackage)
if ([string]::IsNullOrWhiteSpace($TorchIndexUrl) -and $Backend -eq "cpu") {
    $TorchIndexUrl = "https://download.pytorch.org/whl/cpu"
}
if (-not [string]::IsNullOrWhiteSpace($TorchIndexUrl)) {
    $installArguments += @("--index-url", $TorchIndexUrl.Trim())
}
Invoke-CheckedPython `
    -Executable $venvPython `
    -Arguments $installArguments `
    -Operation "PyTorch installation"

if ($RegisterUserEnvironment) {
    [System.Environment]::SetEnvironmentVariable(
        "AURA_TRANSFORMER_PYTHON",
        $venvPython,
        [System.EnvironmentVariableTarget]::User)
    $env:AURA_TRANSFORMER_PYTHON = $venvPython
}

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
    Invoke-CheckedPython `
        -Executable $venvPython `
        -Arguments @(
            $teacherScript,
            "--self-test",
            "--backend", $Backend,
            "--epochs", "2",
            "--cpu-threads", "0"
        ) `
        -Operation "Transformer teacher self-test"
}

Write-Host "Aura Transformer teacher is ready: $venvPython"
Write-Host "Backend preference: $Backend"
if ($RegisterUserEnvironment) {
    Write-Host "Registered AURA_TRANSFORMER_PYTHON for the current user."
}
