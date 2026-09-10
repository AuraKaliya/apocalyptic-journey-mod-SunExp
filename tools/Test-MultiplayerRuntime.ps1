param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
& dotnet run --project (Join-Path $repoRoot 'AuraSharedCore.NetworkTests/AuraSharedCore.NetworkTests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Shared native identity lifecycle tests failed.' }
& dotnet run --project (Join-Path $repoRoot 'Terrias-Dev.MultiplayerTests/Terrias-Dev.MultiplayerTests.csproj') -c $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer receive and transaction tests failed.' }
