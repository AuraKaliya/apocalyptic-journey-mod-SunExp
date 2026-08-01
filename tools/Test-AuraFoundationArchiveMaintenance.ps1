$ErrorActionPreference = "Stop"
$root = Join-Path ([IO.Path]::GetTempPath()) `
    ("aura-foundation-archive-test-" + [Guid]::NewGuid().ToString("N"))
$compatibility = Join-Path $root "v4\compatibility-key"
$expertDirectory = Join-Path $compatibility "e"
$caseDirectory = Join-Path $compatibility "c"
$observationDirectory = Join-Path $compatibility "o"

try {
    New-Item -ItemType Directory -Force -Path `
        $expertDirectory, $caseDirectory, $observationDirectory | Out-Null
    $canonicalName = "case-kept.json"
    $canonicalPath = Join-Path $caseDirectory $canonicalName
    [IO.File]::WriteAllText(
        $canonicalPath,
        ('{"Episodes":[{"Frames":[] }],"Padding":"' + ('x' * 4096) + '"}'))
    [IO.File]::WriteAllText(
        (Join-Path $caseDirectory "case-unreferenced.json"),
        ('{"Episodes":[],"Padding":"' + ('y' * 4096) + '"}'))
    @{
        ProtocolVersion = "success-case-archive-worker-v4"
        StorageVersion = 4
        CompatibilityKey = "compatibility-key"
        CaseId = "case-kept"
        CanonicalFileName = $canonicalName
    } | ConvertTo-Json | ForEach-Object {
        [IO.File]::WriteAllText(
            (Join-Path $expertDirectory "case-kept.json"),
            $_,
            [Text.UTF8Encoding]::new($false))
    }
    1..3 | ForEach-Object {
        $observationJson = @{ CaseId = "observation-$_" } |
            ConvertTo-Json
        [IO.File]::WriteAllText(
            (Join-Path $observationDirectory "observation-$_.json"),
            $observationJson,
            [Text.UTF8Encoding]::new($false))
        Start-Sleep -Milliseconds 10
    }

    $optimizer = Join-Path $PSScriptRoot `
        "Optimize-AuraFoundationCaseArchive.ps1"
    & $optimizer -ArchiveRoot $root -MaximumExpertCases 1 `
        -MaximumObservations 2
    if (-not $?) {
        throw "Archive maintenance dry run failed."
    }
    if (-not (Test-Path -LiteralPath `
            (Join-Path $caseDirectory "case-unreferenced.json"))) {
        throw "Dry run changed an archive entry."
    }
    $dryRun = Get-Content -LiteralPath `
        (Join-Path $root "foundation-archive-maintenance-v1.json") -Raw |
        ConvertFrom-Json
    if ([int]$dryRun.UnreferencedCanonicalCasesRemoved -ne 1 `
        -or [int]$dryRun.ObservationsRemoved -ne 1 `
        -or [bool]$dryRun.Apply) {
        throw "Archive maintenance dry-run accounting is invalid."
    }

    & $optimizer -ArchiveRoot $root -MaximumExpertCases 1 `
        -MaximumObservations 2 -Apply
    if (-not $?) {
        throw "Archive maintenance apply failed."
    }
    $canonicalGzip = $canonicalPath + ".gz"
    if (-not (Test-Path -LiteralPath $canonicalGzip -PathType Leaf) `
        -or (Test-Path -LiteralPath $canonicalPath) `
        -or (Test-Path -LiteralPath `
            (Join-Path $caseDirectory "case-unreferenced.json"))) {
        throw "Canonical archive compaction did not converge."
    }
    $observationFiles = @(Get-ChildItem -LiteralPath $observationDirectory `
        -File)
    if ($observationFiles.Count -ne 2 `
        -or @($observationFiles | Where-Object {
            -not $_.Name.EndsWith(".json.gz")
        }).Count -ne 0) {
        throw "Observation archive compaction did not enforce its cap."
    }
    $reference = Get-Content -LiteralPath `
        (Join-Path $expertDirectory "case-kept.json") -Raw |
        ConvertFrom-Json
    if ([string]$reference.CanonicalFileName -ne "case-kept.json.gz") {
        throw "Expert reference was not redirected to compressed content."
    }
    Write-Host "Aura foundation archive maintenance test passed."
}
finally {
    $resolved = [IO.Path]::GetFullPath($root)
    $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolved.StartsWith(
            $temporaryRoot,
            [StringComparison]::OrdinalIgnoreCase) `
        -and (Test-Path -LiteralPath $resolved)) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
