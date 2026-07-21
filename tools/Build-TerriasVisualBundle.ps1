param(
    [string]$UnityPath = "",
    [string]$UnityProjectPath = "",
    [switch]$RequireBundle
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$bundlePath = Join-Path $repoRoot "SunExp\ModResource\VisualBundles\sunexp_visuals"
$builderSource = Join-Path $repoRoot "SunExp-Dev\VisualAssets\Editor\SunExpVisualBundleBuilder.cs.txt"
$defaultUnityProjectPath = Join-Path $repoRoot "SunExp-Dev\VisualAssets\UnityProject"
$shaderSourceDir = Join-Path $repoRoot "SunExp-Dev\VisualAssets\Shaders"
$auraCgShaderSourceDir = Join-Path $repoRoot "AuraCgShared\VisualAssets\Shaders"
$cgFrameSourceDir = Join-Path $repoRoot "SunExp\SharedResources\CG\WuNa\BlazingCrownCollapse"

function Find-UnityEditor {
    if (-not [string]::IsNullOrWhiteSpace($UnityPath)) {
        return $UnityPath
    }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor",
        "C:\Program Files\Unity",
        "C:\Program Files (x86)\Unity",
        "D:\UnityFile",
        "D:\Unity",
        "D:\Program Files\Unity",
        "D:\Program Files\Unity\Hub\Editor"
    )

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        $match = Get-ChildItem -LiteralPath $candidate -Recurse -Filter Unity.exe -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($match) {
            return $match.FullName
        }
    }

    $registryRoots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($root in $registryRoots) {
        $match = Get-ItemProperty $root -ErrorAction SilentlyContinue |
            Where-Object { ($_.DisplayName -like "Unity 20*") -and ($_.DisplayIcon -like "*Unity.exe*") } |
            Sort-Object DisplayVersion -Descending |
            Select-Object -First 1
        if ($match -and (Test-Path -LiteralPath $match.DisplayIcon)) {
            return $match.DisplayIcon
        }
    }

    return ""
}

function Write-Utf8NoBom {
    param(
        [string]$Path,
        [string]$Text
    )

    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Text, $encoding)
}

function Normalize-UnityPathText {
    param(
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    return $Text.Replace("/", "\")
}

function Stop-StaleUnityProjectProcesses {
    param(
        [string]$ProjectPath
    )

    $fullProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
    $normalizedProjectPath = Normalize-UnityPathText $fullProjectPath
    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $commandLine = Normalize-UnityPathText $_.CommandLine
            $_.CommandLine -and
            $commandLine.Contains($normalizedProjectPath) -and
            $_.Name -eq "Unity.exe"
        }

    $batchmodeProcesses = @($processes | Where-Object { $_.CommandLine.Contains("-batchmode") })
    $interactiveProcesses = @($processes | Where-Object { -not $_.CommandLine.Contains("-batchmode") })
    if ($interactiveProcesses.Count -gt 0) {
        $ids = ($interactiveProcesses | ForEach-Object { $_.ProcessId }) -join ", "
        Write-Warning "Unity project is open in interactive Unity process(es): $ids. Close them before building if Unity reports a project lock."
    }

    if ($batchmodeProcesses.Count -gt 0) {
        $idsToStop = New-Object System.Collections.Generic.HashSet[int]
        foreach ($process in $batchmodeProcesses) {
            [void]$idsToStop.Add([int]$process.ProcessId)
            $children = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
                Where-Object {
                    $_.CommandLine -and
                    $_.CommandLine.Contains([string]$process.ProcessId) -and
                    $_.Name -like "Unity*.exe"
                }
            foreach ($child in $children) {
                [void]$idsToStop.Add([int]$child.ProcessId)
            }
        }

        Write-Warning "Stopping stale Unity batchmode process(es) for ${fullProjectPath}: $($idsToStop -join ', ')"
        foreach ($id in $idsToStop) {
            Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        }

        $deadline = (Get-Date).AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 500
            $remaining = @($idsToStop | Where-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
        } while ($remaining.Count -gt 0 -and (Get-Date) -lt $deadline)
    }

    $liveProjectUnity = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $commandLine = Normalize-UnityPathText $_.CommandLine
            $_.CommandLine -and
            $commandLine.Contains($normalizedProjectPath) -and
            $_.Name -eq "Unity.exe"
        })
    $lockPath = Join-Path $fullProjectPath "Temp\UnityLockfile"
    if ($liveProjectUnity.Count -eq 0 -and (Test-Path -LiteralPath $lockPath)) {
        Remove-Item -LiteralPath $lockPath -Force -ErrorAction SilentlyContinue
    }
}

