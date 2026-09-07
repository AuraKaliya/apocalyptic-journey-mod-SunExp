param()
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'modules/AuraReleaseInputs.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'modules/AuraProductDeployment.psm1') -Force
$fixture=Join-Path $repoRoot ('output/release-contract-tests/'+[Guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory((Join-Path $fixture 'Terrias/SharedResources'))
$null=[IO.Directory]::CreateDirectory((Join-Path $fixture 'tools'))
$asset=Join-Path $fixture 'Terrias/SharedResources/fixture.json'
[IO.File]::WriteAllText($asset,'{"version":1}')
[IO.File]::WriteAllText((Join-Path $fixture 'tools/check.py'),'print(1)')
$snapshotPath=Join-Path $fixture 'input.json'
$snapshot=New-AuraReleaseInputSnapshot $fixture $snapshotPath
if(@($snapshot.files).Count -ne 2){throw 'Snapshot omitted package or test inputs.'}
$null=Assert-AuraReleaseInputSnapshot $fixture $snapshotPath
[IO.File]::WriteAllText($asset,'{"version":2}')
$rejected=$false
try{$null=Assert-AuraReleaseInputSnapshot $fixture $snapshotPath}catch{$rejected=$true}
if(-not $rejected){throw 'Snapshot allowed modified inputs.'}
[IO.File]::WriteAllText($asset,'{"version":1}')
[IO.File]::WriteAllText((Join-Path $fixture 'Terrias/SharedResources/new.json'),'{}')
$rejected=$false
try{$null=Assert-AuraReleaseInputSnapshot $fixture $snapshotPath}catch{$rejected=$true}
if(-not $rejected){throw 'Snapshot allowed newly introduced assets.'}
$mods=Join-Path $fixture 'game/Mods'
$backup=Join-Path $fixture 'backup'
$null=[IO.Directory]::CreateDirectory((Join-Path $mods 'Terrias'))
$null=[IO.Directory]::CreateDirectory($backup)
$target=Join-Path $mods 'Terrias/test.bin'
$original=Join-Path $backup 'original.bin'
[IO.File]::WriteAllText($original,'original')
$operation=[pscustomobject]@{relative='Terrias/test.bin';target=$target;backup=$original;existed=$true;previousSha256=(Get-FileHash $original).Hash}
$newTarget=Join-Path $mods 'Terrias/new.bin'
$newOperation=[pscustomobject]@{relative='Terrias/new.bin';target=$newTarget;backup=(Join-Path $backup 'absent.bin');existed=$false;previousSha256=''}
$journal=Join-Path $backup 'deployment.json'
Write-AuraDeploymentJournal $journal ([ordered]@{state='Prepared';operations=@($operation,$newOperation)})
[IO.File]::WriteAllText($target,'partially overwritten')
[IO.File]::WriteAllText($newTarget,'new data')
$recovered=Get-Content $journal -Raw -Encoding UTF8|ConvertFrom-Json
Restore-AuraDeploymentOperations @($recovered.operations) $mods $backup
if([IO.File]::ReadAllText($target) -ne 'original' -or (Test-Path $newTarget)){throw 'Interrupted file replacement did not restore all originals.'}
Write-AuraDeploymentJournal $journal ([ordered]@{state='RolledBack';operations=@($operation,$newOperation)})
Write-AuraDeploymentJournal $journal ([ordered]@{state='Verified';operations=@($operation,$newOperation)})
if((Get-Content $journal -Raw -Encoding UTF8|ConvertFrom-Json).state -ne 'Verified' -or -not(Test-Path -LiteralPath ($journal+'.previous'))){throw 'Atomic journal update did not retain its current and previous states.'}
$operation.target=Join-Path $fixture 'outside.bin'
$rejected=$false
try{Restore-AuraDeploymentOperations @($operation) $mods $backup}catch{$rejected=$true}
if(-not $rejected){throw 'Rollback accepted a path outside the installation.'}
$lock=Enter-AuraReleaseLock $fixture
try{
    $sameThread=Enter-AuraReleaseLock $fixture
    $sameThread.ReleaseMutex();$sameThread.Dispose()
}finally{$lock.ReleaseMutex();$lock.Dispose()}
Write-Host 'Release input and interrupted-deployment contracts passed: inventory, content, restoration, path ownership and nested transaction lock.'
