param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$roots = @(
    "AuraSharedCore",
    "AuraCgShared",
    "AudioArbiterShared",
    "AuraToolsExp-Dev",
    "Terrias-Dev"
)

$records = foreach ($root in $roots) {
    $path = Join-Path $repoRoot $root
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "RPC source root is missing: $root"
    }

    foreach ($file in Get-ChildItem -LiteralPath $path -Recurse `
            -Filter "*.cs" -File) {
        if ($file.FullName -match "\\(?:bin|obj)\\") {
            continue
        }

        [pscustomobject]@{
            RelativePath = [IO.Path]::GetRelativePath(
                $repoRoot,
                $file.FullName).Replace("\", "/")
            Text = [IO.File]::ReadAllText($file.FullName)
        }
    }
}

$violations = New-Object System.Collections.Generic.List[string]

$payloadIdentityAuthorizationPatterns = @(
    "(?:IsHostIdentity|LobbyContains)\s*\(\s*(?:command|value|request|candidate)\.(?:Issuer|Reporter|Owner)PlayerId",
    "(?:RequireHost|AuthorizeHost)\s*\(\s*(?:command|value|request|candidate)\.(?:Issuer|Reporter|Owner)PlayerId"
)
foreach ($record in $records) {
    foreach ($pattern in $payloadIdentityAuthorizationPatterns) {
        if ([regex]::IsMatch($record.Text, $pattern)) {
            $violations.Add(
                "$($record.RelativePath): payload identity is used as authority")
        }
    }
}

$transportAllowPatterns = @(
    "^AuraCgShared/AuraCgNetworkRuntime\.cs$",
    "^AudioArbiterShared/AudioNetworkRuntime\.cs$",
    "^AuraToolsExp-Dev/Infrastructure/AuraToolsRpcTransport\.cs$",
    "^Terrias-Dev/Network/"
)
foreach ($record in $records) {
    if (-not [regex]::IsMatch(
            $record.Text,
            "\.SendRpcCommand(?:ExcludeOwner)?\s*\(")) {
        continue
    }

    $allowed = @($transportAllowPatterns | Where-Object {
        $record.RelativePath -match $_
    }).Count -gt 0
    if (-not $allowed) {
        $violations.Add(
            "$($record.RelativePath): raw RPC transport bypasses an approved network adapter")
    }
}

$registeredMarkers = New-Object System.Collections.Generic.HashSet[string](
    [System.StringComparer]::Ordinal)
$commandPattern = [regex](
    "(?m)^\s*(?:public|internal)\s+(?:sealed\s+)?class\s+" +
    "(?<class>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*RpcCommandBase" +
    "(?<bases>[^\r\n]*)\r?\n\s*\{")
foreach ($record in $records) {
    $matches = @($commandPattern.Matches($record.Text))
    for ($index = 0; $index -lt $matches.Count; $index++) {
        $match = $matches[$index]
        $end = if ($index + 1 -lt $matches.Count) {
            $matches[$index + 1].Index
        } else {
            $record.Text.Length
        }
        $body = $record.Text.Substring($match.Index, $end - $match.Index)
        $markerMatch = [regex]::Match(
            $match.Groups["bases"].Value,
            "\b(?<marker>I[A-Za-z0-9_]*ServerBoundRpcCommand)\b")
        $hasServerEntry = [regex]::IsMatch(
            $body,
            "override\s+void\s+CmdExecute\s*\(")

        if ($hasServerEntry -and -not $markerMatch.Success) {
            $violations.Add(
                "$($record.RelativePath): $($match.Groups['class'].Value) exposes CmdExecute without a server-bound marker")
            continue
        }

        if ($markerMatch.Success) {
            [void]$registeredMarkers.Add($markerMatch.Groups["marker"].Value)
        }
    }
}

$authorityRegistrations = @()
$registrationPattern = [regex](
    "(?s)AuraRpcAuthorityRuntime\.Register\s*\(.*?\)\s*;")
foreach ($record in $records) {
    foreach ($registration in $registrationPattern.Matches($record.Text)) {
        $authorityRegistrations += [pscustomobject]@{
            RelativePath = $record.RelativePath
            Body = $registration.Value
        }
    }
}

foreach ($marker in $registeredMarkers) {
    $escapedMarker = [regex]::Escape($marker)
    $predicatePattern = "command\s*=>\s*command\s+is\s+" + $escapedMarker + "\b"
    $registrations = @($authorityRegistrations | Where-Object {
        $_.Body -match $predicatePattern
    })
    if ($registrations.Count -ne 1) {
        $violations.Add(
            "server-bound marker must have exactly one AuraRpcAuthorityRuntime registration: $marker; found=$($registrations.Count)")
        continue
    }

    $binderPattern = (
        "\(\s*\(\s*" + $escapedMarker +
        "\s*\)\s*command\s*\)\s*\.BindServerSender\s*\(")
    if ($registrations[0].Body -notmatch $binderPattern) {
        $violations.Add(
            "$($registrations[0].RelativePath): authority registration for $marker does not bind that marker's sender in the same registration block")
    }
}

if ($violations.Count -gt 0) {
    throw "Network RPC authority scan failed:`n - $($violations -join "`n - ")"
}

Write-Host (
    "Network RPC authority scan passed: files={0}, serverBoundMarkers={1}." -f `
        $records.Count,
        $registeredMarkers.Count)
