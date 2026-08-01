param(
    [Parameter(Mandatory = $true)]
    [string]$ArchiveRoot,

    [switch]$Apply,

    [ValidateRange(1, 100000)]
    [int]$MaximumExpertCases = 2048,

    [ValidateRange(1, 1000000)]
    [int]$MaximumObservations = 8192
)

$ErrorActionPreference = "Stop"
$resolvedRoot = [IO.Path]::GetFullPath($ArchiveRoot)
$storageRoot = [IO.Path]::GetFullPath((Join-Path $resolvedRoot "v4"))
if (-not (Test-Path -LiteralPath $storageRoot -PathType Container)) {
    throw "Foundation archive v4 directory is missing: $storageRoot"
}
if (-not $storageRoot.StartsWith(
        $resolvedRoot.TrimEnd('\') + '\',
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved storage path escaped the requested archive root."
}

function Test-ArchiveJson([string]$Path) {
    return $Path.EndsWith(".json", [StringComparison]::OrdinalIgnoreCase) `
        -or $Path.EndsWith(
            ".json.gz",
            [StringComparison]::OrdinalIgnoreCase)
}

function Read-ArchiveText([string]$Path) {
    if (-not $Path.EndsWith(
            ".json.gz",
            [StringComparison]::OrdinalIgnoreCase)) {
        return [IO.File]::ReadAllText($Path)
    }
    $input = [IO.File]::OpenRead($Path)
    try {
        $gzip = [IO.Compression.GZipStream]::new(
            $input,
            [IO.Compression.CompressionMode]::Decompress)
        try {
            $reader = [IO.StreamReader]::new($gzip, [Text.Encoding]::UTF8)
            try { return $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        finally { $gzip.Dispose() }
    }
    finally { $input.Dispose() }
}

function Write-AtomicText([string]$Path, [string]$Contents) {
    $temporary = $Path + ".tmp-" + [Guid]::NewGuid().ToString("N")
    [IO.File]::WriteAllText(
        $temporary,
        $Contents,
        [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $backup = $Path + ".bak-" + [Guid]::NewGuid().ToString("N")
        [IO.File]::Replace($temporary, $Path, $backup)
        [IO.File]::Delete($backup)
    }
    else {
        [IO.File]::Move($temporary, $Path)
    }
}

function Compress-Atomic([string]$SourcePath) {
    if ($SourcePath.EndsWith(
            ".json.gz",
            [StringComparison]::OrdinalIgnoreCase)) {
        return $SourcePath
    }
    $target = $SourcePath + ".gz"
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        if ((Get-Item -LiteralPath $target).Length -le 0) {
            throw "Existing compressed target is empty: $target"
        }
        return $target
    }
    $temporary = $target + ".tmp-" + [Guid]::NewGuid().ToString("N")
    $input = [IO.File]::OpenRead($SourcePath)
    try {
        $output = [IO.File]::Create($temporary)
        try {
            $gzip = [IO.Compression.GZipStream]::new(
                $output,
                [IO.Compression.CompressionLevel]::Fastest,
                $true)
            try { $input.CopyTo($gzip, 1MB) }
            finally { $gzip.Dispose() }
        }
        finally { $output.Dispose() }
    }
    finally { $input.Dispose() }
    [IO.File]::Move($temporary, $target)
    return $target
}

$summary = [ordered]@{
    ProtocolVersion = "foundation-archive-maintenance-v1"
    GeneratedUtc = [DateTime]::UtcNow.ToString("o")
    ArchiveRoot = $resolvedRoot
    Apply = [bool]$Apply
    MaximumExpertCases = $MaximumExpertCases
    MaximumObservations = $MaximumObservations
    CompatibilityDirectories = 0
    ExpertReferencesKept = 0
    ExpertReferencesRemoved = 0
    CanonicalCasesKept = 0
    UnreferencedCanonicalCasesRemoved = 0
    ObservationsKept = 0
    ObservationsRemoved = 0
    FilesCompressed = 0
    PlannedRemovalBytes = 0L
    CompressionCandidateBytes = 0L
    BytesBefore = 0L
    BytesAfter = 0L
}

$compatibilityDirectories = @(Get-ChildItem -LiteralPath $storageRoot `
    -Directory -Force)
$summary.CompatibilityDirectories = $compatibilityDirectories.Count
foreach ($compatibilityDirectory in $compatibilityDirectories) {
    $compatibilityPath = [IO.Path]::GetFullPath(
        $compatibilityDirectory.FullName)
    if (-not $compatibilityPath.StartsWith(
            $storageRoot.TrimEnd('\') + '\',
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Compatibility directory escaped the v4 storage root."
    }
    $expertDirectory = Join-Path $compatibilityPath "e"
    $caseDirectory = Join-Path $compatibilityPath "c"
    $observationDirectory = Join-Path $compatibilityPath "o"
    $expertFiles = if (Test-Path -LiteralPath $expertDirectory) {
        @(Get-ChildItem -LiteralPath $expertDirectory -File -Force |
            Where-Object { Test-ArchiveJson $_.FullName } |
            Sort-Object LastWriteTimeUtc -Descending)
    } else { @() }
    $keptExpertFiles = @($expertFiles | Select-Object `
        -First $MaximumExpertCases)
    $removedExpertFiles = @($expertFiles | Select-Object `
        -Skip $MaximumExpertCases)
    $summary.ExpertReferencesKept += $keptExpertFiles.Count
    $summary.ExpertReferencesRemoved += $removedExpertFiles.Count
    $summary.PlannedRemovalBytes += [long](($removedExpertFiles |
        Measure-Object Length -Sum).Sum)

    $references = [Collections.Generic.List[object]]::new()
    foreach ($expertFile in $keptExpertFiles) {
        $reference = Read-ArchiveText $expertFile.FullName |
            ConvertFrom-Json
        $canonicalName = [string]$reference.CanonicalFileName
        if ([IO.Path]::GetFileName($canonicalName) -ne $canonicalName) {
            throw "Unsafe canonical filename in $($expertFile.FullName)"
        }
        $canonicalPath = Join-Path $caseDirectory $canonicalName
        if (-not (Test-Path -LiteralPath $canonicalPath -PathType Leaf)) {
            throw "Referenced canonical case is missing: $canonicalPath"
        }
        $references.Add([pscustomobject]@{
            ExpertPath = $expertFile.FullName
            Reference = $reference
            CanonicalPath = [IO.Path]::GetFullPath($canonicalPath)
        })
    }

    $canonicalFiles = if (Test-Path -LiteralPath $caseDirectory) {
        @(Get-ChildItem -LiteralPath $caseDirectory -File -Force |
            Where-Object { Test-ArchiveJson $_.FullName })
    } else { @() }
    $keptCanonicalPaths = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    foreach ($referenceEntry in $references) {
        [void]$keptCanonicalPaths.Add($referenceEntry.CanonicalPath)
    }
    $unreferencedCanonicalFiles = @($canonicalFiles | Where-Object {
        -not $keptCanonicalPaths.Contains(
            [IO.Path]::GetFullPath($_.FullName))
    })
    $summary.CanonicalCasesKept += $keptCanonicalPaths.Count
    $summary.UnreferencedCanonicalCasesRemoved +=
        $unreferencedCanonicalFiles.Count
    $summary.PlannedRemovalBytes += [long](($unreferencedCanonicalFiles |
        Measure-Object Length -Sum).Sum)
    $summary.CompressionCandidateBytes += [long](($canonicalFiles |
        Where-Object {
            $keptCanonicalPaths.Contains([IO.Path]::GetFullPath($_.FullName)) `
                -and $_.Name.EndsWith(".json")
        } | Measure-Object Length -Sum).Sum)

    $observationFiles = if (Test-Path -LiteralPath $observationDirectory) {
        @(Get-ChildItem -LiteralPath $observationDirectory -File -Force |
            Where-Object { Test-ArchiveJson $_.FullName } |
            Sort-Object LastWriteTimeUtc -Descending)
    } else { @() }
    $keptObservationFiles = @($observationFiles | Select-Object `
        -First $MaximumObservations)
    $removedObservationFiles = @($observationFiles | Select-Object `
        -Skip $MaximumObservations)
    $summary.ObservationsKept += $keptObservationFiles.Count
    $summary.ObservationsRemoved += $removedObservationFiles.Count
    $summary.PlannedRemovalBytes += [long](($removedObservationFiles |
        Measure-Object Length -Sum).Sum)
    $summary.CompressionCandidateBytes += [long](($keptObservationFiles |
        Where-Object { $_.Name.EndsWith(".json") } |
        Measure-Object Length -Sum).Sum)

    $allFilesBefore = @(@($expertFiles) + @($canonicalFiles) `
        + @($observationFiles) |
        Sort-Object FullName -Unique)
    $summary.BytesBefore += [long](($allFilesBefore |
        Measure-Object Length -Sum).Sum)

    if ($Apply) {
        foreach ($canonicalGroup in @($references |
                Group-Object CanonicalPath)) {
            $sourcePath = [string]$canonicalGroup.Name
            $compressedPath = Compress-Atomic $sourcePath
            if ($compressedPath -ne $sourcePath) {
                foreach ($referenceEntry in $canonicalGroup.Group) {
                    $referenceEntry.Reference.CanonicalFileName =
                        [IO.Path]::GetFileName($compressedPath)
                    Write-AtomicText $referenceEntry.ExpertPath `
                        ($referenceEntry.Reference | ConvertTo-Json -Depth 8)
                }
                Remove-Item -LiteralPath $sourcePath -Force
                $summary.FilesCompressed++
            }
        }
        foreach ($observationFile in $keptObservationFiles) {
            $compressedPath = Compress-Atomic $observationFile.FullName
            if ($compressedPath -ne $observationFile.FullName) {
                Remove-Item -LiteralPath $observationFile.FullName -Force
                $summary.FilesCompressed++
            }
        }
        foreach ($file in @(
                @($removedExpertFiles) +
                @($unreferencedCanonicalFiles) +
                @($removedObservationFiles))) {
            if (Test-Path -LiteralPath $file.FullName -PathType Leaf) {
                Remove-Item -LiteralPath $file.FullName -Force
            }
        }
    }
}

$summary.BytesAfter = if ($Apply) {
    [long]((Get-ChildItem -LiteralPath $storageRoot -File -Recurse -Force |
        Measure-Object Length -Sum).Sum)
} else {
    $summary.BytesBefore
}
$summaryPath = Join-Path $resolvedRoot `
    "foundation-archive-maintenance-v1.json"
[IO.File]::WriteAllText(
    $summaryPath,
    ($summary | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))

Write-Host $(if ($Apply) { "Archive maintenance applied." } else {
    "Archive maintenance dry run complete; no files changed."
})
Write-Host "Report: $summaryPath"
Write-Host "Unreferenced canonical cases: $($summary.UnreferencedCanonicalCasesRemoved)"
Write-Host "Excess observations: $($summary.ObservationsRemoved)"
Write-Host "Planned removal bytes: $($summary.PlannedRemovalBytes)"
