param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$witchHash = (Get-FileHash -LiteralPath (Join-Path $root 'Managed/Witch.dll')).Hash
$catalogs = @(Get-ChildItem -LiteralPath (Join-Path $root 'artifacts/game-reference') -Filter 'official-roles.json' -Recurse -File |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 | ConvertFrom-Json } |
    Where-Object witchSha256 -eq $witchHash)
if ($catalogs.Count -ne 1) { throw 'Export exactly one official role catalog matching the current Managed/Witch.dll.' }
$roles = @($catalogs[0].roles)
if ($roles.Count -eq 0 -or @($roles | Group-Object id | Where-Object Count -ne 1).Count -gt 0) {
    throw 'Official role catalog is empty or has duplicate identities.'
}
$sharedRoot = Join-Path $root 'AuraToolsExp/SharedResources'
$registry = Get-Content -LiteralPath (Join-Path $sharedRoot 'cg.registry.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$registration = Get-Content -LiteralPath (Join-Path $sharedRoot 'aura.registration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (@($registry.entries | Group-Object cgId | Where-Object Count -ne 1).Count -gt 0) { throw 'Duplicate CG identity.' }
$entries = @($registry.entries | Where-Object {
    $_.subjectType -eq 'role' -and $_.tags -contains 'official-role' -and $_.signals -contains 'aura.role.feast.completed'
})
foreach ($role in $roles) {
    $matches = @($entries | Where-Object { $_.subjectIds -contains $role.id })
    if ($matches.Count -ne 1) { throw "Expected exactly one feast CG for $($role.id) ($($role.name)); found $($matches.Count)." }
    $entry = $matches[0]
    if (-not $entry.enabled -or -not $entry.defaultActivation.enabled -or $entry.defaultActivation.consumerMode -ne 'toolManaged' -or $entry.defaultActivation.consumerModId -ne 'AuraToolsExp' -or $entry.media.type -ne 'image') {
        throw "Official feast activation is invalid: $($entry.cgId)"
    }
    $resources = @($registration.resources | Where-Object {
        $_.moduleId -eq 'CG' -and $_.featureId -eq 'Feast' -and $_.scopeType -eq 'Role' -and $_.scopeOwnerModId -eq 'Witch' -and
        ($_.scopeId -eq $role.id -or $_.scopeAliases -contains $role.id)
    })
    if ($resources.Count -ne 1) { throw "Official feast resource/alias missing or ambiguous: $($role.id)" }
    $resource = $resources[0]
    $logical = "CG/Role/$($resource.scopeId)/Feast/AuraToolsExp/$($resource.resourceId)/$($resource.fileName)"
    if ($entry.media.resource -ne $logical -or $entry.media.fallbackImage -ne $logical) { throw "Feast resource does not resolve: $($entry.cgId)" }
    $path = [IO.Path]::GetFullPath((Join-Path $sharedRoot $resource.source))
    if (-not $path.StartsWith([IO.Path]::GetFullPath($sharedRoot) + [IO.Path]::DirectorySeparatorChar) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Feast image missing or outside resource root: $path"
    }
    if ((Get-Item -LiteralPath $path).Length -lt 1024) { throw "Feast image is empty: $path" }
}
foreach ($entry in $entries) {
    foreach ($id in $entry.subjectIds) {
        if ($roles.id -notcontains $id) { throw "Unknown official role in feast registry: $id" }
    }
}
Write-Host "Official feast coverage passed: $($roles.Count) career identities, $($entries.Count) CG images, $($catalogs[0].runtimeVersion)."
