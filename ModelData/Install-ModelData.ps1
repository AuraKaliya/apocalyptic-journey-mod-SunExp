[CmdletBinding()]
param(
    [string]$RepositoryRoot = "",
    [string]$ArchivePath = "",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$packageName = "AuraFoundationTrainer-ModelData-v1.zip"
$checksumName = "AuraFoundationTrainer-ModelData-v1.sha256"
$expectedSchemaVersion = 1

function Resolve-RepositoryRoot {
    param([string]$ExplicitRoot)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        return [IO.Path]::GetFullPath($ExplicitRoot)
    }
    return [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
}

function Assert-NoTrainerProcess {
    $trainer = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like "AuraFoundationTrainer*" } |
        Select-Object -First 1
    if ($null -ne $trainer) {
        throw "请先关闭 Aura 底模训练控制台和 Worker，再安装 ModelData。"
    }
}

function Assert-PackageHash {
    param(
        [string]$PackagePath,
        [string]$ChecksumPath
    )

    if (-not (Test-Path -LiteralPath $ChecksumPath -PathType Leaf)) {
        throw "缺少校验文件：$ChecksumPath"
    }
    $expected = ((Get-Content -LiteralPath $ChecksumPath -Raw).Trim() -split "\s+")[0]
    if ($expected -notmatch "^[0-9A-Fa-f]{64}$") {
        throw "SHA-256 校验文件格式无效：$ChecksumPath"
    }
    $actual = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $expected,
            $actual,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "ModelData 压缩包校验失败。期望 $expected，实际 $actual。"
    }
    Write-Host "压缩包 SHA-256 校验通过。" -ForegroundColor Green
}

function Copy-ArchiveDirectory {
    param(
        [string]$Source,
        [string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (
            Join-Path $Destination $_.Name) -Force
    }
}

$repoRoot = Resolve-RepositoryRoot $RepositoryRoot
if (-not (Test-Path -LiteralPath (
        Join-Path $repoRoot "AuraToolsExp") -PathType Container)) {
    throw "无法识别仓库根目录：$repoRoot"
}

if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    $ArchivePath = Join-Path $PSScriptRoot $packageName
}
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
$checksumPath = Join-Path $PSScriptRoot $checksumName
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw "缺少 ModelData 压缩包：$ArchivePath"
}

Assert-NoTrainerProcess
Assert-PackageHash -PackagePath $ArchivePath -ChecksumPath $checksumPath

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) (
    "AuraFoundationTrainer-ModelData-" + [Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
    Write-Host "正在解压 ModelData..." -ForegroundColor Cyan
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $temporaryRoot -Force

    $payloadRoot = Join-Path $temporaryRoot "AuraFoundationTrainer-ModelData-v1"
    $manifestPath = Join-Path $payloadRoot "model-data-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "压缩包缺少 model-data-manifest.json。"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw |
        ConvertFrom-Json
    if ([int]$manifest.SchemaVersion -ne $expectedSchemaVersion) {
        throw "不支持的 ModelData 协议版本：$($manifest.SchemaVersion)"
    }

    $sourceSettings = Join-Path $payloadRoot "controller-settings.json"
    $sourceCompatibility = Join-Path (
        Join-Path $payloadRoot "foundation-success-cases\v2") (
        [string]$manifest.CompatibilityDirectory)
    $sourceExpert = Join-Path $sourceCompatibility "e"
    $sourceObservations = Join-Path $sourceCompatibility "o"
    foreach ($requiredPath in @(
            $sourceSettings,
            $sourceExpert,
            $sourceObservations)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "压缩包内容不完整：$requiredPath"
        }
    }

    $expertCount = @(Get-ChildItem -LiteralPath $sourceExpert -File).Count
    $observationCount = @(
        Get-ChildItem -LiteralPath $sourceObservations -File).Count
    if ($expertCount -ne [int]$manifest.ExpertCaseFiles -or
        $observationCount -ne [int]$manifest.ObservationFiles) {
        throw "压缩包文件计数不匹配：专家 $expertCount/$($manifest.ExpertCaseFiles)，观测 $observationCount/$($manifest.ObservationFiles)。"
    }

    Write-Host (
        "内容校验通过：专家案例 {0}，奖励观测 {1}。" -f
        $expertCount,
        $observationCount) -ForegroundColor Green
    if ($VerifyOnly) {
        Write-Host "VerifyOnly 已完成，未修改 ModsData。" -ForegroundColor Yellow
        return
    }

    $settingsDirectory = Join-Path $repoRoot (
        "ModsData\Config\Owners\AuraToolsExp\FoundationTrainer")
    $settingsPath = Join-Path $settingsDirectory "controller-settings.json"
    $archiveRoot = Join-Path $repoRoot (
        "ModsData\Logs\AuraToolsExp\combat-simulation-results\" +
        "foundation-success-cases\v2")
    $destinationCompatibility = Join-Path $archiveRoot (
        [string]$manifest.CompatibilityDirectory)
    $destinationExpert = Join-Path $destinationCompatibility "e"
    $destinationObservations = Join-Path $destinationCompatibility "o"

    New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
        $backupPath = $settingsPath + ".bak-" + (
            Get-Date -Format "yyyyMMdd-HHmmss")
        Copy-Item -LiteralPath $settingsPath -Destination $backupPath
        Write-Host "已备份原训练参数：$backupPath" -ForegroundColor DarkGray
    }
    Copy-Item -LiteralPath $sourceSettings -Destination $settingsPath -Force
    Copy-ArchiveDirectory -Source $sourceExpert -Destination $destinationExpert
    Copy-ArchiveDirectory `
        -Source $sourceObservations `
        -Destination $destinationObservations

    Write-Host ""
    Write-Host "ModelData 配置完成。" -ForegroundColor Green
    Write-Host "训练参数：$settingsPath"
    Write-Host "专家案例：$destinationExpert"
    Write-Host "奖励观测：$destinationObservations"
    Write-Host "现在可以启动控制台并点击“开始 / 恢复训练”。" -ForegroundColor Cyan
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
