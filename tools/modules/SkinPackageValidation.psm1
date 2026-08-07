Set-StrictMode -Version Latest

function Read-SkinJsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Skin JSON file is missing: $Path"
    }

    return Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json
}

function Get-SkinJsonValue {
    param(
        [AllowNull()][object]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    return $property.Value
}

function Test-SkinPathInside {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Directory
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullDirectory = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    return $fullPath.Equals($fullDirectory, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullDirectory + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeSkinIdentitySegment {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Field,
        [Parameter(Mandatory)][string]$ManifestPath
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or
        $Value -eq "." -or
        $Value -eq ".." -or
        $Value.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        $Value.Contains([System.IO.Path]::DirectorySeparatorChar) -or
        $Value.Contains([System.IO.Path]::AltDirectorySeparatorChar)) {
        throw "Unsafe skin $Field '$Value' in $ManifestPath"
    }
}

function Resolve-SkinContentAsset {
    param(
        [Parameter(Mandatory)][string]$SkinDirectory,
        [Parameter(Mandatory)][string]$ConfiguredPath,
        [Parameter(Mandatory)][bool]$Directory,
        [Parameter(Mandatory)][string]$ManifestPath
    )

    $candidate = [System.IO.Path]::GetFullPath((Join-Path $SkinDirectory $ConfiguredPath))
    if (-not (Test-SkinPathInside -Path $candidate -Directory $SkinDirectory)) {
        throw "Skin asset '$ConfiguredPath' escapes its source directory in $ManifestPath"
    }

    if ($Directory) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Container)) {
            throw "Skin asset directory is missing: $ConfiguredPath in $ManifestPath"
        }
        return $candidate
    }

    foreach ($path in @($candidate, $candidate + ".png", $candidate + ".jpg", $candidate + ".jpeg")) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }

    throw "Skin asset is missing: $ConfiguredPath in $ManifestPath"
}