function Wait-ForBundle {
    param(
        [string]$Path,
        [int]$TimeoutSeconds = 12
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $Path) {
            return $true
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Test-UnityLogBuiltBundle {
    param(
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    try {
        $tail = Get-Content -LiteralPath $Path -Tail 80 -ErrorAction Stop
        return ($tail -join "`n").Contains("Built SunExp visual bundle:")
    }
    catch {
        return $false
    }
}

function Prepare-UnityProject {
    param(
        [string]$ProjectPath
    )

    $assetsDir = Join-Path $ProjectPath "Assets"
    $editorDir = Join-Path $assetsDir "Editor"
    $shaderDestDir = Join-Path $assetsDir "SunExp\Visuals\Shaders"
    $cgFrameDestDir = Join-Path $assetsDir "SunExp\Visuals\CG\WuNa\BlazingCrownCollapse"
    $packagesDir = Join-Path $ProjectPath "Packages"
    $projectSettingsDir = Join-Path $ProjectPath "ProjectSettings"

    New-Item -ItemType Directory -Path $editorDir -Force | Out-Null
    New-Item -ItemType Directory -Path $shaderDestDir -Force | Out-Null
    New-Item -ItemType Directory -Path $cgFrameDestDir -Force | Out-Null
    New-Item -ItemType Directory -Path $packagesDir -Force | Out-Null
    New-Item -ItemType Directory -Path $projectSettingsDir -Force | Out-Null

    Copy-Item -LiteralPath (Join-Path $shaderSourceDir "StarScoreHud.shader") -Destination (Join-Path $shaderDestDir "StarScoreHud.shader") -Force
    Copy-Item -LiteralPath (Join-Path $shaderSourceDir "WunaOrbitFire.shader") -Destination (Join-Path $shaderDestDir "WunaOrbitFire.shader") -Force
    Copy-Item -LiteralPath (Join-Path $shaderSourceDir "CardFaceEffect.shader") -Destination (Join-Path $shaderDestDir "CardFaceEffect.shader") -Force
    Copy-Item -LiteralPath (Join-Path $auraCgShaderSourceDir "AuraCgLumaKeyUI.shader") -Destination (Join-Path $shaderDestDir "AuraCgLumaKeyUI.shader") -Force
    Copy-Item -LiteralPath (Join-Path $auraCgShaderSourceDir "AuraCgMaskedInvertFlash.shader") -Destination (Join-Path $shaderDestDir "AuraCgMaskedInvertFlash.shader") -Force
    Copy-Item -LiteralPath (Join-Path $auraCgShaderSourceDir "AuraCgScreenBwFlash.shader") -Destination (Join-Path $shaderDestDir "AuraCgScreenBwFlash.shader") -Force
    if (Test-Path -LiteralPath $cgFrameSourceDir) {
        Copy-Item -Path (Join-Path $cgFrameSourceDir "*.png") -Destination $cgFrameDestDir -Force
    }

    Copy-Item -LiteralPath $builderSource -Destination (Join-Path $editorDir "SunExpVisualBundleBuilder.cs") -Force

    $manifestPath = Join-Path $packagesDir "manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        Write-Utf8NoBom $manifestPath @"
{
  "dependencies": {
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.physics2d": "1.0.0",
    "com.unity.modules.uielements": "1.0.0"
  }
}
"@
    }

    $projectVersionPath = Join-Path $projectSettingsDir "ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $projectVersionPath)) {
        Write-Utf8NoBom $projectVersionPath @"
m_EditorVersion: 2022.3.62f3c1
m_EditorVersionWithRevision: 2022.3.62f3c1 (1623fc0bbb97)
"@
    }
}

function Assert-Bundle {
    if (Test-Path -LiteralPath $bundlePath) {
        Write-Host "SunExp visual bundle exists: $bundlePath"
        return
    }

    $message = "SunExp visual bundle is missing: $bundlePath"
    if ($RequireBundle) {
        throw $message
    }

    Write-Warning $message
}

$unity = Find-UnityEditor
if ([string]::IsNullOrWhiteSpace($unity) -or -not (Test-Path -LiteralPath $unity)) {
    Assert-Bundle
    throw "Unity Editor was not found. Pass -UnityPath or build SunExp/ModResource/VisualBundles/sunexp_visuals in a Unity project."
}

if ([string]::IsNullOrWhiteSpace($UnityProjectPath)) {
    $UnityProjectPath = $defaultUnityProjectPath
}

if (-not (Test-Path -LiteralPath $UnityProjectPath)) {
    New-Item -ItemType Directory -Path $UnityProjectPath -Force | Out-Null
}
$project = (Resolve-Path -LiteralPath $UnityProjectPath).Path
Stop-StaleUnityProjectProcesses $project
Prepare-UnityProject $project

$logPath = Join-Path $repoRoot "SunExp-Dev\VisualAssets\sunexp_visuals.unity-build.log"
$arguments = @(
    "-batchmode",
    "-nographics",
    "-quit",
    "-projectPath", $project,
    "-executeMethod", "SunExpVisualBundleBuilder.BuildVisualBundle",
    "-logFile", $logPath
)

$previousBundleOutput = $env:SUNEXP_VISUAL_BUNDLE_OUTPUT
$bundleWriteTimeBefore = if (Test-Path -LiteralPath $bundlePath) { (Get-Item -LiteralPath $bundlePath).LastWriteTimeUtc } else { [DateTime]::MinValue }
try {
    $env:SUNEXP_VISUAL_BUNDLE_OUTPUT = $bundlePath
    $argumentLine = ($arguments | ForEach-Object {
        if ($_ -match '[\s"]') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }) -join ' '
    $unityProcess = Start-Process -FilePath $unity -ArgumentList $argumentLine -WindowStyle Hidden -Wait -PassThru
    $unityExitCode = $unityProcess.ExitCode
}
finally {
    $env:SUNEXP_VISUAL_BUNDLE_OUTPUT = $previousBundleOutput
}

if ([string]::IsNullOrWhiteSpace([string]$unityExitCode) -and
    (Wait-ForBundle $bundlePath 2) -and
    (Get-Item -LiteralPath $bundlePath).LastWriteTimeUtc -gt $bundleWriteTimeBefore) {
    $unityExitCode = 0
}

if ($unityExitCode -ne 0) {
    $bundleUpdated = (Test-Path -LiteralPath $bundlePath) -and (Get-Item -LiteralPath $bundlePath).LastWriteTimeUtc -gt $bundleWriteTimeBefore
    if (($bundleUpdated -and (Wait-ForBundle $bundlePath 2)) -or (Test-UnityLogBuiltBundle $logPath -and $bundleUpdated)) {
        Write-Warning "Unity returned exit code $unityExitCode after producing the visual bundle. See $logPath"
    }
    else {
        throw "Unity visual bundle build failed. See $logPath"
    }
}

Assert-Bundle
