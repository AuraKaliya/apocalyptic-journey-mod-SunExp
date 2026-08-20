param(
    [string]$ImageName = "aura-tools-ffmpeg:9d4ca21220-minimal.1",
    [switch]$SkipImageBuild
)

$ErrorActionPreference = "Stop"

function Write-Utf8Lf([string]$Path, [string]$Content) {
    $normalized = ($Content -replace "`r`n", "`n").TrimEnd("`r", "`n") + "`n"
    [IO.File]::WriteAllText($Path, $normalized, [Text.UTF8Encoding]::new($false))
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildRoot = Join-Path $PSScriptRoot "ffmpeg"
$runtimeRoot = Join-Path $repoRoot "AuraToolsExp\Runtime\ffmpeg\win-x64"
$stagingRoot = Join-Path $repoRoot ".codex\tmp\ffmpeg-win-x64"
$expectedRuntimeRoot = [IO.Path]::GetFullPath(
    (Join-Path $repoRoot "AuraToolsExp\Runtime\ffmpeg\win-x64"))
$resolvedRuntimeRoot = [IO.Path]::GetFullPath($runtimeRoot)
if (-not [string]::Equals(
        $resolvedRuntimeRoot,
        $expectedRuntimeRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "FFmpeg runtime output escaped the expected repository directory."
}

if (-not $SkipImageBuild) {
    docker build --tag $ImageName $buildRoot
    if ($LASTEXITCODE -ne 0) {
        throw "AuraToolsExp FFmpeg build image failed."
    }
}

if (Test-Path -LiteralPath $stagingRoot) {
    [IO.Directory]::Delete([IO.Path]::GetFullPath($stagingRoot), $true)
}
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
$dockerOutput = ([IO.Path]::GetFullPath($stagingRoot) -replace '\\', '/')
docker run --rm --volume "${dockerOutput}:/out" $ImageName
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp minimal FFmpeg compilation failed."
}

$payload = @(Get-ChildItem -LiteralPath $stagingRoot -File |
    Where-Object { $_.Extension -in @(".exe", ".dll") } |
    Sort-Object Name)
if (@($payload | Where-Object Name -eq "ffmpeg.exe").Count -ne 1 `
        -or @($payload | Where-Object Name -eq "ffprobe.exe").Count -ne 1 `
        -or @($payload | Where-Object Extension -eq ".dll").Count -eq 0) {
    throw "Minimal FFmpeg output does not contain both programs and shared libraries."
}

$files = @($payload | ForEach-Object {
    [ordered]@{
        path = $_.Name
        bytes = $_.Length
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
    }
})
$manifest = [ordered]@{
    schemaVersion = 2
    version = "9.0.1-aura-minimal.1"
    platform = "win-x64"
    license = "LGPL-3.0-or-later"
    sourceRevision = "9d4ca21220"
    sourceArchiveSha256 = "6d9f8e49d1b6c561b6abb007fa872e844be181fb2516fd6e77741d0dd838bfa4"
    sourceDateEpoch = 1786990956
    buildProfile = "aura-replay-win-x64-minimal-shared.v1"
    codecProfile = "mp4-mpeg4-aac-bt709.v1"
    files = $files
}
Write-Utf8Lf `
    (Join-Path $stagingRoot "manifest.json") `
    ($manifest | ConvertTo-Json -Depth 5)

$notice = @'
# FFmpeg Runtime Notice

AuraToolsExp distributes a feature-bounded shared-library FFmpeg build for its
replay media pipeline. The runtime is not discovered from PATH and does not
download executable code after installation.

- Source: FFmpeg
- Revision: `9d4ca21220`
- Source archive SHA-256: `6d9f8e49d1b6c561b6abb007fa872e844be181fb2516fd6e77741d0dd838bfa4`
- SOURCE_DATE_EPOCH: `1786990956`
- Build profile: `aura-replay-win-x64-minimal-shared.v1`
- License: GNU Lesser General Public License v3.0 or later
- Upstream source: <https://github.com/FFmpeg/FFmpeg>
- Exact source archive: <https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/9d4ca21220>
- Reproducible build entry: `tools/Build-AuraToolsFfmpeg.ps1`

The full LGPL v3 license text is included as `LICENSE.txt`. `manifest.json`
pins every executable and shared library by path, size, and SHA-256.
'@
Write-Utf8Lf (Join-Path $stagingRoot "NOTICE.md") $notice

if (Test-Path -LiteralPath $runtimeRoot) {
    [IO.Directory]::Delete($resolvedRuntimeRoot, $true)
}
New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
Get-ChildItem -LiteralPath $stagingRoot -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $runtimeRoot
}

$rawBytes = (Get-ChildItem -LiteralPath $runtimeRoot -File | Measure-Object Length -Sum).Sum
if ($rawBytes -gt 40MB) {
    throw "Minimal FFmpeg runtime exceeds the 40 MiB release budget: $rawBytes bytes."
}
Write-Host ("AuraToolsExp minimal FFmpeg runtime: {0:N2} MiB across {1} files." -f (
    $rawBytes / 1MB), (Get-ChildItem -LiteralPath $runtimeRoot -File).Count)
