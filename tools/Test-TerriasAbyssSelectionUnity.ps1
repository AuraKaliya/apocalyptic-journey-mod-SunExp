param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$GameDataDirectory = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Terrias.AbyssSelectionUnity.Tests'
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity Editor missing: $UnityPath" }
if ((Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion -notlike '6000.0.46f1*') { throw 'Abyss selection acceptance requires Unity 6000.0.46f1.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot 'output/abyss-selection-unity' }
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$mirror = Join-Path $project 'Assets/Tests/UnderTest'
$fixtures = Join-Path $project 'Assets/Fixtures'
[IO.Directory]::CreateDirectory($mirror) | Out-Null
[IO.Directory]::CreateDirectory($fixtures) | Out-Null
$sources = @(
    'Terrias-Dev/Hooks/Ui/SilhouetteGlowRasterizer.cs',
    'Terrias-Dev/Hooks/Ui/UiSilhouetteSource.cs',
    'Terrias-Dev/Hooks/Ui/EndlessAbyssSelectionGlow.cs',
    'Terrias-Dev/Hooks/Ui/TerriasUiSprites.cs',
    'Terrias-Dev/Hooks/Ui/TerriasUiBuilder.cs'
)
$hashes = [ordered]@{}
foreach ($relative in $sources) {
    $source = Join-Path $repoRoot $relative
    $destination = Join-Path $mirror (Split-Path -Leaf $source)
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $hashes[$relative] = (Get-FileHash -LiteralPath $source).Hash
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $hashes[$relative]) { throw "Mirror mismatch: $relative" }
}
foreach ($name in @('深渊震荡卡片.png','里程碑卡片.png')) {
    $relative = "Terrias/ModResource/Images/UI/无尽之渊UI/$name"
    Copy-Item -LiteralPath (Join-Path $repoRoot $relative) -Destination (Join-Path $fixtures $name) -Force
    $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relative)).Hash
}
if (-not [string]::IsNullOrWhiteSpace($GameDataDirectory)) {
    & python -X utf8 (Join-Path $PSScriptRoot 'Extract-AbyssCardFrameFixture.py') --game-data $GameDataDirectory --output $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Native card-frame fixture extraction failed.' }
}
$nativeMetadataPath = Join-Path $fixtures 'native-card-frame.json'
if (-not (Test-Path -LiteralPath $nativeMetadataPath)) { throw 'Native card-frame fixture missing. Supply -GameDataDirectory for the installed game.' }
$nativeMetadata = Get-Content -LiteralPath $nativeMetadataPath -Raw -Encoding utf8 | ConvertFrom-Json
if ($nativeMetadata.witchSha256 -ne (Get-FileHash -LiteralPath (Join-Path $repoRoot 'Managed/Witch.dll')).Hash) {
    throw 'Native card-frame fixture does not match the repository Managed contract.'
}
$fixtureHashes = [ordered]@{}
foreach ($name in @('native-card-frame.png','native-card-frame.json')) {
    $fixtureHashes[$name] = (Get-FileHash -LiteralPath (Join-Path $fixtures $name)).Hash
}
$runId = [Guid]::NewGuid().ToString('N')
$resultsPath = Join-Path $OutputDirectory "results-$runId.xml"
$logPath = Join-Path $OutputDirectory "unity-$runId.log"
$arguments = @('-batchmode','-projectPath',('"' + $project + '"'),'-runTests','-testPlatform','PlayMode',
    '-testFilter','AbyssSelectionTests','-testResults',('"' + $resultsPath + '"'),'-screen-width','1440','-screen-height','900',
    '-logFile',('"' + $logPath + '"'))
Write-Output "Unity abyss selection log: $logPath"
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
while (-not $process.HasExited) { Start-Sleep -Milliseconds 500; $process.Refresh() }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultsPath)) { throw "Abyss selection Unity acceptance failed; see $logPath" }
[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$run = $results.'test-run'
if ($run.result -ne 'Passed' -or [int]$run.failed -ne 0 -or [int]$run.passed -lt 4) { throw "Abyss selection rendering/lifecycle checks failed; see $resultsPath" }
foreach ($relative in $hashes.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $relative)).Hash -ne $hashes[$relative]) { throw "Acceptance input changed: $relative" }
}
foreach ($name in $fixtureHashes.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $fixtures $name)).Hash -ne $fixtureHashes[$name]) { throw "Native fixture changed during acceptance: $name" }
}
[ordered]@{ sourceHashes=$hashes; nativeFixtureHashes=$fixtureHashes; nativeAssetsSha256=$nativeMetadata.sourceSha256; unityVersion='6000.0.46f1'; passed=[int]$run.passed; failed=[int]$run.failed; results=$resultsPath; log=$logPath } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'latest.json') -Encoding utf8
Write-Output "Abyss selection Unity acceptance passed: $($run.passed) cases."
