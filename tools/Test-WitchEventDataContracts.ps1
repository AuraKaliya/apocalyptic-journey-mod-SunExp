param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

$project = Join-Path $repoRoot "WitchEventDataContract.Tests\WitchEventDataContract.Tests.csproj"
dotnet build $project -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Witch event data contract build failed."
}

$executable = Join-Path $repoRoot "WitchEventDataContract.Tests\bin\$Configuration\net8.0\WitchEventDataContract.Tests.dll"
dotnet $executable
if ($LASTEXITCODE -ne 0) {
    throw "Witch event data contract tests failed."
}
