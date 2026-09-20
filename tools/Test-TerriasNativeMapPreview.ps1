param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$product = Join-Path $root "Terrias-Dev/bin/$Configuration/net472/Terrias.Aura.dll"
if (-not (Test-Path -LiteralPath $product)) { throw 'Build the products before running the native map regression.' }
$project = Join-Path $root 'Terrias.NativeMapPreview.Tests/Terrias.NativeMapPreview.Tests.csproj'
& dotnet build $project -c $Configuration --nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw 'Native map preview regression compilation failed.' }
& (Join-Path $root "Terrias.NativeMapPreview.Tests/bin/$Configuration/net472/Terrias.NativeMapPreview.Tests.exe") $root
if ($LASTEXITCODE -ne 0) { throw 'Native map preview regression failed.' }
