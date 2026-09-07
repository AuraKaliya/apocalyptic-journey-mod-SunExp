param(
    [Parameter(Mandatory)][string]$GameDataDirectory,
    [string]$Configuration = 'Release',
    [string]$BackupDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'modules/RepositoryPath.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/AuraReleaseInputs.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/AuraProductDeployment.psm1') -Force
$releaseMutex = Enter-AuraReleaseLock -RepoRoot $repoRoot
try {
$gameData = [IO.Path]::GetFullPath($GameDataDirectory).TrimEnd('\','/')
if (-not (Test-Path -LiteralPath (Join-Path $gameData 'Managed/Witch.Core.dll'))) { throw 'GameDataDirectory is not a Witch game data directory.' }
$gameRoot = Split-Path -Parent $gameData
$running = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($gameRoot + '\',[StringComparison]::OrdinalIgnoreCase) -and $_.Name -notlike '*CrashHandler*'
})
if ($running.Count -gt 0) { throw 'Exit the game before deploying product packages.' }
$manifestPath = Join-Path $repoRoot "artifacts/shared-release/$Configuration/shared-package-manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schemaVersion -lt 2 -or $null -eq $manifest.validation -or -not $manifest.validation.success) { throw 'Deployment requires a complete validated package manifest.' }
$snapshotPath = Join-Path $repoRoot "artifacts/shared-release/$Configuration/build-input.json"
$snapshot = Assert-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $snapshotPath
if ($snapshot.fingerprint -ne $manifest.inputFingerprint) { throw 'Package does not match the validated input snapshot.' }
foreach ($name in @('Witch.dll','Witch.Core.dll','AllScripts.dll')) {
    if ((Get-FileHash -LiteralPath (Join-Path $repoRoot "Managed/$name")).Hash -ne (Get-FileHash -LiteralPath (Join-Path $gameData "Managed/$name")).Hash) {
        throw "Installed game Managed differs from the build: $name"
    }
}
if ([string]::IsNullOrWhiteSpace($BackupDirectory)) { $BackupDirectory = Join-Path $repoRoot ('artifacts/product-deploy/' + $manifest.transactionId) }
$backupRoot = [IO.Path]::GetFullPath($BackupDirectory)
$modsRoot = Join-Path $gameData 'Mods'
[IO.Directory]::CreateDirectory($backupRoot) | Out-Null
if (Test-Path -LiteralPath (Join-Path $backupRoot 'deployment.json')) { throw 'Deployment receipt already exists. Use Restore-AuraProductDeployment.ps1 for an interrupted deployment, or a new backup directory.' }
$operations = New-Object 'System.Collections.Generic.List[object]'
foreach ($consumer in $manifest.consumers) {
    if ($consumer.id -notin @('Terrias','AuraToolsExp')) { throw "Unexpected product: $($consumer.id)" }
    foreach ($file in $consumer.files) {
        $relative = ([string]$file.target).Replace('\','/')
        if (-not $relative.StartsWith(([string]$consumer.id + '/'),[StringComparison]::Ordinal) -or $relative -match '(^|/)\.\.(/|$)') { throw "Invalid package path: $relative" }
        $source = [IO.Path]::GetFullPath((Join-Path $repoRoot $relative))
        $target = [IO.Path]::GetFullPath((Join-Path $modsRoot $relative))
        $backup = [IO.Path]::GetFullPath((Join-Path $backupRoot ('previous/' + $relative)))
        $stage = [IO.Path]::GetFullPath((Join-Path $backupRoot ('staged/' + $relative)))
        $null = Get-RepositoryRelativePath -RepoRoot $repoRoot -Path $source
        $null = Get-RepositoryRelativePath -RepoRoot $modsRoot -Path $target
        $null = Get-RepositoryRelativePath -RepoRoot $backupRoot -Path $backup
        $null = Get-RepositoryRelativePath -RepoRoot $backupRoot -Path $stage
        Assert-AuraDeploymentPath $modsRoot $target
        Assert-AuraDeploymentPath $backupRoot $backup
        Assert-AuraDeploymentPath $backupRoot $stage
        if ((Get-FileHash -LiteralPath $source).Hash -ne $file.sha256) { throw "Package source changed: $relative" }
        $existed = Test-Path -LiteralPath $target -PathType Leaf
        if ($existed -and (Get-FileHash -LiteralPath $target).Hash -eq $file.sha256) { continue }
        [IO.Directory]::CreateDirectory((Split-Path -Parent $stage)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $stage -Force
        if ((Get-FileHash -LiteralPath $stage).Hash -ne $file.sha256) { throw "Staging failed: $relative" }
        if ($existed) {
            [IO.Directory]::CreateDirectory((Split-Path -Parent $backup)) | Out-Null
            if (Test-Path -LiteralPath $backup) { throw "Backup already exists: $relative" }
            Copy-Item -LiteralPath $target -Destination $backup
        }
        $previousHash = if($existed){(Get-FileHash -LiteralPath $backup).Hash}else{''}
        $operations.Add([pscustomobject]@{ relative=$relative; target=$target; stage=$stage; backup=$backup; existed=$existed; previousSha256=$previousHash; sha256=$file.sha256; applied=$false })
    }
}
$journalPath = Join-Path $backupRoot 'deployment.json'
function Save-Journal([string]$State) {
    Write-AuraDeploymentJournal $journalPath ([ordered]@{ schemaVersion=1; transactionId=$manifest.transactionId; state=$State; inputFingerprint=$manifest.inputFingerprint; gameData=$gameData; operations=$operations.ToArray() })
}
Save-Journal 'Prepared'
try {
    foreach ($operation in $operations) {
        [IO.Directory]::CreateDirectory((Split-Path -Parent $operation.target)) | Out-Null
        $operation.applied = $true
        Copy-Item -LiteralPath $operation.stage -Destination $operation.target -Force
    }
    foreach ($consumer in $manifest.consumers) { foreach ($file in $consumer.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $modsRoot $file.target)).Hash -ne $file.sha256) { throw "Installed package mismatch: $($file.target)" }
    } }
    Save-Journal 'Verified'
} catch {
    Restore-AuraDeploymentOperations -Operations $operations.ToArray() -ModsRoot $modsRoot -BackupRoot $backupRoot
    Save-Journal 'RolledBack'
    throw
}
Write-Host "Complete product deployment verified: changed=$($operations.Count); receipt=$journalPath"

} finally { $releaseMutex.ReleaseMutex(); $releaseMutex.Dispose() }
