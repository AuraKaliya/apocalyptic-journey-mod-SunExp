[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SnapshotPath,
    [Parameter(Mandatory)]
    [string]$Version,
    [Parameter(Mandatory)]
    [string]$IlSpyCmdPath,
    [string]$OutputRoot = "",
    [string]$ReportRoot = "",
    [string]$ExpectedIlSpyVersion = "9.1.0.7988"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if (Test-Path -LiteralPath variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$SnapshotPath = (Resolve-Path -LiteralPath $SnapshotPath).Path
$IlSpyCmdPath = (Resolve-Path -LiteralPath $IlSpyCmdPath).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "开发参考资料"
}
if ([string]::IsNullOrWhiteSpace($ReportRoot)) {
    $ReportRoot = Join-Path $repoRoot ("artifacts\game-reference\" + $Version.TrimStart("v"))
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$ReportRoot = [System.IO.Path]::GetFullPath($ReportRoot)
$runtimeVersion = "v" + $Version.TrimStart("v")
$snapshotManifestPath = Join-Path $SnapshotPath "managed.manifest.json"
if (-not (Test-Path -LiteralPath $snapshotManifestPath -PathType Leaf)) {
    throw "Snapshot manifest is missing: $snapshotManifestPath"
}

$snapshotManifest = Get-Content -LiteralPath $snapshotManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$isCompleteInput = [bool]$snapshotManifest.complete
$suffix = if ($isCompleteInput) { "" } else { ".partial" }
$finalPath = Join-Path $OutputRoot ("反编译文件夹" + $runtimeVersion + $suffix)
if (Test-Path -LiteralPath $finalPath) {
    throw "Decompile destination already exists and will not be overwritten: $finalPath"
}

$versionOutput = (& $IlSpyCmdPath --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch [regex]::Escape($ExpectedIlSpyVersion)) {
    throw "Expected ilspycmd $ExpectedIlSpyVersion but received: $versionOutput"
}
$versionDisplay = (($versionOutput -split "\r?\n") -join "; ")
$toolHash = (Get-FileHash -LiteralPath $IlSpyCmdPath -Algorithm SHA256).Hash
$stagingPath = Join-Path $OutputRoot ("反编译文件夹" + $runtimeVersion + $suffix + ".staging-" + [guid]::NewGuid().ToString("N"))
$logsPath = Join-Path $stagingPath "_logs"
New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
New-Item -ItemType Directory -Path $ReportRoot -Force | Out-Null

$dlls = @(Get-ChildItem -LiteralPath $SnapshotPath -Filter "*.dll" -File | Sort-Object Name)
if ($dlls.Count -ne [int]$snapshotManifest.assemblyCount) {
    throw "Snapshot DLL count no longer matches its manifest: files=$($dlls.Count), manifest=$($snapshotManifest.assemblyCount)."
}
$manifestByName = @{}
foreach ($assembly in $snapshotManifest.assemblies) {
    $manifestByName[[string]$assembly.fileName] = $assembly
}
foreach ($dll in $dlls) {
    $hash = (Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256).Hash
    if (-not $manifestByName.ContainsKey($dll.Name) -or $hash -ne [string]$manifestByName[$dll.Name].sha256) {
        throw "Snapshot fingerprint mismatch: $($dll.Name)"
    }
}

$results = [System.Collections.Generic.List[object]]::new()
$seenDirectories = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$index = 0
foreach ($dll in $dlls) {
    $index++
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($dll.Name)
    if (-not $seenDirectories.Add($assemblyName)) {
        throw "Case-insensitive assembly output collision: $assemblyName"
    }
    $assemblyOutput = Join-Path $stagingPath $assemblyName
    $stdoutPath = Join-Path $logsPath ($assemblyName + ".stdout.log")
    $stderrPath = Join-Path $logsPath ($assemblyName + ".stderr.log")
    New-Item -ItemType Directory -Path $assemblyOutput -Force | Out-Null
    $cliArguments = @(
        "--disable-updatecheck",
        "--project",
        "--nested-directories",
        "--referencepath", $SnapshotPath,
        "--outputdir", $assemblyOutput,
        $dll.FullName
    )
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    & $IlSpyCmdPath @cliArguments 1> $stdoutPath 2> $stderrPath
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    $outputFileCount = @(Get-ChildItem -LiteralPath $assemblyOutput -Recurse -File -ErrorAction SilentlyContinue).Count
    $projectCount = @(Get-ChildItem -LiteralPath $assemblyOutput -Filter "*.csproj" -File -ErrorAction SilentlyContinue).Count
    $succeeded = $exitCode -eq 0 -and $projectCount -ge 1
    $results.Add([pscustomobject]@{
        assembly = $dll.Name
        outputDirectory = $assemblyName
        exitCode = $exitCode
        elapsedMilliseconds = $timer.ElapsedMilliseconds
        outputFileCount = $outputFileCount
        projectCount = $projectCount
        succeeded = $succeeded
        stdoutLog = "_logs/$assemblyName.stdout.log"
        stderrLog = "_logs/$assemblyName.stderr.log"
    })
    Write-Host ("[{0}/{1}] {2}: exit={3}, files={4}, elapsed={5:n1}s" -f $index, $dlls.Count, $dll.Name, $exitCode, $outputFileCount, $timer.Elapsed.TotalSeconds)
}

$failures = @($results | Where-Object { -not $_.succeeded })
$decompileManifest = [ordered]@{
    schemaVersion = 1
    runtimeVersion = $runtimeVersion
    sourceSnapshot = $SnapshotPath
    sourceManifestSha256 = (Get-FileHash -LiteralPath $snapshotManifestPath -Algorithm SHA256).Hash
    inputAssemblyCount = $dlls.Count
    completeGameInput = $isCompleteInput
    expectedGameAssemblyCount = $snapshotManifest.expectedAssemblyCount
    missingGameAssemblies = @($snapshotManifest.missingAssemblies)
    ilSpyCmd = [ordered]@{
        version = $ExpectedIlSpyVersion
        reportedVersion = $versionOutput
        executableSha256 = $toolHash
        arguments = @("--disable-updatecheck", "--project", "--nested-directories", "--referencepath <snapshot>", "--outputdir <assembly-output>", "<assembly.dll>")
    }
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    succeededCount = @($results | Where-Object succeeded).Count
    failedCount = $failures.Count
    results = $results
}
$manifestJson = $decompileManifest | ConvertTo-Json -Depth 8
$manifestPath = Join-Path $stagingPath "decompile.manifest.json"
[System.IO.File]::WriteAllText($manifestPath, $manifestJson.Replace("`r`n", "`n") + "`n", [System.Text.UTF8Encoding]::new($false))

$summaryLines = [System.Collections.Generic.List[string]]::new()
$summaryLines.Add("# Game Managed Decompile Report")
$summaryLines.Add("")
$summaryLines.Add("- Runtime version: ``$runtimeVersion``")
$summaryLines.Add("- Input assemblies: $($dlls.Count)")
$summaryLines.Add("- Complete game input: ``$($isCompleteInput.ToString().ToLowerInvariant())``")
$summaryLines.Add("- Successful projects: $(@($results | Where-Object succeeded).Count)")
$summaryLines.Add("- Failed projects: $($failures.Count)")
if (@($snapshotManifest.missingAssemblies).Count -gt 0) {
    $summaryLines.Add("- Missing game assemblies: " + ((@($snapshotManifest.missingAssemblies) | ForEach-Object { "``$_``" }) -join ", "))
}
$summaryLines.Add("- ilspycmd: ``$versionDisplay``")
$summaryLines.Add("")
$summaryLines.Add("| Assembly | Exit | Files | Projects | Elapsed (ms) | Status |")
$summaryLines.Add("|---|---:|---:|---:|---:|---|")
foreach ($result in $results) {
    $status = if ($result.succeeded) { "Success" } else { "Failed" }
    $summaryLines.Add("| ``$($result.assembly)`` | $($result.exitCode) | $($result.outputFileCount) | $($result.projectCount) | $($result.elapsedMilliseconds) | $status |")
}
$summaryPath = Join-Path $stagingPath "decompile-report.md"
[System.IO.File]::WriteAllLines($summaryPath, $summaryLines, [System.Text.UTF8Encoding]::new($false))

if ($failures.Count -gt 0) {
    Write-Warning "Decompilation completed with $($failures.Count) failures. Staging output retained at $stagingPath"
    throw "Decompile integrity gate failed."
}

$assemblyDirectories = @(Get-ChildItem -LiteralPath $stagingPath -Directory | Where-Object Name -ne "_logs")
if ($assemblyDirectories.Count -ne $dlls.Count) {
    throw "Decompile output directory count mismatch: expected=$($dlls.Count), actual=$($assemblyDirectories.Count)."
}

Move-Item -LiteralPath $stagingPath -Destination $finalPath
Copy-Item -LiteralPath (Join-Path $finalPath "decompile.manifest.json") -Destination (Join-Path $ReportRoot "decompile.manifest.json") -Force
Copy-Item -LiteralPath (Join-Path $finalPath "decompile-report.md") -Destination (Join-Path $ReportRoot "decompile-report.md") -Force
Write-Host "Decompile output created: $finalPath"
Write-Output $finalPath
