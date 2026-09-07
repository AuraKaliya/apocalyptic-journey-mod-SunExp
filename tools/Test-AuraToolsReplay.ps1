param([string]$Configuration='Release', [switch]$SkipBuild)
$ErrorActionPreference='Stop'
$project=Join-Path (Split-Path -Parent $PSScriptRoot) 'AuraToolsExp-Dev.Tests/AuraToolsExp-Dev.Tests.csproj'
$arguments=@('run','--project',$project,'-c',$Configuration)
if($SkipBuild){$arguments+='--no-build'}
$arguments+=@('--','--suite','replay')
& dotnet @arguments
if($LASTEXITCODE -ne 0){throw 'Replay behavior suite failed.'}
