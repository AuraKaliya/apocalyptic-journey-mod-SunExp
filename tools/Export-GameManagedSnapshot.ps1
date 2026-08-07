[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$SourcePath = "",
    [string]$SnapshotRoot = "",
    [string]$ReportRoot = "",
    [string]$AppId = "3709430",
    [string]$SteamBuildId = "",
    [string]$UnityVersion = "6000.0.46f1",
    [int]$ExpectedAssemblyCount = 253,
    [string[]]$RequiredAssemblies = @(
        "AllScripts.dll",
        "Witch.dll",
        "Witch.Core.dll",
        "Live2D.Cubism.dll"
    ),
    [switch]$AllowPartial
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $repoRoot "Managed"
}
if ([string]::IsNullOrWhiteSpace($SnapshotRoot)) {
    $SnapshotRoot = Join-Path $repoRoot "开发参考资料\Managed快照"
}
if ([string]::IsNullOrWhiteSpace($ReportRoot)) {
    $ReportRoot = Join-Path $repoRoot ("artifacts\game-reference\" + $Version.TrimStart("v"))
}

$SourcePath = (Resolve-Path -LiteralPath $SourcePath).Path
$SnapshotRoot = [System.IO.Path]::GetFullPath($SnapshotRoot)
$ReportRoot = [System.IO.Path]::GetFullPath($ReportRoot)
$runtimeVersion = "v" + $Version.TrimStart("v")
$versionFolder = $runtimeVersion
$inspectorProject = Join-Path $repoRoot "tools\GameManagedInspector\GameManagedInspector.csproj"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("game-managed-snapshot-" + [guid]::NewGuid().ToString("N"))

function Invoke-InspectorInventory {
    param(
        [Parameter(Mandatory)][string]$InputPath,
        [Parameter(Mandatory)][string]$OutputPath,
        [string]$CsvPath = "",
        [string]$MarkdownPath = ""
    )

    $arguments = @(
        "run", "--project", $inspectorProject, "-c", "Release", "--no-build", "--",
        "inventory", "--input", $InputPath, "--output", $OutputPath,
        "--source-path", $SourcePath,
        "--app-id", $AppId,
        "--steam-build-id", $SteamBuildId,
        "--runtime-version", $runtimeVersion,
        "--unity-version", $UnityVersion,
        "--expected-count", [string]$ExpectedAssemblyCount
    )
    foreach ($required in $RequiredAssemblies) {
        $arguments += @("--required-assembly", $required)
    }
    if (-not [string]::IsNullOrWhiteSpace($CsvPath)) {
        $arguments += @("--csv", $CsvPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($MarkdownPath)) {
        $arguments += @("--markdown", $MarkdownPath)
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "GameManagedInspector inventory failed with exit code $LASTEXITCODE."
    }
}

function Assert-SameAssemblies {
    param(
        [Parameter(Mandatory)]$Expected,
        [Parameter(Mandatory)]$Actual,
        [Parameter(Mandatory)][string]$Context
    )

    $expectedByName = @{}
    foreach ($assembly in $Expected.assemblies) {
        $expectedByName[[string]$assembly.fileName] = $assembly
    }
    $actualByName = @{}
    foreach ($assembly in $Actual.assemblies) {
        $actualByName[[string]$assembly.fileName] = $assembly
    }
    if ($expectedByName.Count -ne $actualByName.Count) {
        throw "$Context assembly count changed: expected=$($expectedByName.Count), actual=$($actualByName.Count)."
    }
    foreach ($name in $expectedByName.Keys) {
        if (-not $actualByName.ContainsKey($name)) {
            throw "$Context is missing $name."
        }
        if ([string]$expectedByName[$name].sha256 -ne [string]$actualByName[$name].sha256 `
            -or [string]$expectedByName[$name].mvid -ne [string]$actualByName[$name].mvid `
            -or [long]$expectedByName[$name].size -ne [long]$actualByName[$name].size) {
            throw "$Context fingerprint changed for $name."
        }
    }
}

New-Item -ItemType Directory -Path $SnapshotRoot -Force | Out-Null
New-Item -ItemType Directory -Path $ReportRoot -Force | Out-Null
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    dotnet build $inspectorProject -c Release --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "GameManagedInspector build failed."
    }

    $beforePath = Join-Path $tempRoot "source-before.json"
    Invoke-InspectorInventory -InputPath $SourcePath -OutputPath $beforePath
    $before = Get-Content -LiteralPath $beforePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($before.metadataFailures).Count -gt 0) {
        throw "Managed metadata could not be read for: $(@($before.metadataFailures) -join ', ')."
    }
    if (-not [bool]$before.complete -and -not $AllowPartial) {
        $missing = [string]::Join(", ", @($before.missingAssemblies))
        throw "Managed input is incomplete. assemblies=$($before.assemblyCount)/$ExpectedAssemblyCount; missing=$missing. Use -AllowPartial only for a deliberately provisional snapshot."
    }

    $suffix = if ([bool]$before.complete) { "" } else { ".partial" }
    $finalPath = Join-Path $SnapshotRoot ($versionFolder + $suffix)
    if (Test-Path -LiteralPath $finalPath) {
        throw "Snapshot destination already exists and will not be overwritten: $finalPath"
    }
    $stagingPath = Join-Path $SnapshotRoot ($versionFolder + $suffix + ".staging-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $stagingPath | Out-Null

    foreach ($dll in Get-ChildItem -LiteralPath $SourcePath -Filter "*.dll" -File | Sort-Object Name) {
        Copy-Item -LiteralPath $dll.FullName -Destination (Join-Path $stagingPath $dll.Name)
    }

    $copiedPath = Join-Path $tempRoot "copied.json"
    Invoke-InspectorInventory -InputPath $stagingPath -OutputPath $copiedPath
    $copied = Get-Content -LiteralPath $copiedPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-SameAssemblies -Expected $before -Actual $copied -Context "Copied snapshot"

    $afterPath = Join-Path $tempRoot "source-after.json"
    Invoke-InspectorInventory -InputPath $SourcePath -OutputPath $afterPath
    $after = Get-Content -LiteralPath $afterPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-SameAssemblies -Expected $before -Actual $after -Context "Managed source during copy"

    Move-Item -LiteralPath $stagingPath -Destination $finalPath

    $manifestPath = Join-Path $finalPath "managed.manifest.json"
    $csvPath = Join-Path $ReportRoot "managed-assemblies.csv"
    $markdownPath = Join-Path $ReportRoot "managed-assemblies.md"
    Invoke-InspectorInventory -InputPath $finalPath -OutputPath $manifestPath -CsvPath $csvPath -MarkdownPath $markdownPath
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $ReportRoot "managed.manifest.json") -Force

    Write-Host "Managed snapshot created: $finalPath"
    Write-Host "Assembly report: $markdownPath"
    Write-Output $finalPath
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
