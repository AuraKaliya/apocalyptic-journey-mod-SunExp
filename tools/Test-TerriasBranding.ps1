param()

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$legacyToken = -join [char[]](83, 117, 110, 69, 120, 112)
$expectedWorkshopId = "3741157062"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-ByteSequence {
    param(
        [byte[]]$Bytes,
        [byte[]]$Sequence
    )

    for ($i = 0; $i -le $Bytes.Length - $Sequence.Length; $i++) {
        $matches = $true
        for ($j = 0; $j -lt $Sequence.Length; $j++) {
            if ($Bytes[$i + $j] -ne $Sequence[$j]) {
                $matches = $false
                break
            }
        }

        if ($matches) {
            return $true
        }
    }

    return $false
}

function Test-BrandingTextPath {
    param([string]$RelativePath)

    $normalized = ([string]$RelativePath).Replace('\', '/')
    return -not $normalized.StartsWith("artifacts/", [StringComparison]::OrdinalIgnoreCase) `
        -and -not $normalized.StartsWith("tmp/", [StringComparison]::OrdinalIgnoreCase) `
        -and -not $normalized.StartsWith("开发参考资料/", [StringComparison]::OrdinalIgnoreCase) `
        -and -not $normalized.Equals(
            "docs/AuraCombatAI/combat-knowledge.base-game.report.json",
            [StringComparison]::OrdinalIgnoreCase)
}

Push-Location $repoRoot
try {
    $trackedPaths = @(& git -c core.quotepath=false ls-files)
    Assert-True ($LASTEXITCODE -eq 0) "Unable to enumerate tracked files."

    $legacyPaths = @($trackedPaths | Where-Object { $_ -match [regex]::Escape($legacyToken) })
    Assert-True ($legacyPaths.Count -eq 0) ("Legacy brand remains in tracked paths:`n" + ($legacyPaths -join "`n"))

    $legacyText = @(& git -c core.quotepath=false grep -I -i -n -- $legacyToken -- `
        . `
        ':(exclude)artifacts/**' `
        ':(exclude)tmp/**' `
        ':(exclude)开发参考资料/**' `
        ':(exclude)docs/AuraCombatAI/combat-knowledge.base-game.report.json' 2>$null)
    $grepExitCode = $LASTEXITCODE
    Assert-True ($grepExitCode -in @(0, 1)) "git grep failed while checking legacy brand text."
    Assert-True ($legacyText.Count -eq 0) ("Legacy brand remains in tracked text:`n" + ($legacyText -join "`n"))

    $textExtensions = @(
        ".asset", ".cs", ".csproj", ".csv", ".json", ".mat", ".md",
        ".meta", ".ps1", ".shader", ".txt", ".yaml", ".yml"
    )
    $base64Pattern = '(?<![A-Za-z0-9+/])(?:[A-Za-z0-9+/]{4}){2,}(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?(?![A-Za-z0-9+/])'
    $legacyBase64Locations = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $trackedPaths) {
        if (-not (Test-BrandingTextPath $relativePath)) {
            continue
        }
        $extension = [IO.Path]::GetExtension($relativePath)
        if ($extension -notin $textExtensions) {
            continue
        }

        $fullPath = Join-Path $repoRoot $relativePath
        if (-not [IO.File]::Exists($fullPath)) {
            continue
        }

        $text = [IO.File]::ReadAllText($fullPath)
        foreach ($match in [regex]::Matches($text, $base64Pattern)) {
            try {
                $decoded = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($match.Value))
                if ($decoded.IndexOf($legacyToken, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    $legacyBase64Locations.Add($relativePath)
                    break
                }
            }
            catch [FormatException] {
                continue
            }
        }
    }
    Assert-True ($legacyBase64Locations.Count -eq 0) ("Legacy brand remains in Base64 source literals:`n" + ($legacyBase64Locations -join "`n"))

    $modConfigPath = Join-Path $repoRoot "Terrias\ModConfig.json"
    $modConfig = Get-Content -LiteralPath $modConfigPath -Raw | ConvertFrom-Json
    Assert-True ($modConfig.ModName -eq "Terrias") "ModConfig ModName must be Terrias."
    Assert-True ($modConfig.ModAuthor -eq "Aura") "ModConfig ModAuthor must remain Aura."
    Assert-True ($modConfig.ModVersion -eq "0.5.4") "ModConfig ModVersion must be 0.5.4."
    Assert-True ($modConfig.PublishedFileId -eq $expectedWorkshopId) "ModConfig must preserve the existing Workshop item."

    $modProjectId = (Get-Content -LiteralPath (Join-Path $repoRoot "Terrias\Terrias.modproj") -Raw).Trim()
    Assert-True ($modProjectId -eq $expectedWorkshopId) "Terrias.modproj must preserve the existing Workshop item."

    [xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot "Terrias-Dev\Terrias.Dll.csproj") -Raw
    $assemblyName = @($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ })[0]
    $rootNamespace = @($project.Project.PropertyGroup.RootNamespace | Where-Object { $_ })[0]
    Assert-True ($assemblyName -eq "Terrias.Aura") "C# assembly name must be Terrias.Aura."
    Assert-True ($rootNamespace -eq "Terrias.Dll") "C# root namespace must be Terrias.Dll."

    $entryDllPath = Join-Path $repoRoot "Terrias\Scripts\Entry.dll"
    $shippedAssemblyName = [Reflection.AssemblyName]::GetAssemblyName($entryDllPath).Name
    Assert-True ($shippedAssemblyName -eq "Terrias.Aura") "Shipped Entry.dll must contain the Terrias.Aura assembly."

    $binaryPaths = @(
        $entryDllPath,
        (Join-Path $repoRoot "Terrias\Scripts\Aura.Shared.dll"),
        (Join-Path $repoRoot "Terrias\ModResource\VisualBundles\terrias_visuals")
    )
    $legacyBytePatterns = @(
        [Text.Encoding]::ASCII.GetBytes($legacyToken),
        [Text.Encoding]::Unicode.GetBytes($legacyToken),
        [Text.Encoding]::ASCII.GetBytes($legacyToken.ToLowerInvariant()),
        [Text.Encoding]::Unicode.GetBytes($legacyToken.ToLowerInvariant())
    )
    foreach ($binaryPath in $binaryPaths) {
        Assert-True ([IO.File]::Exists($binaryPath)) "Required shipped binary is missing: $binaryPath"
        $bytes = [IO.File]::ReadAllBytes($binaryPath)
        foreach ($pattern in $legacyBytePatterns) {
            Assert-True (-not (Test-ByteSequence -Bytes $bytes -Sequence $pattern)) "Legacy brand remains in shipped binary: $binaryPath"
        }
    }

    $genericCsvPaths = @($trackedPaths | Where-Object { $_ -match '^Terrias/(?:Data|Text)/.+/terrias\.csv$' })
    Assert-True ($genericCsvPaths.Count -eq 30) "Expected 30 Terrias generic Data/Text CSV bridge files."

    Write-Host "Terrias branding gate passed: paths, text, Base64 literals, identities, Workshop association, CSV bridges, and shipped binaries are clean."
    $global:LASTEXITCODE = 0
}
finally {
    Pop-Location
}
