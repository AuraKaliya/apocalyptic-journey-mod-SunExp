Set-StrictMode -Version Latest

function Resolve-ConsumerPath {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Shared consumer paths must be repository-relative: $RelativePath"
    }

    $root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\', '/')
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $resolved.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Shared consumer path escapes the repository: $RelativePath"
    }
    return $resolved
}

function Get-SharedConsumerManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$ManifestPath = ""
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
        $ManifestPath = Join-Path $resolvedRoot "tools\shared-consumers.json"
    }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Shared consumer manifest is missing: $ManifestPath"
    }

    $manifest = Get-Content -Raw -LiteralPath $ManifestPath | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Unsupported shared consumer manifest schemaVersion: $($manifest.schemaVersion)"
    }

    $ids = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($consumer in @($manifest.consumers)) {
        foreach ($required in @("id", "classification", "projectPath", "assemblyName", "packagePath")) {
            if ([string]::IsNullOrWhiteSpace([string]$consumer.$required)) {
                throw "Shared consumer is missing '$required'."
            }
        }
        if (-not $ids.Add([string]$consumer.id)) {
            throw "Duplicate shared consumer id: $($consumer.id)"
        }
        if ([string]$consumer.classification -notin @("product", "test")) {
            throw "Unsupported shared consumer classification: $($consumer.id) -> $($consumer.classification)"
        }

        $project = Resolve-ConsumerPath -RepoRoot $resolvedRoot -RelativePath ([string]$consumer.projectPath)
        [void](Resolve-ConsumerPath -RepoRoot $resolvedRoot -RelativePath ([string]$consumer.packagePath))
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
            throw "Shared consumer project is missing: $($consumer.projectPath)"
        }
        $packageNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        [void]$packageNames.Add('Entry.dll')
        [void]$packageNames.Add('Aura.Shared.dll')
        foreach ($dependency in @(Get-SharedConsumerRuntimeDependencies -Consumer $consumer)) {
            $dependencyProject = Resolve-ConsumerPath -RepoRoot $resolvedRoot -RelativePath ([string]$dependency.projectPath)
            if (-not (Test-Path -LiteralPath $dependencyProject -PathType Leaf)) {
                throw "Consumer runtime dependency project is missing: $($dependency.projectPath)"
            }
            if (@($dependency.files).Count -eq 0) { throw "Consumer runtime dependency has no package files: $($dependency.projectPath)" }
            foreach ($name in @($dependency.files)) {
                if ([string]$name -notmatch '^[^\\/:*?"<>|]+\.dll$' -or -not $packageNames.Add([string]$name)) {
                    throw "Invalid or duplicate consumer runtime package file: $($consumer.id)/$name"
                }
            }
        }
    }

    return $manifest
}

function Get-SharedConsumers {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [ValidateSet("product", "test")][string]$Classification = "product",
        [switch]$DefaultOnly,
        [string[]]$Id = @()
    )

    $manifest = Get-SharedConsumerManifest -RepoRoot $RepoRoot
    $selected = @($manifest.consumers | Where-Object {
        [string]$_.classification -eq $Classification `
            -and (-not $DefaultOnly -or [bool]$_.defaultBuild) `
            -and ($Id.Count -eq 0 -or $Id -contains [string]$_.id)
    })
    if ($Id.Count -gt 0 -and $selected.Count -ne $Id.Count) {
        $found = @($selected | ForEach-Object { [string]$_.id })
        $missing = @($Id | Where-Object { $found -notcontains $_ })
        throw "Unknown or misclassified shared consumer id(s): $($missing -join ', ')"
    }
    return $selected
}

function Get-SharedConsumerAssemblyPath {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][object]$Consumer,
        [Parameter(Mandatory)][string]$Configuration
    )

    $project = Resolve-ConsumerPath -RepoRoot $RepoRoot -RelativePath ([string]$Consumer.projectPath)
    $projectRoot = Split-Path -Parent $project
    return Join-Path $projectRoot "bin\$Configuration\net472\$($Consumer.assemblyName).dll"
}

function Get-SharedConsumerRuntimeDependencies {
    param([Parameter(Mandatory)][object]$Consumer)
    if ($null -ne $Consumer.PSObject.Properties['runtimeDependencies']) {
        return @($Consumer.runtimeDependencies)
    }
}

function Get-SharedConsumerPackageArtifacts {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][object]$Consumer,
        [Parameter(Mandatory)][string]$Configuration
    )

    $packagePath = ([string]$Consumer.packagePath).Replace('\', '/').TrimEnd('/')
    [pscustomobject]@{
        Source = Get-SharedConsumerAssemblyPath -RepoRoot $RepoRoot -Consumer $Consumer -Configuration $Configuration
        Target = "$packagePath/Entry.dll"
        Kind = 'entry'
    }
    [pscustomobject]@{
        Source = Join-Path $RepoRoot "AuraSharedRuntime-Dev/bin/$Configuration/net472/Aura.Shared.dll"
        Target = "$packagePath/Aura.Shared.dll"
        Kind = 'shared'
    }
    foreach ($dependency in @(Get-SharedConsumerRuntimeDependencies -Consumer $Consumer)) {
        $project = Resolve-ConsumerPath -RepoRoot $RepoRoot -RelativePath ([string]$dependency.projectPath)
        foreach ($name in @($dependency.files)) {
            [pscustomobject]@{
                Source = Join-Path (Split-Path -Parent $project) "bin/$Configuration/net472/$name"
                Target = "$packagePath/$name"
                Kind = 'runtime'
            }
        }
    }
}

Export-ModuleMember -Function `
    Resolve-ConsumerPath, `
    Get-SharedConsumerManifest, `
    Get-SharedConsumers, `
    Get-SharedConsumerAssemblyPath, `
    Get-SharedConsumerRuntimeDependencies, `
    Get-SharedConsumerPackageArtifacts