function Test-SkinPackageContent {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PackagePath)

    $resolvedPackagePath = [System.IO.Path]::GetFullPath($PackagePath)
    $package = Read-SkinJsonFile -Path $resolvedPackagePath
    $packageId = [string](Get-SkinJsonValue -Object $package -Name "packageId")
    $packageVersion = Get-SkinJsonValue -Object $package -Name "packageVersion"
    $packageResources = @(Get-SkinJsonValue -Object $package -Name "resources")
    if ((Get-SkinJsonValue -Object $package -Name "schemaVersion") -ne 1 -or
        [string]::IsNullOrWhiteSpace($packageId) -or
        [int64]$packageVersion -lt 1 -or
        $packageResources.Count -eq 0) {
        throw "Invalid skin package manifest: $resolvedPackagePath"
    }

    $participantKind = [string](Get-SkinJsonValue -Object $package -Name "participantKind")
    if ([string]::IsNullOrWhiteSpace($participantKind)) {
        $participantKind = "Content"
    }
    if (@("Content", "Tool") -notcontains $participantKind) {
        throw "Invalid skin participantKind '$participantKind' in $resolvedPackagePath"
    }

    $packageDirectory = Split-Path -Parent $resolvedPackagePath
    $seenIdentities = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $skins = [System.Collections.Generic.List[object]]::new()

    foreach ($resource in $packageResources) {
        $source = ([string](Get-SkinJsonValue -Object $resource -Name "source")).Trim()
        if ([string]::IsNullOrWhiteSpace($source) -or [System.IO.Path]::IsPathRooted($source)) {
            throw "Skin package source must be relative in $resolvedPackagePath"
        }

        $skinDirectory = [System.IO.Path]::GetFullPath((Join-Path $packageDirectory $source))
        if (-not (Test-SkinPathInside -Path $skinDirectory -Directory $packageDirectory) -or
            -not (Test-Path -LiteralPath $skinDirectory -PathType Container)) {
            throw "Skin package source is missing or escapes its package: $source"
        }

        $skinManifestPath = Join-Path $skinDirectory "skin.json"
        $characterDirectory = Split-Path -Parent $skinDirectory
        $characterManifestPath = Join-Path $characterDirectory "character.json"
        $character = Read-SkinJsonFile -Path $characterManifestPath
        $skin = Read-SkinJsonFile -Path $skinManifestPath
        if ((Get-SkinJsonValue -Object $character -Name "schemaVersion") -ne 2 -or
            (Get-SkinJsonValue -Object $character -Name "enabled") -eq $false) {
            throw "Invalid character manifest: $characterManifestPath"
        }
        if ((Get-SkinJsonValue -Object $skin -Name "schemaVersion") -ne 2 -or
            (Get-SkinJsonValue -Object $skin -Name "enabled") -eq $false) {
            throw "Invalid skin manifest: $skinManifestPath"
        }

        $targetCareerId = ([string](Get-SkinJsonValue -Object $character -Name "targetCareerId")).Trim()
        if ([string]::IsNullOrWhiteSpace($targetCareerId)) {
            $targetCareerId = Split-Path -Leaf $characterDirectory
        }
        if ($targetCareerId -match '^\d+$') {
            throw "Official career id '$targetCareerId' must use its canonical runtime id in $characterManifestPath"
        }
        Assert-SafeSkinIdentitySegment -Value $targetCareerId -Field "targetCareerId" -ManifestPath $characterManifestPath

        $skinTargetCareerId = ([string](Get-SkinJsonValue -Object $skin -Name "targetCareerId")).Trim()
        if (-not [string]::IsNullOrWhiteSpace($skinTargetCareerId) -and
            -not $skinTargetCareerId.Equals($targetCareerId, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Skin targetCareerId differs from its character manifest: $skinManifestPath"
        }

        $skinId = ([string](Get-SkinJsonValue -Object $skin -Name "skinId")).Trim()
        Assert-SafeSkinIdentitySegment -Value $skinId -Field "skinId" -ManifestPath $skinManifestPath
        $identity = ($targetCareerId + "::" + $skinId).ToLowerInvariant()
        if (-not $seenIdentities.Add($identity)) {
            throw "Duplicate skin identity '$identity' in $resolvedPackagePath"
        }

        $skinAssets = Get-SkinJsonValue -Object $skin -Name "assets"
        if ($null -eq $skinAssets) {
            throw "Missing assets in $skinManifestPath"
        }

        $resolvedAssets = @{}
        foreach ($field in @("CareerImage", "Avatar", "Character", "DollIcon", "ChoiceIcon", "Animation")) {
            $configuredPath = ([string](Get-SkinJsonValue -Object $skinAssets -Name $field)).Trim()
            if ([string]::IsNullOrWhiteSpace($configuredPath)) {
                continue
            }
            $resolvedAssets[$field] = Resolve-SkinContentAsset `
                -SkinDirectory $skinDirectory `
                -ConfiguredPath $configuredPath `
                -Directory ($field -eq "Animation") `
                -ManifestPath $skinManifestPath
        }
        if ($resolvedAssets.Count -eq 0) {
            throw "No valid assets declared by $skinManifestPath"
        }

        $preview = ([string](Get-SkinJsonValue -Object $skin -Name "preview")).Trim()
        if (-not [string]::IsNullOrWhiteSpace($preview)) {
            $null = Resolve-SkinContentAsset `
                -SkinDirectory $skinDirectory `
                -ConfiguredPath $preview `
                -Directory $false `
                -ManifestPath $skinManifestPath
        }

        $skins.Add([pscustomobject]@{
            TargetCareerId = $targetCareerId
            SkinId = $skinId
            Source = $source
            ManifestPath = $skinManifestPath
            Manifest = $skin
            Assets = $resolvedAssets
        })
    }

    return [pscustomobject]@{
        PackagePath = $resolvedPackagePath
        Package = $package
        ParticipantKind = $participantKind
        Skins = $skins.ToArray()
    }
}

Export-ModuleMember -Function Test-SkinPackageContent
