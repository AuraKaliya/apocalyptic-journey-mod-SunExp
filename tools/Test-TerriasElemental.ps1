param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "Terrias-Dev.ElementalTests\Terrias-Dev.ElementalTests.csproj"
$dataPath = Join-Path $repoRoot "Terrias\Data\Buff\terrias.csv"
$textPath = Join-Path $repoRoot "Terrias\Text\Buff\terrias.csv"
$runtimePath = Join-Path $repoRoot "Terrias-Dev\Hooks\RuntimeHooks.cs"
$rpcPath = Join-Path $repoRoot "Terrias-Dev\Network\RpcElementalMechanics.cs"

dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Terrias elemental catalog tests failed."
}

$requiredIds = @(
    "element_pyro",
    "element_electro",
    "element_cryo",
    "element_hydro",
    "element_dendro",
    "dendro_core",
    "frozen"
)
$data = Import-Csv -LiteralPath $dataPath
$text = Import-Csv -LiteralPath $textPath
foreach ($id in $requiredIds) {
    if (-not ($data.Id -contains $id)) {
        throw "Missing elemental Buff data row: $id"
    }
    if (-not ($text.Id -contains $id)) {
        throw "Missing elemental Buff text row: $id"
    }
}

$utf8 = [System.Text.Encoding]::UTF8
function Decode-Text([string]$value) {
    return $utf8.GetString([Convert]::FromBase64String($value))
}

$expectedAttachmentDescriptions = @{
    "element_pyro" = @((Decode-Text "54Gr5YWD57Sg6ZmE552A44CC"), (Decode-Text "54Gr5YWD57Sg6ZmE6JGX44CC"), (Decode-Text "54KO5YWD57Sg5LuY552A44CC"), "Pyro aura.")
    "element_electro" = @((Decode-Text "6Zu35YWD57Sg6ZmE552A44CC"), (Decode-Text "6Zu35YWD57Sg6ZmE6JGX44CC"), (Decode-Text "6Zu35YWD57Sg5LuY552A44CC"), "Electro aura.")
    "element_cryo" = @((Decode-Text "5Yaw5YWD57Sg6ZmE552A44CC"), (Decode-Text "5Yaw5YWD57Sg6ZmE6JGX44CC"), (Decode-Text "5rC35YWD57Sg5LuY552A44CC"), "Cryo aura.")
    "element_hydro" = @((Decode-Text "5rC05YWD57Sg6ZmE552A44CC"), (Decode-Text "5rC05YWD57Sg6ZmE6JGX44CC"), (Decode-Text "5rC05YWD57Sg5LuY552A44CC"), "Hydro aura.")
    "element_dendro" = @((Decode-Text "6I2J5YWD57Sg6ZmE552A44CC"), (Decode-Text "6I2J5YWD57Sg6ZmE6JGX44CC"), (Decode-Text "6I2J5YWD57Sg5LuY552A44CC"), "Dendro aura.")
}
foreach ($id in $expectedAttachmentDescriptions.Keys) {
    $row = $text | Where-Object Id -eq $id | Select-Object -First 1
    $actual = @($row.Description, $row.'Description_zh-Hant', $row.Description_ja, $row.Description_en)
    if (($actual -join "|") -ne ($expectedAttachmentDescriptions[$id] -join "|")) {
        throw "Elemental attachment description mismatch: $id"
    }
}

$runtime = [System.IO.File]::ReadAllText($runtimePath)
if (-not $runtime.Contains('ElementalMechanicsRuntime.Initialize(modConfig)')) {
    throw "Elemental mechanics runtime is not registered."
}

$rpc = [System.IO.File]::ReadAllText($rpcPath)
if (-not $rpc.Contains('RpcElementalCrystalCreateRequest : RpcCommandBase, ITerriasServerBoundRpcCommand')) {
    throw "Elemental crystal creation must remain sender-bound."
}
if (-not $rpc.Contains('RpcElementalCrystalClaim : RpcCommandBase, ITerriasServerBoundRpcCommand')) {
    throw "Elemental crystal claim must remain sender-bound."
}

$damageApi = [System.IO.File]::ReadAllText((Join-Path $repoRoot "Terrias-Dev\GameApi\DamageApi.cs"))
if (-not $damageApi.Contains('CreateCardSourceExecutor')) {
    throw "Status-triggered elemental damage must have a configured native source executor path."
}
if (-not $damageApi.Contains('HasNativeDamageIdentity')) {
    throw "Native damage must validate its source data Id."
}

$reactionService = [System.IO.File]::ReadAllText((Join-Path $repoRoot "Terrias-Dev\Mechanics\ElementalReactionService.cs"))
$validateIndex = $reactionService.IndexOf('if (!CanCommit(plan))')
$consumeIndex = $reactionService.IndexOf('CommitConsumedAttachment(plan);')
if ($validateIndex -lt 0 -or $consumeIndex -lt 0 -or $validateIndex -gt $consumeIndex) {
    throw "Elemental resolution must validate its damage source before consuming an attachment."
}

Write-Host "Terrias elemental mechanics assertions passed."
