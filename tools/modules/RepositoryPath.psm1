Set-StrictMode -Version Latest

function Get-RepositoryRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$RepoRoot,
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Path
    )

    $separators = [char[]]@(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $root = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd($separators)
    if ([string]::IsNullOrEmpty($root)) {
        $root = [System.IO.Path]::DirectorySeparatorChar.ToString()
    }
    elseif ($root.Length -eq 2 -and $root[1] -eq ':') {
        $root += [System.IO.Path]::DirectorySeparatorChar
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $comparison = if ([System.IO.Path]::DirectorySeparatorChar -eq '\') {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }

    if ($resolved.Equals($root, $comparison)) {
        return "."
    }

    $separator = [System.IO.Path]::DirectorySeparatorChar.ToString()
    $rootPrefix = if ($root.EndsWith($separator)) { $root } else { $root + $separator }
    if (-not $resolved.StartsWith($rootPrefix, $comparison)) {
        throw "Path escapes the repository: $Path"
    }

    return $resolved.Substring($rootPrefix.Length).Replace('\', '/')
}

Export-ModuleMember -Function Get-RepositoryRelativePath
