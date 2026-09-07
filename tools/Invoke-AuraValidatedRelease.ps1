param(
    [string]$Configuration='Release',
    [string]$UnityPath='D:/UnityFile/6000.0.46f1/Editor/Unity.exe',
    [string]$GameDataDirectory=''
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'modules/AuraReleaseInputs.psm1') -Force
$releaseMutex = Enter-AuraReleaseLock -RepoRoot $repoRoot
try {
Import-Module (Join-Path $PSScriptRoot 'modules/SharedConsumerManifest.psm1') -Force
$artifactRoot=Join-Path $repoRoot "artifacts/shared-release/$Configuration"
$inputPath=Join-Path $artifactRoot 'build-input.json'
$snapshot=New-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $inputPath
& (Join-Path $PSScriptRoot 'Build-MainSharedConsumers.ps1') -Configuration $Configuration -SkipPublish -InputSnapshotPath $inputPath
& dotnet build (Join-Path $repoRoot 'AuraToolsExp-Dev.Tests/AuraToolsExp-Dev.Tests.csproj') -c $Configuration /v:minimal
if($LASTEXITCODE -ne 0){throw 'Behavior test compilation failed.'}
$checks=New-Object 'System.Collections.Generic.List[object]'
function Invoke-Check([string]$Name, [scriptblock]$Run){
    $started=[DateTime]::UtcNow
    & $Run
    $checks.Add([pscustomobject]@{name=$Name;success=$true;elapsedSeconds=([DateTime]::UtcNow-$started).TotalSeconds})
}
Invoke-Check 'AuraTools behavior and content' { & (Join-Path $PSScriptRoot 'Test-AuraToolsExp.ps1') -Configuration $Configuration -SkipBuild -SkipModelIntegration }
Invoke-Check 'Terrias behavior' { & (Join-Path $PSScriptRoot 'Test-TerriasCSharp.ps1') -Configuration $Configuration -SkipBuild }
Invoke-Check 'Olimya behavior and content' { & (Join-Path $PSScriptRoot 'Test-TerriasOlimya.ps1') -Configuration $Configuration }
Invoke-Check 'Columbina behavior and content' { & (Join-Path $PSScriptRoot 'Test-TerriasColumbina.ps1') -Configuration $Configuration }
Invoke-Check 'Moon Homecoming behavior and content' { & (Join-Path $PSScriptRoot 'Test-TerriasMoonHomecoming.ps1') }
Invoke-Check 'Shared behavior' { & (Join-Path $PSScriptRoot 'Test-AuraSharedCore.ps1') -Configuration $Configuration }
Invoke-Check 'Shared compatibility' { & (Join-Path $PSScriptRoot 'Test-SharedRuntimeCompatibility.ps1') -Configuration $Configuration -SkipBuild }
Invoke-Check 'Release failure boundaries' { & (Join-Path $PSScriptRoot 'Test-AuraReleaseInputs.ps1') }
Invoke-Check 'Terrias content' { & (Join-Path $PSScriptRoot 'Test-TerriasContent.ps1') }
Invoke-Check 'Terrias resources' { & (Join-Path $PSScriptRoot 'Test-TerriasResources.ps1') }
Invoke-Check 'Event CG resource closure' { & (Join-Path $PSScriptRoot 'Test-AuraEventCgResources.ps1') }
Invoke-Check 'Terrias architecture' { & (Join-Path $PSScriptRoot 'Test-TerriasArchitecture.ps1') }
Invoke-Check 'Shared architecture' { & (Join-Path $PSScriptRoot 'Test-SharedArchitectureGuidelines.ps1') }
Invoke-Check 'Content ownership' { & (Join-Path $PSScriptRoot 'Test-ContentToolSharedBoundary.ps1') }
Invoke-Check 'Write ownership' { & (Join-Path $PSScriptRoot 'Test-SharedWriteEntrypoints.ps1') }
Invoke-Check 'RPC authority' { & (Join-Path $PSScriptRoot 'Test-NetworkRpcAuthority.ps1') }
Invoke-Check 'Unity replay and resource adapters' { & (Join-Path $PSScriptRoot 'Test-AuraToolsReplayNativeUi.ps1') -UnityPath $UnityPath }
$null=Assert-AuraReleaseInputSnapshot -RepoRoot $repoRoot -Path $inputPath
$assemblies=@(foreach($consumer in Get-SharedConsumers -RepoRoot $repoRoot -Classification product -DefaultOnly){
    $path=Get-SharedConsumerAssemblyPath -RepoRoot $repoRoot -Consumer $consumer -Configuration $Configuration
    [pscustomobject]@{id=$consumer.id;sha256=(Get-FileHash -LiteralPath $path).Hash}
})
$receiptPath=Join-Path $artifactRoot 'validation.json'
[ordered]@{schemaVersion=1;success=$true;inputFingerprint=$snapshot.fingerprint;sharedSha256=(Get-FileHash -LiteralPath (Join-Path $repoRoot "AuraSharedRuntime-Dev/bin/$Configuration/net472/Aura.Shared.dll")).Hash;completedUtc=[DateTime]::UtcNow.ToString('O');checks=$checks.ToArray();assemblies=$assemblies;runtimeAcceptance='Real game and multiplayer acceptance remain separate from automated validation.'}|
    ConvertTo-Json -Depth 7|Set-Content -LiteralPath $receiptPath -Encoding UTF8
& (Join-Path $PSScriptRoot 'Publish-MainSharedConsumers.ps1') -Configuration $Configuration -InputSnapshotPath $inputPath -ValidationReceiptPath $receiptPath
Invoke-Check 'DLL package integrity' { & (Join-Path $PSScriptRoot 'Test-SharedDllPackaging.ps1') -Configuration $Configuration }
if(-not[string]::IsNullOrWhiteSpace($GameDataDirectory)){
    & (Join-Path $PSScriptRoot 'Deploy-AuraProducts.ps1') -Configuration $Configuration -GameDataDirectory $GameDataDirectory
}
Write-Host "Validated release complete: $receiptPath"

} finally { $releaseMutex.ReleaseMutex(); $releaseMutex.Dispose() }
