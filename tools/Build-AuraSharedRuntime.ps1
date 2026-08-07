param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

$project = Join-Path $repoRoot "AuraSharedRuntime-Dev\Aura.Shared.csproj"
dotnet build $project -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Aura.Shared runtime build failed."
}

$assemblyPath = Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472\Aura.Shared.dll"
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "Aura.Shared runtime DLL was not produced: $assemblyPath"
}
$assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
if ($assemblyName.Name -ne "Aura.Shared") {
    throw "Aura.Shared runtime DLL has the wrong assembly name: $($assemblyName.Name)"
}

Write-Host "Aura.Shared runtime build passed: $assemblyPath"
