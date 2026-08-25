param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force

$consumers = @(Get-SharedConsumers -RepoRoot $repoRoot -Classification product -DefaultOnly)
if ($consumers.Count -ne 2 `
    -or @($consumers.id) -notcontains "Terrias" `
    -or @($consumers.id) -notcontains "AuraToolsExp") {
    throw "Product shared consumers must be exactly Terrias and AuraToolsExp."
}

$sharedSource = Join-Path $repoRoot "AuraSharedRuntime-Dev\bin\$Configuration\net472\Aura.Shared.dll"
if (-not (Test-Path -LiteralPath $sharedSource -PathType Leaf)) {
    throw "Canonical Aura.Shared.dll is missing: $sharedSource"
}
$sharedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sharedSource).Hash
$transactionId = [Guid]::NewGuid().ToString("N")
$operations = New-Object System.Collections.Generic.List[object]

foreach ($consumer in $consumers) {
    $entrySource = Get-SharedConsumerAssemblyPath `
        -RepoRoot $repoRoot `
        -Consumer $consumer `
        -Configuration $Configuration
    if (-not (Test-Path -LiteralPath $entrySource -PathType Leaf)) {
        throw "Consumer assembly is missing: $($consumer.id) -> $entrySource"
    }

    $packageRoot = Resolve-ConsumerPath -RepoRoot $repoRoot -RelativePath ([string]$consumer.packagePath)
    [System.IO.Directory]::CreateDirectory($packageRoot) | Out-Null
    foreach ($item in @(
        [pscustomobject]@{ Source = $entrySource; Name = "Entry.dll"; Kind = "entry" },
        [pscustomobject]@{ Source = $sharedSource; Name = "Aura.Shared.dll"; Kind = "shared" }
    )) {
        $target = Join-Path $packageRoot $item.Name
        $stage = "$target.publish-$transactionId.tmp"
        $backup = "$target.publish-$transactionId.bak"
        Copy-Item -LiteralPath $item.Source -Destination $stage -Force
        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.Source).Hash
        $stageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $stage).Hash
        if ($sourceHash -ne $stageHash) {
            throw "Staged package hash mismatch: $target"
        }
        $operations.Add([pscustomobject]@{
            Consumer = [string]$consumer.id
            Kind = [string]$item.Kind
            Source = [string]$item.Source
            SourceHash = $sourceHash
            Target = $target
            Stage = $stage
            Backup = $backup
            Existed = Test-Path -LiteralPath $target -PathType Leaf
            Committed = $false
        })
    }
}

try {
    foreach ($operation in $operations) {
        if ($operation.Existed) {
            [System.IO.File]::Replace($operation.Stage, $operation.Target, $operation.Backup, $true)
        }
        else {
            [System.IO.File]::Move($operation.Stage, $operation.Target)
        }
        $operation.Committed = $true
    }

    foreach ($operation in $operations) {
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $operation.Target).Hash
        if ($actual -ne $operation.SourceHash) {
            throw "Published package hash mismatch: $($operation.Target)"
        }
    }
}
catch {
    foreach ($operation in @($operations | Where-Object Committed | Sort-Object Target -Descending)) {
        try {
            if ($operation.Existed -and (Test-Path -LiteralPath $operation.Backup -PathType Leaf)) {
                if (Test-Path -LiteralPath $operation.Target -PathType Leaf) {
                    [System.IO.File]::Replace($operation.Backup, $operation.Target, $null, $true)
                }
                else {
                    [System.IO.File]::Move($operation.Backup, $operation.Target)
                }
            }
            elseif (-not $operation.Existed -and (Test-Path -LiteralPath $operation.Target -PathType Leaf)) {
                Remove-Item -LiteralPath $operation.Target -Force
            }
        }
        catch {
            Write-Warning "Package rollback failed: $($operation.Target): $($_.Exception.Message)"
        }
    }
    throw
}
finally {
    foreach ($operation in $operations) {
        foreach ($temporary in @($operation.Stage, $operation.Backup)) {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) {
                Remove-Item -LiteralPath $temporary -Force
            }
        }
    }
}

$artifactRoot = Join-Path $repoRoot "artifacts\shared-release\$Configuration"
[System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$manifestPath = Join-Path $artifactRoot "shared-package-manifest.json"
$publishManifest = [ordered]@{
    schemaVersion = 1
    transactionId = $transactionId
    configuration = $Configuration
    sharedSha256 = $sharedHash
    consumers = @($operations | Group-Object Consumer | ForEach-Object {
        [ordered]@{
            id = $_.Name
            files = @($_.Group | ForEach-Object {
                [ordered]@{
                    kind = $_.Kind
                    target = [System.IO.Path]::GetRelativePath($repoRoot, $_.Target).Replace('\', '/')
                    sha256 = $_.SourceHash
                }
            })
        }
    })
}
$publishManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host "Product consumer package transaction committed: $transactionId"
Write-Host "Aura.Shared.dll SHA256: $sharedHash"
Write-Host "Publish manifest: $manifestPath"
