param(
    [string]$RepoRoot = "",
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}
$RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

function Read-ProjectJson {
    param([string]$RelativePath)
    Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $RelativePath) | ConvertFrom-Json
}

function Read-PublicIntConstant {
    param([string]$RelativePath, [string]$Symbol)
    $source = Get-Content -Raw -LiteralPath (Join-Path $RepoRoot $RelativePath)
    $pattern = '(?m)^\s*public\s+const\s+int\s+' + [regex]::Escape($Symbol) + '\s*=\s*(\d+)\s*;'
    $matches = [regex]::Matches($source, $pattern)
    if ($matches.Count -ne 1) {
        throw "Expected one public integer contract $Symbol in $RelativePath; review its source declaration."
    }
    [pscustomobject]@{
        symbol = $Symbol
        value = [int]$matches[0].Groups[1].Value
        source = $RelativePath
    }
}

$consumerDocument = Read-ProjectJson "tools/shared-consumers.json"
if ($consumerDocument.schemaVersion -ne 1) {
    throw "Unsupported consumer manifest schema."
}
$validation = @(foreach ($path in @("tools/terrias-test-matrix.json", "tools/shared-release-matrix.json")) {
    $matrix = Read-ProjectJson $path
    if ($matrix.schemaVersion -ne 2) { throw "Unsupported test matrix schema: $path" }
    $steps = @($matrix.steps | Where-Object { $_.enabled -ne $false })
    [pscustomobject]@{
        source = $path
        profiles = @($steps | ForEach-Object { $_.profiles } | Sort-Object -Unique)
        steps = @($steps | Select-Object id, path, owner, category, cost, profiles, impactTags, arguments)
    }
})
$contracts = @(
    Read-PublicIntConstant "AuraCgShared/AuraCgRegistry.cs" "CurrentRegistrySchemaVersion"
    Read-PublicIntConstant "AuraCgShared/AuraCgRuntime.cs" "CurrentProtocolVersion"
    Read-PublicIntConstant "Terrias-Dev/Mechanics/CompanionAuthorityService.cs" "ProjectionProtocolVersion"
)
$referenceRoot = Join-Path $RepoRoot "开发参考资料"
$decompileCandidates = @()
if (Test-Path -LiteralPath $referenceRoot -PathType Container) {
    $decompileCandidates = @(Get-ChildItem -LiteralPath $referenceRoot -Directory |
        Where-Object { $_.Name -match '^反编译文件夹v\d+(\.\d+){1,3}$' } |
        Sort-Object { [version]($_.Name -replace '^反编译文件夹v', '') } -Descending |
        ForEach-Object { "开发参考资料/" + $_.Name })
}
$result = [pscustomobject]@{
    schemaVersion = 1
    consumerSource = "tools/shared-consumers.json"
    consumers = @($consumerDocument.consumers)
    validation = $validation
    sourceContracts = $contracts
    decompileCandidates = $decompileCandidates
    decompileSelection = "Candidates only; match investigated game/Managed fingerprints before using host behavior."
}
if ($AsJson) {
    $result | ConvertTo-Json -Depth 12
}
else {
    Write-Output "Product consumers:"
    $result.consumers | Where-Object { $_.classification -eq "product" } |
        Select-Object id, projectPath, packagePath | Format-Table -AutoSize
    Write-Output "Current source contracts:"
    $contracts | Format-Table -AutoSize
    foreach ($matrix in $validation) {
        Write-Output ("{0}: {1} enabled steps; profiles: {2}" -f $matrix.source, $matrix.steps.Count, ($matrix.profiles -join ", "))
    }
    Write-Output "Decompile candidates (fingerprint match still required):"
    $decompileCandidates | Write-Output
}
