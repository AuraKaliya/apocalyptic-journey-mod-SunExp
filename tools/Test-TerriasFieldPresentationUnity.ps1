param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$GameDataDirectory = '',
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'Terrias.FieldPresentationUnity.Tests'
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity Editor missing: $UnityPath" }
if ((Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion -notlike '6000.0.46f1*') { throw 'Field acceptance requires Unity 6000.0.46f1.' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot 'output/field-presentation-unity' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$mirror = Join-Path $project 'Assets/Tests/UnderTest'
$fixtures = Join-Path $project 'Assets/Fixtures'
[IO.Directory]::CreateDirectory($mirror) | Out-Null
[IO.Directory]::CreateDirectory($fixtures) | Out-Null
$sources = @(
    'Terrias-Dev/Infrastructure/TerriasFieldId.cs',
    'Terrias-Dev/Infrastructure/FieldPresentationSignals.cs',
    'Terrias-Dev/Mechanics/FieldPresentationState.cs',
    'Terrias-Dev/Mechanics/FieldVisualSpec.cs',
    'Terrias-Dev/GameApi/FieldPresentationScene.cs',
    'Terrias-Dev/Hooks/FieldPresentationRuntime.cs',
    'Terrias-Dev/Hooks/Visual/FieldVisualMesh.cs',
    'Terrias-Dev/Hooks/Visual/FieldPresentationView.cs'
)
$hashes = [ordered]@{}
foreach ($relative in $sources) {
    $source = Join-Path $repoRoot $relative
    $destination = Join-Path $mirror (Split-Path -Leaf $source)
    Copy-Item -LiteralPath $source -Destination $destination -Force
    $hashes[$relative] = (Get-FileHash -LiteralPath $source).Hash
    if ((Get-FileHash -LiteralPath $destination).Hash -ne $hashes[$relative]) { throw "Mirror mismatch: $relative" }
}
foreach ($field in @('moon_domain','scorching_canopy','samsara_garden')) {
    $source = Join-Path $repoRoot "Terrias/ModResource/Images/Field/$field.png"
    Copy-Item -LiteralPath $source -Destination (Join-Path $fixtures "$field.png") -Force
}
if (-not [string]::IsNullOrWhiteSpace($GameDataDirectory)) {
    & python (Join-Path $PSScriptRoot 'Extract-FieldPresentationFixtures.py') --game-data $GameDataDirectory --output $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Native field fixture extraction failed.' }
}
$runId = [Guid]::NewGuid().ToString('N')
$resultsPath = Join-Path $OutputDirectory "results-$runId.xml"
$logPath = Join-Path $OutputDirectory "unity-$runId.log"
$arguments = @('-batchmode', '-projectPath', ('"' + $project + '"'), '-runTests', '-testPlatform', 'PlayMode',
    '-testFilter', 'FieldPresentationTests', '-testResults', ('"' + $resultsPath + '"'),
    '-screen-width', '1280', '-screen-height', '720', '-logFile', ('"' + $logPath + '"'))
Write-Output "Unity field acceptance log: $logPath"
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
while (-not $process.HasExited) { Start-Sleep -Milliseconds 500; $process.Refresh() }
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultsPath)) {
    throw "Field Unity acceptance failed (exit=$($process.ExitCode)); see $logPath"
}
[xml]$results = Get-Content -LiteralPath $resultsPath -Raw
$run = $results.'test-run'
if ($run.result -ne 'Passed' -or [int]$run.failed -ne 0 -or [int]$run.passed -lt 4) {
    throw "Field Unity acceptance did not pass all 4 rendering/lifecycle cases; see $resultsPath"
}
foreach ($relative in $sources) {
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $relative)).Hash -ne $hashes[$relative]) {
        throw "Production source changed during acceptance: $relative"
    }
}
[ordered]@{ sourceHashes=$hashes; unityVersion='6000.0.46f1'; passed=[int]$run.passed;
    failed=[int]$run.failed; results=$resultsPath; log=$logPath; nativeGameDataDirectory=$GameDataDirectory } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'latest.json') -Encoding utf8
Write-Output "Field Unity acceptance passed: $($run.passed) cases; production sources verified."
