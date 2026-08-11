[CmdletBinding()]
param(
    [string] $DotNetExecutable = ""
)

$ErrorActionPreference = "Stop"
$requiredFramework = "Microsoft.WindowsDesktop.App"
$requiredMajorVersion = 8
$missingRuntimeExitCode = 10

function Get-AuraDotNetCandidates {
    if (-not [string]::IsNullOrWhiteSpace($DotNetExecutable)) {
        return @([System.IO.Path]::GetFullPath($DotNetExecutable))
    }

    $candidates = [System.Collections.Generic.List[string]]::new()
    foreach ($programFilesRoot in @(
        [System.Environment]::GetEnvironmentVariable("ProgramW6432"),
        [System.Environment]::GetFolderPath(
            [System.Environment+SpecialFolder]::ProgramFiles)
    )) {
        if ([string]::IsNullOrWhiteSpace($programFilesRoot)) {
            continue
        }
        $candidate = Join-Path $programFilesRoot "dotnet\dotnet.exe"
        if (-not $candidates.Contains($candidate)) {
            $candidates.Add($candidate)
        }
    }

    $pathCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $pathCommand `
            -and -not [string]::IsNullOrWhiteSpace($pathCommand.Source) `
            -and -not $candidates.Contains($pathCommand.Source)) {
        $candidates.Add($pathCommand.Source)
    }
    return @($candidates)
}

foreach ($candidate in @(Get-AuraDotNetCandidates)) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        continue
    }

    $runtimeLines = @()
    try {
        $runtimeLines = @(
            & $candidate --list-runtimes --arch x64 2>$null
        )
        if ($LASTEXITCODE -ne 0) {
            continue
        }
    }
    catch {
        continue
    }

    foreach ($line in $runtimeLines) {
        $match = [System.Text.RegularExpressions.Regex]::Match(
            [string]$line,
            '^Microsoft\.WindowsDesktop\.App\s+(?<version>\d+\.\d+\.\d+)\s+\[')
        if (-not $match.Success) {
            continue
        }
        $version = [System.Version]$match.Groups["version"].Value
        if ($version.Major -eq $requiredMajorVersion) {
            Write-Host (
                ".NET Desktop Runtime x64 detected: " +
                $requiredFramework +
                " " +
                $version +
                " (" +
                $candidate +
                ")")
            exit 0
        }
    }
}

Write-Host (
    ".NET 8 Desktop Runtime x64 is required but was not detected. " +
    "Install Microsoft.WindowsDesktop.App 8.x and try again.") `
    -ForegroundColor Yellow
exit $missingRuntimeExitCode
