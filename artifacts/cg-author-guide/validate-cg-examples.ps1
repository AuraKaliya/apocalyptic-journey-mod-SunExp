param([string]$RepoRoot)
$ErrorActionPreference = 'Stop'
$RepoRoot = [IO.Path]::GetFullPath($RepoRoot)
foreach ($relative in @('Managed/Newtonsoft.Json.dll', 'Managed/UnityEngine.CoreModule.dll', 'AuraToolsExp/Scripts/Aura.Shared.dll')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $RepoRoot $relative)) | Out-Null
}
$sample = Join-Path $RepoRoot 'docs/AuraToolsExp/examples/character-mod-cg/SharedResources'
$guide = [IO.File]::ReadAllText((Join-Path $RepoRoot 'docs/AuraToolsExp/character-mod-cg-configuration-guide.zh-CN.md'))
$blocks = [regex]::Matches($guide, '(?s)' + [char]96 + '{3}json\r?\n(.*?)\r?\n' + [char]96 + '{3}')
$files = @('aura.discovery.json', 'aura.registration.json', 'cg.registry.json')
if ($blocks.Count -ne $files.Count) { throw 'Unexpected number of complete JSON examples.' }
for ($i = 0; $i -lt $files.Count; $i++) {
    $fileToken = [Newtonsoft.Json.Linq.JToken]::Parse([IO.File]::ReadAllText((Join-Path $sample $files[$i])))
    $blockToken = [Newtonsoft.Json.Linq.JToken]::Parse($blocks[$i].Groups[1].Value)
    if (-not [Newtonsoft.Json.Linq.JToken]::DeepEquals($fileToken, $blockToken)) { throw ('Manual/template mismatch: ' + $files[$i]) }
}
$resourceManifest = [AuraShared.Core.AuraSharedJson]::Deserialize(
    [IO.File]::ReadAllText((Join-Path $sample 'aura.registration.json')),
    [AuraShared.Core.AuraSharedRegistrationManifestV4])
$resourceManifest.Normalize('MoonlightMod')
$cgManifest = [AuraShared.Core.AuraSharedJson]::Deserialize(
    [IO.File]::ReadAllText((Join-Path $sample 'cg.registry.json')),
    [AuraCg.Shared.AuraCgManifest])
$cgManifest.Normalize('MoonlightMod')
if ($cgManifest.Protocol.MinVersion -gt [AuraCg.Shared.AuraCgRegistryRuntime]::CurrentRegistrySchemaVersion) { throw 'Unsupported CG registry version.' }
$entry = $cgManifest.Entries[0]
$resource = $resourceManifest.Resources[0]
$logicalPath = [AuraShared.Core.AuraSharedResourcePathPolicy]::ResourcePath($resource.Scope, $resourceManifest.OwnerModId, $resource)
if ($logicalPath -ne $entry.Media.Resource -or $logicalPath -ne $entry.Media.FallbackImage) { throw 'Shared path mismatch.' }
if ($entry.OwnerModId -ne $resourceManifest.OwnerModId -or $resource.ScopeId -ne $entry.SubjectIds[0]) { throw 'Owner/subject mismatch.' }
$queryType = [AuraCg.Shared.AuraCgManifest].Assembly.GetType('AuraCg.Shared.AuraCgRegistryQueryService', $true)
$match = $queryType.GetMethod('MatchesSignal', [Reflection.BindingFlags]'Static,Public')
$context = New-Object AuraCg.Shared.AuraCgSignalContext
$context.SignalId = 'aura.role.skill.committed'
$context.SubjectType = 'role'
$context.RoleId = 'MoonlightMod_luna_luna'
$context.SubjectId = $context.RoleId
$context.SkillId = 'MoonlightMod_luna_moon_burst'
if (-not $match.Invoke($null, [object[]]@($entry.psobject.BaseObject, $context.psobject.BaseObject, $true))) { throw 'Expected skill does not match.' }
$context.SkillId = 'MoonlightMod_luna_other_skill'
$context.Facts.Clear()
if ($match.Invoke($null, [object[]]@($entry.psobject.BaseObject, $context.psobject.BaseObject, $true))) { throw 'Wrong skill matched.' }
$context.SkillId = 'MoonlightMod_luna_moon_burst'
$context.RoleId = 'OtherMod_someone'
$context.SubjectId = $context.RoleId
$context.Facts.Clear()
if ($match.Invoke($null, [object[]]@($entry.psobject.BaseObject, $context.psobject.BaseObject, $true))) { throw 'Wrong role matched.' }

