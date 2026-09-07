param([Parameter(Mandatory)][string]$ReceiptPath)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'modules/AuraReleaseInputs.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/AuraProductDeployment.psm1') -Force
$releaseMutex=Enter-AuraReleaseLock $repoRoot
try{
    $path=[IO.Path]::GetFullPath($ReceiptPath)
    $receipt=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
    if($receipt.schemaVersion -ne 1 -or $receipt.state -notin @('Prepared','Verified')){throw 'Receipt is not a restorable deployment.'}
    $gameRoot=Split-Path -Parent ([string]$receipt.gameData)
    if(@(Get-CimInstance Win32_Process|Where-Object{$_.ExecutablePath -and $_.ExecutablePath.StartsWith($gameRoot+'\',[StringComparison]::OrdinalIgnoreCase) -and $_.Name -notlike '*CrashHandler*'}).Count -gt 0){throw 'Exit the game before restoring packages.'}
    Restore-AuraDeploymentOperations @($receipt.operations) (Join-Path $receipt.gameData 'Mods') (Split-Path -Parent $path)
    $receipt.state='RolledBack'
    Write-AuraDeploymentJournal $path $receipt
    Write-Host "Package backup restored and verified: $path"
}finally{$releaseMutex.ReleaseMutex();$releaseMutex.Dispose()}
