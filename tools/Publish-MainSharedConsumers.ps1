param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot "tools\modules\SharedConsumerManifest.psm1") -Force
Import-Module (Join-Path $repoRoot "tools\modules\RepositoryPath.psm1") -Force

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
$transactionCommitted = $false
$artifactRoot = Join-Path $repoRoot "artifacts\shared-release\$Configuration"
$manifestPath = Join-Path $artifactRoot "shared-package-manifest.json"

try {
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
            $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.Source).Hash
            $operation = [pscustomobject]@{
                Consumer = [string]$consumer.id
                Kind = [string]$item.Kind
                Source = [string]$item.Source
                SourceHash = $sourceHash
                Target = $target
                Stage = "$target.publish-$transactionId.tmp"
                Backup = "$target.publish-$transactionId.bak"
                Existed = Test-Path -LiteralPath $target -PathType Leaf
                Committed = $false
            }
            $operations.Add($operation)

            Copy-Item -LiteralPath $operation.Source -Destination $operation.Stage -Force
            $stageHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $operation.Stage).Hash
            if ($operation.SourceHash -ne $stageHash) {
                throw "Staged package hash mismatch: $target"
            }
        }
    }

    $packageOperations = $operations.ToArray()
    $publishManifest = [ordered]@{
        schemaVersion = 1
        transactionId = $transactionId
        configuration = $Configuration
        sharedSha256 = $sharedHash
        consumers = @($packageOperations | Group-Object Consumer | ForEach-Object {
            [ordered]@{
                id = $_.Name
                files = @($_.Group | ForEach-Object {
                    [ordered]@{
                        kind = $_.Kind
                        target = Get-RepositoryRelativePath -RepoRoot $repoRoot -Path $_.Target
                        sha256 = $_.SourceHash
                    }
                })
            }
        })
    }

    [System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    $manifestOperation = [pscustomobject]@{
        Consumer = ""
        Kind = "manifest"
        Source = ""
        SourceHash = ""
        Target = $manifestPath
        Stage = "$manifestPath.publish-$transactionId.tmp"
        Backup = "$manifestPath.publish-$transactionId.bak"
        Existed = Test-Path -LiteralPath $manifestPath -PathType Leaf
        Committed = $false
    }
    $operations.Add($manifestOperation)
    $manifestJson = $publishManifest | ConvertTo-Json -Depth 8
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText(
        $manifestOperation.Stage,
        $manifestJson + [System.Environment]::NewLine,
        $utf8NoBom)
    $manifestOperation.SourceHash = `
        (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestOperation.Stage).Hash

    foreach ($operation in $operations) {
        try {
            if ($operation.Existed) {
                [System.IO.File]::Replace($operation.Stage, $operation.Target, $operation.Backup, $true)
            }
            else {
                [System.IO.File]::Move($operation.Stage, $operation.Target)
            }
            $operation.Committed = $true
        }
        catch {
            throw "Publish commit failed: $($operation.Target): $($_.Exception.Message)"
        }
    }

    foreach ($operation in $operations) {
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $operation.Target).Hash
        if ($actual -ne $operation.SourceHash) {
            throw "Published artifact hash mismatch: $($operation.Target)"
        }
    }
    $transactionCommitted = $true
}
catch {
    for ($index = $operations.Count - 1; $index -ge 0; $index--) {
        $operation = $operations[$index]
        if (-not $operation.Committed) {
            continue
        }
        try {
            if ($operation.Existed) {
                if (-not (Test-Path -LiteralPath $operation.Backup -PathType Leaf)) {
                    throw "Rollback backup is missing: $($operation.Backup)"
                }
                if (Test-Path -LiteralPath $operation.Target -PathType Leaf) {
                    [System.IO.File]::Replace(
                        $operation.Backup,
                        $operation.Target,
                        $operation.Stage,
                        $true)
                }
                else {
                    [System.IO.File]::Move($operation.Backup, $operation.Target)
                }
            }
            elseif (-not $operation.Existed -and (Test-Path -LiteralPath $operation.Target -PathType Leaf)) {
                Remove-Item -LiteralPath $operation.Target -Force
            }
            $operation.Committed = $false
        }
        catch {
            Write-Warning "Publish rollback failed: $($operation.Target): $($_.Exception.Message)"
        }
    }
    throw
}
finally {
    foreach ($operation in $operations) {
        if (Test-Path -LiteralPath $operation.Stage -PathType Leaf) {
            Remove-Item -LiteralPath $operation.Stage -Force
        }
        if (($transactionCommitted -or -not $operation.Committed) `
            -and (Test-Path -LiteralPath $operation.Backup -PathType Leaf)) {
            Remove-Item -LiteralPath $operation.Backup -Force
        }
    }
}

Write-Host "Product consumer package transaction committed: $transactionId"
Write-Host "Aura.Shared.dll SHA256: $sharedHash"
Write-Host "Publish manifest: $manifestPath"