$runRoot = Join-Path ([IO.Path]::GetTempPath()) ('AuraCgGuide-' + [Guid]::NewGuid().ToString('N'))
$modRoot = Join-Path $runRoot 'Mod'
$sharedRoot = Join-Path $modRoot 'SharedResources'
[IO.Directory]::CreateDirectory($sharedRoot) | Out-Null
foreach ($name in $files) { [IO.File]::Copy((Join-Path $sample $name), (Join-Path $sharedRoot $name)) }
[IO.File]::WriteAllText((Join-Path $modRoot 'Example.modproj'), '1234567890')
$pngPath = Join-Path $sharedRoot $resource.Source
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($pngPath)) | Out-Null
# Temporary 1x1 PNG fixture: validates installation/path contracts, not visual playback.
[IO.File]::WriteAllBytes($pngPath, [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jL1sAAAAASUVORK5CYII='))
$discovered = [AuraShared.Core.AuraSharedDiscoveryLoader]::Load($modRoot, $true)
if (-not $discovered.Success -or $discovered.Source.Contributions.Count -ne 2) { throw ('Discovery failed: ' + $discovered.Message) }
$storage = New-Object AuraShared.Core.AuraSharedStorageCoordinator (Join-Path $runRoot 'Storage')
try {
    $packages = New-Object AuraShared.Core.AuraSharedPackageCoordinator $storage
    $coordinator = New-Object AuraShared.Core.AuraSharedRegistrationCoordinator ($storage, $packages, 'guide-validation')
    $first = $coordinator.Register('MoonlightMod', $resourceManifest, $sharedRoot)
    if (-not $first.Success -or -not $first.Activated) { throw ('Resource registration failed: ' + $first.Message + ' ' + ($first.Items | ConvertTo-Json -Depth 5)) }
    $resolved = $coordinator.Resolve($logicalPath)
    if (-not $resolved.Success -or -not [IO.File]::Exists($resolved.ResolvedPath)) { throw 'Registered resource did not resolve.' }
    $repeat = $coordinator.Register('MoonlightMod', $resourceManifest, $sharedRoot)
    if (-not $repeat.Success -or $repeat.ContentChanged) { throw 'Repeated registration did not deduplicate.' }
} finally {
    $storage.Dispose()
}
$guideDir = Join-Path $RepoRoot 'docs/AuraToolsExp'
$checkedLinks = 0
foreach ($link in [regex]::Matches($guide, '\]\(([^)]+)\)')) {
    $target = $link.Groups[1].Value
    if ($target -match '^https?://|^#') { continue }
    if (-not (Test-Path -LiteralPath (Join-Path $guideDir $target))) { throw ('Broken document link: ' + $target) }
    $checkedLinks++
}
[pscustomobject]@{
    JsonExamples = $files.Count
    ManualMatchesTemplates = $true
    ProductionDiscovery = $true
    ProductionResourceRegistration = $true
    ProductionResourceResolution = $true
    RepeatDeduplicated = $true
    ExpectedSkillMatches = $true
    WrongSkillRejected = $true
    WrongRoleRejected = $true
    DocumentLinks = $checkedLinks
    TemporaryValidationDirectory = $runRoot
    GamePlaybackTested = $false
} | ConvertTo-Json
