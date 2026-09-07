param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
Import-Module (Join-Path $repoRoot "tools\modules\AuraReleaseInputs.psm1") -Force

$windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
if (-not (Test-Path -LiteralPath $windowsPowerShell -PathType Leaf)) {
    throw "Windows PowerShell 5.1 is required for the publish compatibility fixture."
}

$sourceRoot = $repoRoot
$consumers = @(Get-SharedConsumers -RepoRoot $sourceRoot -Classification product -DefaultOnly)
# Exercise the real publisher with distinct old/new bytes without touching shipped packages.
$repoRoot = Join-Path $sourceRoot ('output/publish-contract-tests/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory((Join-Path $repoRoot 'tools/modules')) | Out-Null
foreach ($relative in @('tools/Publish-MainSharedConsumers.ps1','tools/modules/SharedConsumerManifest.psm1','tools/modules/RepositoryPath.psm1','tools/modules/AuraReleaseInputs.psm1')) {
    Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination (Join-Path $repoRoot $relative)
}
[ordered]@{schemaVersion=1;consumers=$consumers} | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $repoRoot 'tools/shared-consumers.json') -Encoding UTF8
foreach ($consumer in $consumers) {
    $projects = @([string]$consumer.projectPath) + @(Get-SharedConsumerRuntimeDependencies $consumer | ForEach-Object projectPath)
    foreach ($relative in $projects) {
        $project = Join-Path $repoRoot $relative
        [IO.Directory]::CreateDirectory((Split-Path -Parent $project)) | Out-Null
        [IO.File]::WriteAllText($project, '<Project />')
    }
    foreach ($artifact in Get-SharedConsumerPackageArtifacts $repoRoot $consumer $Configuration) {
        $target = Join-Path $repoRoot $artifact.Target
        [IO.Directory]::CreateDirectory((Split-Path -Parent $artifact.Source)) | Out-Null
        [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
        [IO.File]::WriteAllText($artifact.Source, ('new build: ' + (Split-Path -Leaf $artifact.Source)))
        [IO.File]::WriteAllText($target, ('old package: ' + $artifact.Target))
    }
}
$manifestPath = Join-Path $repoRoot "artifacts\shared-release\$Configuration\shared-package-manifest.json"
$snapshotPath = Join-Path $repoRoot "artifacts/shared-release/$Configuration/build-input.json"
$snapshot = New-AuraReleaseInputSnapshot $repoRoot $snapshotPath
[IO.File]::WriteAllText($manifestPath, '{"previous":true}')
$trackedPaths = New-Object System.Collections.Generic.List[string]
foreach ($consumer in $consumers) {
    foreach ($artifact in Get-SharedConsumerPackageArtifacts -RepoRoot $repoRoot -Consumer $consumer -Configuration $Configuration) {
        $trackedPaths.Add((Resolve-ConsumerPath -RepoRoot $repoRoot -RelativePath $artifact.Target))
    }
}
$trackedPaths.Add($manifestPath)

$beforeHashes = @{}
foreach ($path in $trackedPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Publish transaction fixture input is missing: $path"
    }
    $beforeHashes[$path] = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
}

$manifestLock = [System.IO.File]::Open(
    $manifestPath,
    [System.IO.FileMode]::Open,
    [System.IO.FileAccess]::Read,
    [System.IO.FileShare]::None)
try {
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $childOutput = @(& $windowsPowerShell `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File (Join-Path $repoRoot "tools\Publish-MainSharedConsumers.ps1") `
        -Configuration $Configuration 2>&1)
    $childExitCode = $LASTEXITCODE
}
finally {
    $manifestLock.Dispose()
    $ErrorActionPreference = $previousErrorAction
}

$childText = $childOutput | Out-String
$normalizedChildText = $childText -replace '\s', ''
$expectedFailure = ("Publish commit failed: $manifestPath") -replace '\s', ''
if ($childExitCode -eq 0) {
    throw "Publish transaction fixture unexpectedly committed while the manifest was locked."
}
if (-not $normalizedChildText.Contains($expectedFailure)) {
    throw "Publish transaction fixture failed before the locked manifest commit: $childText"
}
if ($normalizedChildText.Contains("Publishrollbackfailed:")) {
    throw "Publish transaction rollback reported a failure: $childText"
}

foreach ($path in $trackedPaths) {
    $afterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    if ($afterHash -ne $beforeHashes[$path]) {
        throw "Publish transaction rollback did not restore: $path"
    }
}

$targetDirectories = @($trackedPaths | ForEach-Object { Split-Path -Parent $_ } | Sort-Object -Unique)
$leftovers = @(
    foreach ($directory in $targetDirectories) {
        Get-ChildItem -LiteralPath $directory -File | Where-Object {
            $_.Name -match '\.publish-[0-9a-f]{32}\.(?:tmp|bak)$'
        }
    }
)
if ($leftovers.Count -gt 0) {
    throw "Publish transaction rollback left temporary files: $($leftovers.FullName -join ', ')"
}

