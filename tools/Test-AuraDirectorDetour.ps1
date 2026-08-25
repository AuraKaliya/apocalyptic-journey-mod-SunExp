param(
    [string]$Configuration = "Release",
    [string]$ManagedPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
if ([string]::IsNullOrWhiteSpace($ManagedPath)) {
    $ManagedPath = Join-Path $repoRoot "Managed"
}

$testProject = Join-Path $repoRoot "AuraDirectorDetour.Tests\AuraDirectorDetour.Tests.csproj"
dotnet build $testProject -c $Configuration /p:ManagedPath="$ManagedPath" /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "AuraDirector detour test build failed."
}

$testExe = Join-Path $repoRoot "AuraDirectorDetour.Tests\bin\$Configuration\net472\AuraDirectorDetour.Tests.exe"
& $testExe $ManagedPath
if ($LASTEXITCODE -ne 0) {
    throw "AuraDirector detour behavior tests failed."
}

[xml]$terriasProject = Get-Content -Raw -LiteralPath (Join-Path $repoRoot "Terrias-Dev\Terrias.Dll.csproj")
$projectReferences = @($terriasProject.Project.ItemGroup.ProjectReference | ForEach-Object { [string]$_.Include })
if ($projectReferences -notcontains "..\AuraDirectorDetour-Dev\Aura.Director.DetourBackend.csproj") {
    throw "Terrias must reference the optional AuraDirector detour backend project."
}

$terriasScripts = Join-Path $repoRoot "Terrias\Scripts"
foreach ($binary in @("0Harmony.dll", "Aura.Director.DetourBackend.dll")) {
    if (-not (Test-Path -LiteralPath (Join-Path $terriasScripts $binary) -PathType Leaf)) {
        throw "Terrias director runtime binary is missing: Terrias\Scripts\$binary"
    }
    $siblingPackages = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification product -DefaultOnly |
        Where-Object { [string]$_.id -ne "Terrias" } |
        ForEach-Object { ([string]$_.packagePath).Replace('/', '\') })
    foreach ($sibling in $siblingPackages) {
        if (Test-Path -LiteralPath (Join-Path (Join-Path $repoRoot $sibling) $binary) -PathType Leaf) {
            throw "AuraDirector technical binary must remain scoped to Terrias: $sibling\$binary"
        }
    }
}

Write-Host "AuraDirector behavior and packaging validation passed."
