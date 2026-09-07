param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$GameDataDirectory = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolsExp.ReplayUnity.Tests"
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) {
    throw "Unity Editor is missing: $UnityPath"
}
$requiredVersion = ((Get-Content -LiteralPath (Join-Path $project "ProjectSettings/ProjectVersion.txt") |
    Where-Object { $_ -like "m_EditorVersion:*" }) -split ": ", 2)[1]
if ((Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion -notlike "$requiredVersion*") {
    throw "Replay UI tests require Unity $requiredVersion."
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "output/replay-native-ui-tests"
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$mirror = Join-Path $project "Assets/Tests/UnderTest"
[System.IO.Directory]::CreateDirectory($mirror) | Out-Null
$sources = @(
    "AuraSharedCore/AuraSharedResourceCache.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayNativePrefabInstanceV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayCanvasSpaceV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayBoundsProjectionV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/Recording/MatchReplayHandCapture.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Core/ReplayHandLifecycleContractV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayCardInstructionProjectionV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayHandProjectionV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Core/ReplayCanonicalJsonV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Core/ReplayStateReducerV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Core/ReplayFastCloneV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayCombatantProjectionV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Playback/ReplayPresentationValuesV17.cs",
    "AuraToolsExp-Dev/GameApi/ReplayGlobalLightRendererFeatureV17.cs",
    "AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Core/ReplayContractsV17.cs")
$sourceHashes = [ordered]@{}
if (-not [string]::IsNullOrWhiteSpace($GameDataDirectory)) {
    & python (Join-Path $PSScriptRoot "extract_replay_native_hud.py") `
        --game-data $GameDataDirectory --output (Join-Path $project "Assets/Tests/NativeHudFixtures")
    if ($LASTEXITCODE -ne 0) { throw "Native HUD fixture extraction failed." }
}
foreach ($relative in $sources) {
    $source = Join-Path $repoRoot $relative
    $mirroredSource = Join-Path $mirror (Split-Path -Leaf $source)
    Copy-Item -LiteralPath $source -Destination $mirroredSource -Force
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $mirroredSource -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "Replay UI test source mirror differs from production: $relative"
    }
    $sourceHashes[$relative] = $sourceHash
}
$runId = [Guid]::NewGuid().ToString("N")
$resultsPath = Join-Path $OutputDirectory "results-$runId.xml"
$logPath = Join-Path $OutputDirectory "unity-$runId.log"
$arguments = @("-batchmode", "-projectPath", "`"$project`"",
    "-runTests", "-testPlatform", "PlayMode", "-testResults", "`"$resultsPath`"",
    "-logFile", "`"$logPath`"")
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
$process.WaitForExit()
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultsPath)) {
    throw "Replay Unity tests failed (exit=$($process.ExitCode)). See $logPath"
}
[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$run = $results.'test-run'
$minimumPassed = if ([string]::IsNullOrWhiteSpace($GameDataDirectory)) { 22 } else { 23 }
if ($run.result -ne "Passed" -or [int]$run.failed -ne 0 -or [int]$run.passed -lt $minimumPassed) {
    throw "Replay Unity tests did not pass all runtime cases. See $resultsPath"
}
$receipt = [ordered]@{
    sourceHashes = $sourceHashes
    unityVersion = $requiredVersion
    passed = [int]$run.passed
    failed = [int]$run.failed
    nativeGameDataDirectory = $GameDataDirectory
    results = $resultsPath
    log = $logPath
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory "latest.json") -Encoding UTF8
Write-Host "Replay native UI Unity tests passed: $($run.passed); verified $($sourceHashes.Count) source/dependency mirrors."