# A validation receipt binds every generated artifact, including runtime DLLs.
$assemblies = @(foreach ($consumer in $consumers) {
    $files = @(foreach ($artifact in Get-SharedConsumerPackageArtifacts $repoRoot $consumer $Configuration) {
        [pscustomobject]@{target=$artifact.Target;sha256=(Get-FileHash -LiteralPath $artifact.Source).Hash}
    })
    [pscustomobject]@{id=$consumer.id;files=$files}
})
$receiptPath = Join-Path (Split-Path -Parent $manifestPath) 'validation.json'
[ordered]@{schemaVersion=2;success=$true;inputFingerprint=$snapshot.fingerprint;sharedSha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot "AuraSharedRuntime-Dev/bin/$Configuration/net472/Aura.Shared.dll")).Hash;assemblies=$assemblies} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
$terrias = @($consumers | Where-Object id -eq 'Terrias')[0]
$runtimeArtifacts = @(Get-SharedConsumerPackageArtifacts $repoRoot $terrias $Configuration | Where-Object Kind -eq runtime)
if ($runtimeArtifacts.Count -ne 2 -or @($runtimeArtifacts.Target) -notcontains 'Terrias/Scripts/Aura.Director.DetourBackend.dll' -or @($runtimeArtifacts.Target) -notcontains 'Terrias/Scripts/0Harmony.dll') {
    throw 'Terrias runtime package contract must include the detour backend and Harmony.'
}
foreach ($artifact in $runtimeArtifacts) {
    $originalBytes = [IO.File]::ReadAllBytes($artifact.Source)
    try {
        [IO.File]::WriteAllText($artifact.Source, 'changed after validation')
        $rejected = $false
        try {
            & (Join-Path $repoRoot 'tools/Publish-MainSharedConsumers.ps1') -Configuration $Configuration -ValidationReceiptPath $receiptPath
        } catch {
            if ($_.Exception.Message -notlike 'Product artifact changed after validation:*') { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Publisher accepted unvalidated runtime bytes: $($artifact.Target)" }
        Remove-Item -LiteralPath $artifact.Source
        $rejected = $false
        try {
            & (Join-Path $repoRoot 'tools/Publish-MainSharedConsumers.ps1') -Configuration $Configuration
        } catch {
            if ($_.Exception.Message -notlike 'Product artifact is missing:*') { throw }
            $rejected = $true
        }
        if (-not $rejected) { throw "Publisher accepted a missing runtime dependency: $($artifact.Target)" }
        foreach ($path in $trackedPaths) {
            if ((Get-FileHash -LiteralPath $path).Hash -ne $beforeHashes[$path]) { throw "Rejected publication changed: $path" }
        }
    } finally { [IO.File]::WriteAllBytes($artifact.Source, $originalBytes) }
}

& $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'tools/Publish-MainSharedConsumers.ps1') -Configuration $Configuration -ValidationReceiptPath $receiptPath
if ($LASTEXITCODE -ne 0) { throw 'Validated publish fixture failed.' }
$published = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($consumer in $consumers) {
    $manifestConsumer = @($published.consumers | Where-Object id -eq $consumer.id)
    $artifacts = @(Get-SharedConsumerPackageArtifacts $repoRoot $consumer $Configuration)
    if ($manifestConsumer.Count -ne 1 -or @($manifestConsumer[0].files).Count -ne $artifacts.Count) { throw 'Published artifact inventory is incomplete.' }
    foreach ($artifact in $artifacts) {
        $record = @($manifestConsumer[0].files | Where-Object target -eq $artifact.Target)
        $hash = (Get-FileHash -LiteralPath $artifact.Source).Hash
        if ($record.Count -ne 1 -or $record[0].kind -ne $artifact.Kind -or $record[0].sha256 -ne $hash -or (Get-FileHash -LiteralPath (Join-Path $repoRoot $artifact.Target)).Hash -ne $hash) {
            throw "Published artifact or manifest hash is incorrect: $($artifact.Target)"
        }
    }
}
$null = Assert-AuraReleaseInputSnapshot $repoRoot $snapshotPath

# Run the real orchestration with lightweight builders: trainer payload changes
# must precede the snapshot and publication, including on the first build.
Copy-Item -LiteralPath (Join-Path $sourceRoot 'tools/Rebuild-All.ps1') -Destination (Join-Path $repoRoot 'tools/Rebuild-All.ps1')
[IO.Directory]::CreateDirectory((Join-Path $repoRoot 'Managed')) | Out-Null
@'
param([string]$Configuration, [switch]$StopRunningTrainer)
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'AuraToolsExp/TrainingWorker'
[IO.Directory]::CreateDirectory($output) | Out-Null
foreach ($name in @('Worker','ControlCenter','SimulationViewer')) {
    [IO.File]::WriteAllText((Join-Path $output "AuraFoundationTrainer.$name.exe"), [Guid]::NewGuid().ToString())
}
'@ | Set-Content -LiteralPath (Join-Path $repoRoot 'tools/Build-AuraFoundationTrainer.ps1') -Encoding UTF8
@'
param([string]$Configuration, [string]$ManagedPath)
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'modules/AuraReleaseInputs.psm1') -Force
$inputPath = Join-Path $root "artifacts/shared-release/$Configuration/build-input.json"
$null = New-AuraReleaseInputSnapshot $root $inputPath
& (Join-Path $PSScriptRoot 'Publish-MainSharedConsumers.ps1') -Configuration $Configuration -InputSnapshotPath $inputPath
'@ | Set-Content -LiteralPath (Join-Path $repoRoot 'tools/Build-MainSharedConsumers.ps1') -Encoding UTF8
& (Join-Path $repoRoot 'tools/Rebuild-All.ps1') -Configuration $Configuration
$null = Assert-AuraReleaseInputSnapshot $repoRoot $snapshotPath
$rebuilt = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$trainerPayload = @($rebuilt.consumers | Where-Object id -eq AuraToolsExp | ForEach-Object files | Where-Object target -like 'AuraToolsExp/TrainingWorker/*.exe')
if ($trainerPayload.Count -ne 3) { throw 'Rebuild-All omitted freshly built trainer payload from the release manifest.' }
foreach ($file in $trainerPayload) {
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot $file.target)).Hash -ne $file.sha256) { throw "Rebuild-All left stale package payload: $($file.target)" }
}

$global:LASTEXITCODE = 0
Write-Host "Main shared consumer publication passed: rollback, missing dependencies, validated runtime hashes, Rebuild-All payload ordering and PowerShell 5.1 compatibility."
