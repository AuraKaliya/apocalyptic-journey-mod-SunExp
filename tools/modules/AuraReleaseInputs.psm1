Set-StrictMode -Version Latest

function Get-AuraReleaseFiles {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $root = [IO.Path]::GetFullPath($RepoRoot).TrimEnd('\','/')
    $files = New-Object 'System.Collections.Generic.List[string]'
    $roots = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object {
        $_.Name -in @('Terrias','AuraToolsExp','Managed','tools') -or $_.Name -match '(?:Shared|SharedCore|SharedRuntime-Dev|-Dev|Tests)$'
    })
    foreach ($directory in $roots) {
        $directories = New-Object 'System.Collections.Generic.Stack[string]'
        $directories.Push($directory.FullName)
        while ($directories.Count -gt 0) {
          $currentDirectory = $directories.Pop()
          foreach ($child in Get-ChildItem -LiteralPath $currentDirectory -Directory) {
            if ($child.Name -in @('bin','obj','Library','Temp','Logs','node_modules','.git','UnderTest')) { continue }
            if ($child.FullName -match '[\\/]VisualAssets[\\/]UnityProject$') { continue }
            if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Release directory is a reparse point: $($child.FullName)" }
            $directories.Push($child.FullName)
          }
          foreach ($file in Get-ChildItem -LiteralPath $currentDirectory -File) {
            $relative = $file.FullName.Substring($root.Length + 1).Replace('\','/')
            if ($relative -match '/(?:bin|obj|Library|Temp|Logs|node_modules|\.git|UnderTest)/' -or $relative -match '/VisualAssets/UnityProject/') { continue }
            if ($relative -match '\.publish-[a-f0-9]+\.(tmp|bak)$' -or $relative -match '^(Terrias|AuraToolsExp)/Scripts/(Entry|Aura.Shared)\.dll$') { continue }
            $package = $relative -match '^(Terrias|AuraToolsExp)/'
            if (-not $package -and $file.Extension -notin @('.cs','.csproj','.props','.targets','.ps1','.psm1','.json','.dll','.txt','.py','.yaml','.yml','.asmdef','.unity','.prefab','.asset','.meta','.csv')) { continue }
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Release input is a reparse point: $relative" }
            $files.Add($relative)
          }
        }
    }
    foreach ($name in @('Directory.Build.props','Directory.Build.targets','global.json','NuGet.config','AGENTS.md','tools/shared-consumers.json')) {
        if (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf) { if (-not $files.Contains($name)) { $files.Add($name) } }
    }
    $files.Sort([StringComparer]::Ordinal)
    return $files.ToArray()
}

function Get-AuraInputFingerprint {
    param([Parameter(Mandatory)][object[]]$Files)
    $lines = @($Files | ForEach-Object { [string]$_.path + "`t" + [string]$_.sha256 })
    $hash = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))).Replace('-','') }
    finally { $hash.Dispose() }
}

function New-AuraReleaseInputSnapshot {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Path)
    $entries = @(foreach ($relative in Get-AuraReleaseFiles $RepoRoot) {
        $file = Join-Path $RepoRoot $relative
        [pscustomobject]@{ path=$relative; bytes=(Get-Item -LiteralPath $file).Length; sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    })
    $snapshot = [ordered]@{ schemaVersion=1; createdUtc=[DateTime]::UtcNow.ToString('O'); fingerprint=(Get-AuraInputFingerprint $entries); files=$entries }
    [IO.Directory]::CreateDirectory((Split-Path -Parent ([IO.Path]::GetFullPath($Path)))) | Out-Null
    [IO.File]::WriteAllText([IO.Path]::GetFullPath($Path), ($snapshot | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    return [pscustomobject]$snapshot
}

function Assert-AuraReleaseInputSnapshot {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)][string]$Path)
    $snapshot = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    $current = @(Get-AuraReleaseFiles $RepoRoot)
    $expected = @($snapshot.files | ForEach-Object { [string]$_.path })
    if (@(Compare-Object $current $expected).Count -gt 0) { throw 'Release input inventory changed. Rebuild and revalidate the current snapshot.' }
    foreach ($entry in $snapshot.files) {
        $pathOnDisk = Join-Path $RepoRoot ([string]$entry.path)
        if ((Get-FileHash -LiteralPath $pathOnDisk -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Release input changed after the snapshot: $($entry.path)"
        }
    }
    if ((Get-AuraInputFingerprint @($snapshot.files)) -ne $snapshot.fingerprint) { throw 'Release snapshot fingerprint is invalid.' }
    return $snapshot
}

function Enter-AuraReleaseLock {
    param([Parameter(Mandatory)][string]$RepoRoot)
    $identity = Get-AuraInputFingerprint @([pscustomobject]@{path=[IO.Path]::GetFullPath($RepoRoot).ToLowerInvariant();sha256=''})
    $mutex = [Threading.Mutex]::new($false, ('Local\AuraRelease-' + $identity))
    try {
        try { $entered = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $entered = $true }
        if (-not $entered) { throw 'Another process owns this repository release/build transaction.' }
        return $mutex
    } catch { $mutex.Dispose(); throw }
}

Export-ModuleMember -Function Get-AuraReleaseFiles,Get-AuraInputFingerprint,New-AuraReleaseInputSnapshot,Assert-AuraReleaseInputSnapshot,Enter-AuraReleaseLock
