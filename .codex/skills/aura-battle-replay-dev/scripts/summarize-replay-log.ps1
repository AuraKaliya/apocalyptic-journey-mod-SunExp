[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$LogPath,

    [ValidateRange(1, 200)]
    [int]$MaximumMatchesPerCategory = 40
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
    throw "Replay log does not exist: $LogPath"
}

$resolvedPath = (Resolve-Path -LiteralPath $LogPath).Path
$item = Get-Item -LiteralPath $resolvedPath
$rules = @(
    [pscustomobject]@{ Category = "environment"; Marker = "render-graph-enabled"; Pattern = "RenderGraph is now enabled" },
    [pscustomobject]@{ Category = "recording"; Marker = "baseline-committed"; Pattern = "materialized baseline committed" },
    [pscustomobject]@{ Category = "recording"; Marker = "document-ready"; Pattern = "Replay Document .*ready=True" },
    [pscustomobject]@{ Category = "recording"; Marker = "document-rejected"; Pattern = "Replay Document .*ready=False|recording rejected|finalization rejected" },
    [pscustomobject]@{ Category = "classification"; Marker = "action-source-classified"; Pattern = "implicit action source classified" },
    [pscustomobject]@{ Category = "classification"; Marker = "intent-fallback-resolved"; Pattern = "native intent visual fallback captured" },
    [pscustomobject]@{ Category = "presentation-lifecycle"; Marker = "visual-completed"; Pattern = "card visual observation completed" },
    [pscustomobject]@{ Category = "capture-diagnostic"; Marker = "observer-failed"; Pattern = "replay observer failed|capture diagnostic|capture failure" },
    [pscustomobject]@{ Category = "capture-diagnostic"; Marker = "watchdog-timeout"; Pattern = "watchdog|observation timeout|timeout diagnostic" },
    [pscustomobject]@{ Category = "resource-preflight"; Marker = "required-resource-missing"; Pattern = "Replay required .* missing|resource preflight.*missing|module.*required.*missing" },
    [pscustomobject]@{ Category = "render-host"; Marker = "renderer-registered"; Pattern = "replay URP renderer registered" },
    [pscustomobject]@{ Category = "render-host"; Marker = "renderer-assigned"; Pattern = "replay URP renderer assigned" },
    [pscustomobject]@{ Category = "render-host"; Marker = "host-prepared"; Pattern = "replay render host prepared" },
    [pscustomobject]@{ Category = "render-host"; Marker = "host-preflighted"; Pattern = "replay render host preflighted" },
    [pscustomobject]@{ Category = "render-host"; Marker = "frame-barrier-confirmed"; Pattern = "replay render host frame-barrier-confirmed" },
    [pscustomobject]@{ Category = "render-host"; Marker = "host-active"; Pattern = "replay render host active" },
    [pscustomobject]@{ Category = "render-host"; Marker = "host-disposed"; Pattern = "replay render host disposed" },
    [pscustomobject]@{ Category = "rejection"; Marker = "preparation-rejected"; Pattern = "replay preparation rejected" },
    [pscustomobject]@{ Category = "primary-error"; Marker = "command-exception"; Pattern = "\[Command/Error\].*\[Exception\]" },
    [pscustomobject]@{ Category = "primary-error"; Marker = "render-graph-resource"; Pattern = "RenderGraphResourceRegistry\.GetTextureResourceDesc" },
    [pscustomobject]@{ Category = "primary-error"; Marker = "full-screen-render-graph"; Pattern = "FullScreenPassRendererFeature.*RecordRenderGraph" },
    [pscustomobject]@{ Category = "primary-error"; Marker = "render-graph-execution"; Pattern = "Render Graph Execution error" },
    [pscustomobject]@{ Category = "secondary-error"; Marker = "error-reporter-failed"; Pattern = "Failed to update error selection|Already continuation registered" },
    [pscustomobject]@{ Category = "performance"; Marker = "replay-performance"; Pattern = "\[MatchRecords:perf\]" }
)

$allMatches = [System.Collections.Generic.List[object]]::new()
$categoryCounts = @{}
$seenMarkers = @{}
foreach ($category in $rules.Category | Select-Object -Unique) {
    $categoryCounts[[string]$category] = 0
}

$lineNumber = 0
foreach ($line in [System.IO.File]::ReadLines($resolvedPath)) {
    $lineNumber++
    foreach ($rule in $rules) {
        if ($line -notmatch $rule.Pattern) { continue }
        $seenMarkers[$rule.Marker] = $true
        $categoryName = [string]$rule.Category
        if ([int]$categoryCounts[$categoryName] -lt $MaximumMatchesPerCategory) {
            [void]$allMatches.Add([pscustomobject]@{
                category = $categoryName
                line = $lineNumber
                marker = $rule.Marker
                text = $line
            })
            $categoryCounts[$categoryName] = [int]$categoryCounts[$categoryName] + 1
        }
    }
}

function Has-Marker([string]$Name) {
    return $seenMarkers.ContainsKey($Name)
}

$lastReachedStage = if (Has-Marker "host-active") {
    "active-playback"
}
elseif (Has-Marker "frame-barrier-confirmed") {
    "normal-frame-barrier"
}
elseif (Has-Marker "host-preflighted") {
    "render-host-preflight"
}
elseif (Has-Marker "host-prepared") {
    "render-host-prepared"
}
elseif (Has-Marker "document-ready") {
    "recording-finalized"
}
elseif (Has-Marker "baseline-committed") {
    "recording"
}
else {
    "environment"
}

$failureBoundary = if ((Has-Marker "render-graph-resource") -or
        (Has-Marker "full-screen-render-graph") -or
        (Has-Marker "render-graph-execution")) {
    "playback-render-pipeline"
}
elseif (Has-Marker "required-resource-missing") {
    "playback-resource-or-module-preflight"
}
elseif ((Has-Marker "preparation-rejected") -and (Has-Marker "host-prepared")) {
    "playback-first-frame-preflight"
}
elseif ((Has-Marker "document-rejected") -or
        (Has-Marker "observer-failed") -or
        (Has-Marker "watchdog-timeout")) {
    "recording-or-finalization"
}
else {
    "none-detected"
}

$allPrimaryErrors = @($allMatches | Where-Object category -eq "primary-error")
$firstPrimaryError = if ($allPrimaryErrors.Count -gt 0) {
    $allPrimaryErrors | Sort-Object line | Select-Object -First 1
}
else {
    $null
}

[pscustomobject]@{
    schemaVersion = 1
    logPath = $resolvedPath
    bytes = $item.Length
    lastWriteUtc = $item.LastWriteTimeUtc.ToString("O")
    sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPath).Hash
    lastReachedStage = $lastReachedStage
    failureBoundary = $failureBoundary
    stageEvidence = [pscustomobject][ordered]@{
        baselineCommitted = Has-Marker "baseline-committed"
        documentReady = Has-Marker "document-ready"
        requiredResourceMissing = Has-Marker "required-resource-missing"
        renderHostPrepared = Has-Marker "host-prepared"
        renderHostPreflighted = Has-Marker "host-preflighted"
        frameBarrierConfirmed = Has-Marker "frame-barrier-confirmed"
        playbackActive = Has-Marker "host-active"
        preparationRejected = Has-Marker "preparation-rejected"
        renderGraphFailure = (Has-Marker "render-graph-resource") -or
            (Has-Marker "full-screen-render-graph") -or
            (Has-Marker "render-graph-execution")
        hostDisposed = Has-Marker "host-disposed"
    }
    firstPrimaryError = $firstPrimaryError
    matches = @($allMatches)
} | ConvertTo-Json -Depth 8
