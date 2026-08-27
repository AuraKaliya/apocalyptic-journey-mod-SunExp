param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "AuraToolsExp-Dev.Tests\AuraToolsExp-Dev.Tests.csproj"
$skinModule = Join-Path $repoRoot "tools\modules\SkinPackageValidation.psm1"
$bundledModelIntegration = Join-Path $repoRoot (
    "tools\Test-AuraToolsBundledModelRegistrationIntegration.ps1")

if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "AuraToolsExp behavior test project is missing: $project"
}

$modConfig = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\ModConfig.json") | ConvertFrom-Json
if ([string]$modConfig.ModVersion -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$' `
        -or $modConfig.MustSame -ne $false) {
    throw "AuraToolsExp must use semantic release metadata without a global exact-version lobby gate."
}

$protocolManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\protocol.compatibility.json") |
    ConvertFrom-Json
$requiredProtocolFeatures = @{
    "multiplayer.mod-sync" = @(1, 2)
    "presentation.pixel-emoji" = @(2, 2)
    "records.damage-meter" = @(4, 4)
    "presentation.event-cg" = @(1, 1)
    "records.match-replay" = @(12, 12)
}
if ($protocolManifest.schemaVersion -ne 1 `
        -or $protocolManifest.releaseBaseline -ne $modConfig.ModVersion `
        -or $protocolManifest.globalExactModVersionRequired -ne $false) {
    throw "AuraToolsExp protocol inventory must disable the global exact-version gate."
}
foreach ($entry in $requiredProtocolFeatures.GetEnumerator()) {
    $feature = @($protocolManifest.features | Where-Object id -eq $entry.Key)
    if ($feature.Count -ne 1 `
            -or [int]$feature[0].minimumSupportedVersion -ne $entry.Value[0] `
            -or [int]$feature[0].currentVersion -ne $entry.Value[1] `
            -or @($feature[0].requiredCapabilities).Count -eq 0 `
            -or [string]::IsNullOrWhiteSpace([string]$feature[0].fallback)) {
        throw "AuraToolsExp protocol inventory is incomplete or invalid: $($entry.Key)"
    }
}
$damageProtocol = @($protocolManifest.features | Where-Object id -eq "records.damage-meter")[0]
if ([int]$damageProtocol.minimumReadableVersion -ne 3) {
    throw "AuraToolsExp damage-meter protocol inventory must distinguish v4 live networking from v3 persisted-data migration."
}
$replayProtocol = @($protocolManifest.features | Where-Object id -eq "records.match-replay")[0]
$requiredReplayCapabilities = @(
    "causal-transactions.v1",
    "authoritative-public-state.v1",
    "dual-journal-lane.v1",
    "full-checkpoints.v1",
    "portable-presentation.v1",
    "independent-replay-scene.v1",
    "embedded-assets.v1")
if (@(Compare-Object @($replayProtocol.requiredCapabilities) $requiredReplayCapabilities).Count -ne 0 `
        -or $replayProtocol.fallback -ne "reject-structured-replay-and-retain-summary-analysis-verified-mp4") {
    throw "AuraToolsExp match-replay v12 protocol capabilities or fallback are incomplete."
}

$ffmpegRoot = Join-Path $repoRoot "AuraToolsExp\Runtime\ffmpeg\win-x64"
$ffmpegManifestPath = Join-Path $ffmpegRoot "manifest.json"
$ffmpegPath = Join-Path $ffmpegRoot "ffmpeg.exe"
$ffprobePath = Join-Path $ffmpegRoot "ffprobe.exe"
$ffmpegLicensePath = Join-Path $ffmpegRoot "LICENSE.txt"
$ffmpegNoticePath = Join-Path $ffmpegRoot "NOTICE.md"
foreach ($required in @($ffmpegManifestPath, $ffmpegPath, $ffprobePath, $ffmpegLicensePath, $ffmpegNoticePath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "AuraToolsExp controlled FFmpeg runtime is incomplete: $required"
    }
}
$ffmpegManifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $ffmpegManifestPath | ConvertFrom-Json
if ([int]$ffmpegManifest.schemaVersion -ne 2 `
        -or $ffmpegManifest.version -ne "9.0.1-aura-minimal.1" `
        -or $ffmpegManifest.platform -ne "win-x64" `
        -or $ffmpegManifest.license -ne "LGPL-3.0-or-later" `
        -or $ffmpegManifest.buildProfile -ne "aura-replay-win-x64-minimal-shared.v1" `
        -or $ffmpegManifest.codecProfile -ne "mp4-mpeg4-aac-bt709.v1" `
        -or $ffmpegManifest.sourceRevision -ne "9d4ca21220" `
        -or $ffmpegManifest.sourceArchiveSha256 -ne "6d9f8e49d1b6c561b6abb007fa872e844be181fb2516fd6e77741d0dd838bfa4" `
        -or [long]$ffmpegManifest.sourceDateEpoch -ne 1786990956) {
    throw "AuraToolsExp controlled FFmpeg manifest metadata is invalid."
}
$declaredPayload = @($ffmpegManifest.files)
$actualPayload = @(Get-ChildItem -LiteralPath $ffmpegRoot -File |
    Where-Object { $_.Extension -in @(".exe", ".dll") } |
    Sort-Object Name)
if ($declaredPayload.Count -ne $actualPayload.Count `
        -or $actualPayload.Count -ne 8 `
        -or @($actualPayload | Where-Object Name -eq "ffplay.exe").Count -ne 0) {
    throw "AuraToolsExp FFmpeg shared payload is incomplete or contains an unshipped program."
}
foreach ($file in $actualPayload) {
    $entry = @($declaredPayload | Where-Object path -eq $file.Name)
    if ($entry.Count -ne 1 `
            -or [long]$entry[0].bytes -ne $file.Length `
            -or (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash -ne $entry[0].sha256) {
        throw "AuraToolsExp FFmpeg payload hash or size is invalid: $($file.Name)"
    }
}
$runtimeBytes = (Get-ChildItem -LiteralPath $ffmpegRoot -File | Measure-Object Length -Sum).Sum
if ($runtimeBytes -gt 40MB) {
    throw "AuraToolsExp FFmpeg runtime exceeds its 40 MiB release budget: $runtimeBytes bytes."
}
$buildConfiguration = & $ffmpegPath -hide_banner -buildconf 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 `
        -or $buildConfiguration -notmatch '--disable-everything' `
        -or $buildConfiguration -notmatch '--disable-autodetect' `
        -or $buildConfiguration -notmatch '--disable-network' `
        -or $buildConfiguration -notmatch '--disable-static' `
        -or $buildConfiguration -notmatch '--enable-shared' `
        -or $buildConfiguration -notmatch '--enable-version3' `
        -or $buildConfiguration -match '--enable-(?:gpl|nonfree)') {
    throw "AuraToolsExp FFmpeg build is not the feature-bounded shared LGPL profile."
}
$encoderInventory = & $ffmpegPath -hide_banner -encoders 2>$null | Out-String
if ($LASTEXITCODE -ne 0 `
        -or $encoderInventory -notmatch '(?m)^ V..... mpeg4\s' `
        -or $encoderInventory -notmatch '(?m)^ A..... aac\s') {
    throw "AuraToolsExp controlled FFmpeg build lacks the fixed MP4 profile encoders."
}
$decoderInventory = & $ffmpegPath -hide_banner -decoders 2>$null | Out-String
foreach ($decoder in @("aac", "alac", "av1", "flac", "h264", "hevc", "mjpeg", "mpeg4", "mp3", "opus", "pcm_f32le", "pcm_s16le", "pcm_s24le", "pcm_s32le", "rawvideo", "vorbis", "vp8", "vp9")) {
    if ($decoderInventory -notmatch "(?m)^ [VAS].{5} $([regex]::Escape($decoder))\s") {
        throw "AuraToolsExp FFmpeg build lacks a declared source decoder: $decoder"
    }
}
$formatInventory = & $ffmpegPath -hide_banner -formats 2>$null | Out-String
foreach ($format in @("avi", "matroska", "mov", "rawvideo", "wav")) {
    if ($formatInventory -notmatch "(?m)^ D.\s+[^\r\n]*\b$([regex]::Escape($format))(?:,|\s)") {
        throw "AuraToolsExp FFmpeg build lacks a declared source demuxer: $format"
    }
}
$protocolInventory = & $ffmpegPath -hide_banner -protocols 2>$null | Out-String
if ($protocolInventory -notmatch '(?m)^\s*file\s*$' -or $protocolInventory -notmatch '(?m)^\s*pipe\s*$') {
    throw "AuraToolsExp FFmpeg build lacks the controlled file or pipe protocols."
}

$ffmpegSmokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    "AuraTools-ReplayFfmpeg-" + [guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($ffmpegSmokeRoot) | Out-Null
$rawVideo = Join-Path $ffmpegSmokeRoot "frames.rgb24"
$waveAudio = Join-Path $ffmpegSmokeRoot "audio.wav"
$ffmpegSmoke = Join-Path $ffmpegSmokeRoot "smoke.mp4"
$normalizedSmoke = Join-Path $ffmpegSmokeRoot "normalized.mp4"
try {
    [IO.File]::WriteAllBytes($rawVideo, [byte[]]::new(64 * 64 * 3 * 6))
    $waveStream = [IO.File]::Create($waveAudio)
    try {
        $waveWriter = [IO.BinaryWriter]::new($waveStream)
        $sampleFrames = 9600
        $dataBytes = $sampleFrames * 2 * 2
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes("RIFF"))
        $waveWriter.Write(36 + $dataBytes)
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes("WAVEfmt "))
        $waveWriter.Write(16)
        $waveWriter.Write([int16]1)
        $waveWriter.Write([int16]2)
        $waveWriter.Write(48000)
        $waveWriter.Write(48000 * 2 * 2)
        $waveWriter.Write([int16]4)
        $waveWriter.Write([int16]16)
        $waveWriter.Write([Text.Encoding]::ASCII.GetBytes("data"))
        $waveWriter.Write($dataBytes)
        $waveWriter.Write([byte[]]::new($dataBytes))
        $waveWriter.Flush()
    }
    finally {
        $waveStream.Dispose()
    }
    & $ffmpegPath -hide_banner -loglevel error -nostdin -y `
        -f rawvideo -pixel_format rgb24 -video_size 64x64 -framerate 30 -i $rawVideo `
        -i $waveAudio `
        -shortest -vf "vflip,scale=in_range=pc:out_range=tv:out_color_matrix=bt709,format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709" `
        -c:v mpeg4 -q:v 3 -pix_fmt yuv420p -c:a aac -b:a 160k `
        -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 `
        -movflags +faststart+write_colr -f mp4 $ffmpegSmoke
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $ffmpegSmoke -PathType Leaf)) {
        throw "AuraToolsExp controlled FFmpeg smoke encode failed."
    }
    $smokeProbe = & $ffprobePath -v error -count_frames `
        -show_entries "stream=codec_type,codec_name,pix_fmt,color_range,color_space,color_transfer,color_primaries,sample_rate,channels,nb_read_frames" `
        -of json $ffmpegSmoke | Out-String | ConvertFrom-Json
    $smokeVideo = @($smokeProbe.streams | Where-Object codec_type -eq "video")
    $smokeAudio = @($smokeProbe.streams | Where-Object codec_type -eq "audio")
    if ($smokeVideo.Count -ne 1 -or $smokeAudio.Count -ne 1 `
            -or $smokeVideo[0].codec_name -ne "mpeg4" `
            -or $smokeVideo[0].pix_fmt -ne "yuv420p" `
            -or $smokeVideo[0].color_range -ne "tv" `
            -or $smokeVideo[0].color_space -ne "bt709" `
            -or $smokeVideo[0].color_transfer -ne "bt709" `
            -or $smokeVideo[0].color_primaries -ne "bt709" `
            -or $smokeAudio[0].codec_name -ne "aac" `
            -or [int]$smokeAudio[0].sample_rate -ne 48000 `
            -or [int]$smokeAudio[0].channels -ne 2) {
        throw "AuraToolsExp controlled FFmpeg smoke output does not match the fixed MP4 profile."
    }
    & $ffmpegPath -hide_banner -v error -i $ffmpegSmoke -f null NUL
    if ($LASTEXITCODE -ne 0) {
        throw "AuraToolsExp controlled FFmpeg smoke output failed complete decode."
    }
    & $ffmpegPath -hide_banner -loglevel error -nostdin -y -i $ffmpegSmoke `
        -map "0:v:0" -map "0:a:0?" -sn -dn -fps_mode cfr `
        -vf "fps=30,scale=trunc(iw/2)*2:trunc(ih/2)*2:in_range=auto:out_range=tv:out_color_matrix=bt709,format=yuv420p,setparams=range=limited:color_primaries=bt709:color_trc=bt709:colorspace=bt709" `
        -c:v mpeg4 -q:v 3 -pix_fmt yuv420p -c:a aac -b:a 160k -ar 48000 -ac 2 `
        -color_range tv -colorspace bt709 -color_primaries bt709 -color_trc bt709 `
        -movflags +faststart+write_colr -f mp4 $normalizedSmoke
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $normalizedSmoke -PathType Leaf)) {
        throw "AuraToolsExp controlled FFmpeg normalization smoke failed."
    }
    $normalizedProbe = & $ffprobePath -v error `
        -show_entries "stream=codec_type,codec_name,r_frame_rate,pix_fmt,color_range,color_space,color_transfer,color_primaries,sample_rate,channels" `
        -of json $normalizedSmoke | Out-String | ConvertFrom-Json
    $normalizedVideo = @($normalizedProbe.streams | Where-Object codec_type -eq "video")
    $normalizedAudio = @($normalizedProbe.streams | Where-Object codec_type -eq "audio")
    if ($LASTEXITCODE -ne 0 `
            -or $normalizedVideo.Count -ne 1 -or $normalizedAudio.Count -ne 1 `
            -or $normalizedVideo[0].codec_name -ne "mpeg4" `
            -or $normalizedVideo[0].r_frame_rate -ne "30/1" `
            -or $normalizedVideo[0].pix_fmt -ne "yuv420p" `
            -or $normalizedVideo[0].color_range -ne "tv" `
            -or $normalizedVideo[0].color_space -ne "bt709" `
            -or $normalizedVideo[0].color_transfer -ne "bt709" `
            -or $normalizedVideo[0].color_primaries -ne "bt709" `
            -or $normalizedAudio[0].codec_name -ne "aac" `
            -or [int]$normalizedAudio[0].sample_rate -ne 48000 `
            -or [int]$normalizedAudio[0].channels -ne 2) {
        throw "AuraToolsExp normalized import smoke does not match the fixed MP4 profile."
    }
    & $ffmpegPath -hide_banner -v error -i $normalizedSmoke -f null NUL
    if ($LASTEXITCODE -ne 0) {
        throw "AuraToolsExp normalized import smoke failed complete decode."
    }
}
finally {
    if ([IO.Directory]::Exists($ffmpegSmokeRoot)) {
        [IO.Directory]::Delete($ffmpegSmokeRoot, $true)
    }
}

$replaySources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features\MatchRecords") -Recurse -File -Filter "*.cs")
$replayText = ($replaySources | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
if ($replayText -match 'ReplayDocumentV11|ReplayProtocolV11|SaveV11|LoadV11|MatchReplayFightSandboxInitializer|ReplayNativeDocumentAdapter|ReplayNativeViewRuntime|MatchReplayNativePresentationApi|ScreenCapture\.CaptureScreenshotAsTexture|StartLocalHost|RpcLoadRoles|ReplayTimelineController|native-or-silence|MjpegAviWriter|ReplayFrameSpool|GetEnvironmentVariable\("PATH"\)|falling back to built-in AVI' `
        -or $replayText -match '\bTerrias\b' `
        -or (Test-Path -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\GameApi\MatchReplayNativePresentationApi.cs")) `
        -or (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsMatchRecordSettings.cs")) -match 'PreferMp4|FfmpegPath' `
        -or (Get-Content -Raw -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\AuraToolsExp.Dll.csproj")) -match 'UnityEngine\.ScreenCaptureModule') {
    throw "AuraToolsExp v12 release still contains a retired v11/native-player, AVI, screenshot, PATH FFmpeg, or silent-audio path."
}
$portablePlaybackText = (@(Get-ChildItem -LiteralPath (
            Join-Path $repoRoot "AuraToolsExp-Dev\Features\MatchRecords\ReplayV12\Playback") -File -Filter "*.cs") |
        ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"
$playerText = Get-Content -Raw -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\MatchRecords\Playback\MatchReplayPlayer.cs")
if ($portablePlaybackText -match '\bFightManager\b|\bFightUI\b|\bRoleTable\b|\bIScriptExecutor\b|using\s+Witch(?:\.Core)?\s*;' `
        -or $playerText -match '\bFightManager\b|\bFightUI\b|MatchReplayEnvironmentScope|MatchReplayFightSandboxInitializer' `
        -or $playerText -notmatch 'ReplaySceneRuntime' `
        -or $replayText -notmatch 'ReplayNetworkAuthorityV12' `
        -or $replayText -notmatch 'DeclaredDocumentRoot' `
        -or $replayText -notmatch 'TruthRoot' `
        -or $replayText -notmatch 'PresentationRoot') {
    throw "AuraToolsExp portable replay boundary, network authority, or canonical-root contract is invalid."
}

& dotnet run --project $project -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp behavior tests failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $bundledModelIntegration -PathType Leaf)) {
    throw "AuraToolsExp bundled-model integration test is missing: $bundledModelIntegration"
}
& powershell `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File $bundledModelIntegration `
    -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "AuraToolsExp bundled-model integration failed with exit code $LASTEXITCODE."
}

Import-Module $skinModule -Force
$skinValidation = Test-SkinPackageContent -PackagePath (
    Join-Path $repoRoot "AuraToolsExp\SharedResources\Skins\package.json")
if ($skinValidation.Package.packageId -ne "AuraToolsExp.BundledSkins" `
        -or $skinValidation.ParticipantKind -ne "Tool") {
    throw "AuraToolsExp bundled skin package identity or Tool ownership is invalid."
}

$officialSummerSkins = @($skinValidation.Skins | Where-Object {
    $_.TargetCareerId -eq "career_1" `
        -and $_.SkinId -eq "AuraToolsExp.career_1.summer_cool"
})
if ($officialSummerSkins.Count -ne 1) {
    throw "AuraToolsExp must publish its official career_1 summer skin exactly once."
}
$terriasSkins = @($skinValidation.Skins | Where-Object {
    ($_.TargetCareerId -eq "Terrias_wuna_wuna" -and $_.SkinId -eq "AuraToolsExp.Terrias_wuna_wuna.summer_cool") `
        -or ($_.TargetCareerId -eq "Terrias_columbina_columbina" -and $_.SkinId -eq "AuraToolsExp.Terrias_columbina_columbina.restore_colors")
})
if ($terriasSkins.Count -ne 2) {
    throw "AuraToolsExp must own both currently bundled Terrias replacement skins."
}

$matchSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\MatchExperienceSettings.json") | ConvertFrom-Json
$standardPreset = @($matchSettings.autoBattle.gameParameters.presets | Where-Object id -eq "standard")
if ($matchSettings.schemaVersion -ne 32 `
        -or $matchSettings.starterDeck.schemaVersion -ne 2 `
        -or $matchSettings.starterDeck.globalProfile.cardIds.Count -ne 0 `
        -or $matchSettings.starterDeck.globalProfile.relicIds.Count -ne 0 `
        -or $matchSettings.starterDeck.PSObject.Properties.Name -contains "selectedProfileByRole" `
        -or $matchSettings.starterDeck.PSObject.Properties.Name -contains "preferRoleModProfile" `
        -or $matchSettings.feast.schemaVersion -ne 2 `
        -or $matchSettings.feast.cg.schemaVersion -ne 1 `
        -or $matchSettings.feast.enabled -ne $true `
        -or $matchSettings.feast.cg.enabled -ne $true `
        -or $matchSettings.feast.PSObject.Properties.Name -contains "playCg" `
        -or @($matchSettings.autoBattle.modelRiskAcknowledgements).Count -ne 0 `
        -or $matchSettings.autoBattle.PSObject.Properties.Name -contains "experimentalModelAcknowledgement" `
        -or $matchSettings.cardRefresh.enabled -ne $false `
        -or $matchSettings.autoBattle.enabled -ne $false `
        -or $matchSettings.autoBattle.training.preset -ne "steady" `
        -or $matchSettings.autoBattle.simulation.scenarioId -ne "witch.world-simulation.standard-v2" `
        -or $matchSettings.autoBattle.simulation.difficultyId -ne "normal" `
        -or $standardPreset.Count -ne 1 `
        -or $standardPreset[0].partnerId -ne "Partner_10001" `
        -or $standardPreset[0].preferredDeckSizeMinimum -ne 15) {
    throw "AuraToolsExp match-experience configuration contract is invalid."
}
if ($matchSettings.matchRecords.enabled -ne $false `
        -or $matchSettings.matchRecords.statistics.enabled -ne $true `
        -or $matchSettings.matchRecords.statistics.displayMode -ne "Table" `
        -or $matchSettings.matchRecords.statistics.displayScope -ne "Fight" `
        -or $matchSettings.matchRecords.statistics.teamFilter -ne "All" `
        -or $matchSettings.matchRecords.statistics.captureTeamAvatars -ne $false `
        -or $matchSettings.matchRecords.statistics.uiRefreshIntervalMs -ne 1000 `
        -or $matchSettings.matchRecords.statistics.submitBatchIntervalMs -ne 250 `
        -or $matchSettings.matchRecords.statistics.maxEventsPerBatch -ne 24 `
        -or $matchSettings.matchRecords.replay.enabled -ne $false `
        -or $matchSettings.matchRecords.replay.autoRecordLimit -ne 20 `
        -or $matchSettings.matchRecords.replay.chunkTargetBytes -ne 262144) {
    throw "AuraToolsExp match-record shipped defaults are invalid."
}

$rootSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\AuraTools.json") | ConvertFrom-Json
$audioSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\AudioSettings.json") | ConvertFrom-Json
$pixelEmojiSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\PixelEmojiSettings.json") | ConvertFrom-Json
if ($rootSettings.schemaVersion -ne 2 `
        -or $audioSettings.schemaVersion -ne 5 `
        -or $null -ne $audioSettings.audioSystemVersion `
        -or $audioSettings.voice.enabled -ne $true `
        -or $null -eq $audioSettings.voice.bindings `
        -or $rootSettings.cardVisual.configFile -ne "CardVisualSettings.json" `
        -or $rootSettings.pixelEmoji.configFile -ne "PixelEmojiSettings.json" `
        -or $pixelEmojiSettings.schemaVersion -ne 1 `
        -or $pixelEmojiSettings.enabled -ne $false `
        -or $pixelEmojiSettings.syncRemote -ne $true `
        -or $pixelEmojiSettings.maxFavorites -ne 64) {
    throw "AuraToolsExp pixel emoji bundled config defaults drifted."
}

$loggingSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\LoggingSettings.json") | ConvertFrom-Json
if ($loggingSettings.schemaVersion -ne 5 `
        -or $loggingSettings.minimumLevel -ne "Info" `
        -or $loggingSettings.performanceDiagnostics -ne $false `
        -or $loggingSettings.mirrorUnityLog -ne $false `
        -or $loggingSettings.mirrorCommandsLog -ne $false `
        -or $loggingSettings.PSObject.Properties.Name -contains "enabledSources") {
    throw "AuraToolsExp logging configuration contract is invalid."
}

$skillCgSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\SkillCgSettings.json") | ConvertFrom-Json
if ($skillCgSettings.schemaVersion -ne 7 `
        -or $skillCgSettings.disableAfterFailures -ne $true `
        -or [Math]::Abs([double]$skillCgSettings.lowHealthThreshold - 0.3) -gt 0.0001 `
        -or $skillCgSettings.eventCg.enabled -ne $true `
        -or $skillCgSettings.eventCg.syncRemote -ne $true `
        -or $skillCgSettings.eventCg.baseWidth -ne 1600 `
        -or $skillCgSettings.eventCg.baseHeight -ne 900 `
        -or $skillCgSettings.eventCg.specialOpeningEnabled -ne $true `
        -or $skillCgSettings.eventCg.specialVictoryEnabled -ne $true `
        -or $skillCgSettings.eventCg.battleDefeatEnabled -ne $true `
        -or $skillCgSettings.eventCg.adventureSettlementEnabled -ne $true `
        -or $null -ne $skillCgSettings.PSObject.Properties["preloadOnFightStart"]) {
    throw "AuraToolsExp unified CG configuration contract is invalid."
}

$registration = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\SharedResources\aura.registration.json") | ConvertFrom-Json
$cgRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\SharedResources\cg.registry.json") | ConvertFrom-Json
$terriasRegistration = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "Terrias\SharedResources\aura.registration.json") | ConvertFrom-Json
$terriasCgRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "Terrias\SharedResources\cg.registry.json") | ConvertFrom-Json
$officialSkillCg = @($cgRegistry.entries | Where-Object {
    $_.subjectType -eq "role" `
        -and @($_.signals) -contains "aura.role.skill.committed" `
        -and $_.cgId -in @(
        "official.career_1.careercard_1",
        "official.career_3.careercard_4")
})
$officialFeastCg = @($cgRegistry.entries | Where-Object {
    $_.subjectType -eq "role" -and @($_.signals) -contains "aura.role.feast.completed"
})
$officialLowHealthCg = @($cgRegistry.entries | Where-Object {
    $_.subjectType -eq "role" -and @($_.signals) -contains "aura.role.health.crossed-down"
})
$eventCg = @($cgRegistry.entries | Where-Object {
    $_.subjectType -eq "event"
})
$terriasSkillCg = @($terriasCgRegistry.entries | Where-Object {
    $_.subjectType -eq "role" `
        -and @($_.signals) -contains "aura.role.skill.committed" `
        -and $_.cgId -in @(
        "loneer.morning-star-prayer",
        "wuna.white-sun-prayer",
        "columbina.homesickness")
})
$terriasCardUseCg = @($terriasCgRegistry.entries | Where-Object {
    $_.subjectType -eq "card" `
        -and @($_.signals) -contains "aura.card.use.presentation-committed" `
        -and $_.cgId -eq "terrias.blazing-crown-collapse"
})
$terriasFeastCg = @($terriasCgRegistry.entries | Where-Object {
    $_.subjectType -eq "role" `
        -and @($_.signals) -contains "aura.role.feast.completed" `
        -and $_.cgId -in @("loneer.feast", "wuna.feast", "columbina.feast")
})
$allCgEntries = @($cgRegistry.entries) + @($terriasCgRegistry.entries)
$legacyCgFieldsPresent = @($allCgEntries | Where-Object {
    $null -ne $_.PSObject.Properties["kind"] `
        -or $null -ne $_.PSObject.Properties["targetRoleIds"] `
        -or $null -ne $_.PSObject.Properties["skillIds"] `
        -or $null -ne $_.PSObject.Properties["cardIds"]
}).Count -ne 0
$invalidSemanticCg = @($allCgEntries | Where-Object {
    [string]::IsNullOrWhiteSpace([string]$_.subjectType) `
        -or @($_.subjectIds).Count -eq 0 `
        -or @($_.signals).Count -eq 0
}).Count -ne 0
$invalidSkillCgFacts = @($officialSkillCg + $terriasSkillCg | Where-Object {
    @($_.match.facts.skillId).Count -eq 0
}).Count -ne 0
$invalidEventScene = @($eventCg | Where-Object {
    $_.media.type -ne "scene" `
        -or $_.scene.layoutId -ne "team-stage.v1" `
        -or $_.scene.maximumParticipants -ne 8 `
        -or [string]::IsNullOrWhiteSpace([string]$_.scene.backgroundAsset.ownerModId) `
        -or [string]::IsNullOrWhiteSpace([string]$_.scene.backgroundAsset.assetId) `
        -or [string]::IsNullOrWhiteSpace([string]$_.scene.roleLayerOwnerModId) `
        -or [string]::IsNullOrWhiteSpace([string]$_.scene.roleLayerAssetPrefix)
}).Count -ne 0
$terminalEventCg = @($eventCg | Where-Object {
    @($_.signals) -notcontains "aura.battle.opening"
})
$openingEventCg = @($eventCg | Where-Object {
    @($_.signals) -contains "aura.battle.opening"
})
$cardVisualRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\card-visual.registry.json") | ConvertFrom-Json
$cardVisualSettings = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp\Config\CardVisualSettings.json") | ConvertFrom-Json
$terriasTheme = @($cardVisualRegistry.themes | Where-Object themeId -eq "terrias")
$foilEffect = @($cardVisualRegistry.effects | Where-Object effectId -eq "foil-holo")
$stardustEffect = @($cardVisualRegistry.effects | Where-Object effectId -eq "stardust")
$solarFramePreset = @($terriasTheme[0].mappingPreset | Where-Object skinId -eq "solar")
$morningStarFramePreset = @($terriasTheme[0].mappingPreset | Where-Object skinId -eq "morning-star")
$stellarOvertureCardIds = @(
    "Terrias_terrias_stellar_overture_start",
    "Terrias_terrias_stellar_overture_sustain",
    "Terrias_terrias_stellar_overture_turn",
    "Terrias_terrias_stellar_overture_close")
$solarFramePackMismatch = @(Compare-Object @($solarFramePreset[0].cardPackIds) @("Terrias_terrias_cardpack_solar_ember_crown_canopy")).Count -ne 0
$morningStarFramePackMismatch = @(Compare-Object @($morningStarFramePreset[0].cardPackIds) @("Terrias_terrias_cardpack_morning_star_overture")).Count -ne 0
$morningStarExplicitMismatch = @(Compare-Object @($morningStarFramePreset[0].cardIds) $stellarOvertureCardIds).Count -ne 0
$stardustMappingMismatch = @(Compare-Object @($stardustEffect[0].mappingPreset[0].cardIds) $stellarOvertureCardIds).Count -ne 0
$visualBundleBuildScript = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "tools\Build-AuraToolsVisualBundle.ps1")
$visualBundleBuilder = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\VisualAssets\Editor\AuraToolsVisualBundleBuilder.cs.txt")
$cardUiShader = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\VisualAssets\Shaders\CardFaceEffect.shader")
$cardUrpShader = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\VisualAssets\Shaders\CardFrameEffectUrp.shader")
$audioRegistry = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "Terrias\SharedResources\audio.registry.json") | ConvertFrom-Json
$voiceProviders = @($audioRegistry.providers | Where-Object { $_.match.stages.Count -gt 0 })
$skillVoiceProviders = @($audioRegistry.providers | Where-Object kind -eq "SkillVoice")
$expectedSkillVoiceSlots = @{
    "Terrias.Wuna.WhiteSunPrayer" = 1
    "Terrias.Wuna.GraveSong" = 2
    "Terrias.Columbina.EternalTide" = 1
    "Terrias.Columbina.Homesickness" = 2
}
$skillVoiceMismatch = @($skillVoiceProviders | Where-Object {
    -not $expectedSkillVoiceSlots.ContainsKey([string]$_.providerId) `
        -or [int]$_.match.skillSlot -ne [int]$expectedSkillVoiceSlots[[string]$_.providerId] `
        -or @($_.match.stages).Count -ne 1 `
        -or [string]$_.match.stages[0] -ne "Committed" `
        -or $null -ne $_.match.cardIds
}).Count -ne 0
$visualBundle = Join-Path $repoRoot "AuraToolsExp\ModResource\VisualBundles\auratools_visuals"
if ($registration.schemaVersion -ne 4 `
        -or $registration.ownerModId -ne "AuraToolsExp" `
        -or $registration.participantKind -ne "Tool" `
        -or $cgRegistry.ownerModId -ne "AuraToolsExp" `
        -or $cgRegistry.schemaVersion -ne 4 `
        -or $cgRegistry.protocol.preferredVersion -ne 4 `
        -or @($cgRegistry.entries).Count -ne 20 `
        -or $officialSkillCg.Count -ne 2 `
        -or $officialFeastCg.Count -ne 12 `
        -or $officialLowHealthCg.Count -ne 2 `
        -or $eventCg.Count -ne 4 `
        -or $invalidEventScene `
        -or $terminalEventCg.Count -ne 3 `
        -or @($terminalEventCg | Where-Object {
            $_.scene.exclusive -ne $true -or $_.scene.presentationProfileId -ne "terminal"
        }).Count -ne 0 `
        -or $openingEventCg.Count -ne 1 `
        -or $openingEventCg[0].scene.exclusive -ne $false `
        -or $terriasRegistration.ownerModId -ne "Terrias" `
        -or @($terriasRegistration.resources).Count -ne 9 `
        -or $terriasCgRegistry.ownerModId -ne "Terrias" `
        -or $terriasCgRegistry.schemaVersion -ne 4 `
        -or $terriasCgRegistry.protocol.preferredVersion -ne 4 `
        -or @($terriasCgRegistry.entries).Count -ne 7 `
        -or $terriasSkillCg.Count -ne 3 `
        -or $terriasCardUseCg.Count -ne 1 `
        -or $terriasFeastCg.Count -ne 3 `
        -or $invalidSkillCgFacts `
        -or $invalidSemanticCg `
        -or $legacyCgFieldsPresent `
        -or $cardVisualSettings.schemaVersion -ne 2 `
        -or @($cardVisualSettings.themes.PSObject.Properties).Count -ne 0 `
        -or @($cardVisualSettings.dynamicEffectOverrides.PSObject.Properties).Count -ne 0 `
        -or $terriasTheme.Count -ne 1 `
        -or @($terriasTheme[0].skins).Count -ne 3 `
        -or @($terriasTheme[0].mappingPreset).Count -ne 2 `
        -or $solarFramePreset.Count -ne 1 `
        -or $morningStarFramePreset.Count -ne 1 `
        -or $solarFramePackMismatch `
        -or @($solarFramePreset[0].cardIds).Count -ne 0 `
        -or $morningStarFramePackMismatch `
        -or $morningStarExplicitMismatch `
        -or @($cardVisualRegistry.effects).Count -ne 2 `
        -or $foilEffect.Count -ne 1 `
        -or @($foilEffect[0].mappingPreset[0].cardIds).Count -ne 1 `
        -or @($foilEffect[0].mappingPreset[0].cardPackIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0 `
        -or $foilEffect[0].mappingPreset[0].cardIds[0] -ne "Terrias_terrias_blazing_crown_collapse" `
        -or $stardustEffect.Count -ne 1 `
        -or @($stardustEffect[0].mappingPreset[0].cardIds).Count -ne 4 `
        -or @($stardustEffect[0].mappingPreset[0].cardPackIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -ne 0 `
        -or $stardustMappingMismatch `
        -or $cardVisualRegistry.schemaVersion -ne 9 `
        -or $cardVisualRegistry.protocol.minVersion -ne 9 `
        -or $cardVisualRegistry.protocol.preferredVersion -ne 9 `
        -or @($cardVisualRegistry.effects | Where-Object {
            $_.rendererId -ne "aura.card-visual.material-v7" `
                -or $_.targetLayer -ne "frame" `
                -or $_.coverageProfile -ne "native-frame-v5" `
                -or [string]::IsNullOrWhiteSpace([string]$_.imageMaterialPath) `
                -or [string]::IsNullOrWhiteSpace([string]$_.meshMaterialPath) `
                -or $_.floats._TerriasOverlayMode -ne 0 `
                -or $_.floats._TerriasFrameOnlyOverlay -ne 0
        }).Count -ne 0 `
        -or @($cardVisualRegistry.effects | ForEach-Object { $_.exposedParameters.PSObject.Properties.Value } | Where-Object { [string]::IsNullOrWhiteSpace($_.displayName) -or $_.step -le 0 }).Count -ne 0 `
        -or $visualBundleBuildScript -notmatch '\$requiredUnityVersion\s*=\s*"6000\.0\.46f1"' `
        -or $visualBundleBuildScript -notmatch 'com\.unity\.render-pipelines\.universal":"17\.0\.4"' `
        -or $visualBundleBuilder -notmatch 'BuildTarget\.StandaloneWindows64' `
        -or $visualBundleBuilder -notmatch 'CardFrameEffectUI\.mat' `
        -or $visualBundleBuilder -notmatch 'CardFrameEffectURP\.mat' `
        -or $visualBundleBuilder -notmatch 'GraphicsSettings\.defaultRenderPipeline' `
        -or $visualBundleBuilder -notmatch 'ValidateBuiltBundle' `
        -or $visualBundleBuilder -notmatch 'ValidateCardFrameEffectUiPixels' `
        -or $visualBundleBuilder -notmatch 'ValidateCardFrameEffectUrpPixels' `
        -or $visualBundleBuilder -notmatch 'material lease smoke passed' `
        -or $visualBundleBuilder -notmatch 'typeof\(MeshFilter\)' `
        -or $visualBundleBuilder -notmatch 'typeof\(MeshRenderer\)' `
        -or $visualBundleBuilder -notmatch 'GraphicsDeviceType\.Null' `
        -or $visualBundleBuilder -notmatch 'render smoke passed' `
        -or $visualBundleBuilder -notmatch 'ForceRebuildAssetBundle' `
        -or $visualBundleBuilder -notmatch 'ShaderUtil\.GetShaderMessages' `
        -or $visualBundleBuildScript -match '"-nographics"' `
        -or $visualBundleBuildScript -notmatch '"-force-d3d11"' `
        -or $visualBundleBuildScript -notmatch 'real Direct3D11 pixel smoke test' `
        -or $visualBundleBuildScript -notmatch 'real MeshRenderer Direct3D11 pixel smoke test' `
        -or $visualBundleBuildScript -notmatch 'pooled material detach/rebind smoke test' `
        -or $visualBundleBuildScript -notmatch 'total internal programs: \[1-9\]' `
        -or $cardUiShader -notmatch 'Shader\s+"AuraTools/CardFrameEffectUI"' `
        -or $cardUiShader -notmatch '"RenderPipeline"\s*=\s*"UniversalPipeline"' `
        -or $cardUiShader -notmatch '"LightMode"\s*=\s*"SRPDefaultUnlit"' `
        -or $cardUiShader -notmatch 'HLSLPROGRAM' `
        -or $cardUiShader -match 'FallBack\s+"UI/Default"' `
        -or $cardUrpShader -notmatch 'Shader\s+"AuraTools/CardFrameEffectURP"' `
        -or $cardUrpShader -notmatch '"RenderPipeline"\s*=\s*"UniversalPipeline"' `
        -or $cardUrpShader -notmatch 'CGPROGRAM' `
        -or $cardUrpShader -notmatch 'UnityCG\.cginc' `
        -or $audioRegistry.schemaVersion -ne 4 `
        -or $audioRegistry.ownerModId -ne "Terrias" `
        -or $audioRegistry.audioProtocol.minVersion -ne 8 `
        -or $audioRegistry.audioProtocol.preferredVersion -ne 8 `
        -or $voiceProviders.Count -ne @($audioRegistry.providers).Count `
        -or $skillVoiceProviders.Count -ne 4 `
        -or $skillVoiceMismatch `
        -or -not (Test-Path -LiteralPath $visualBundle -PathType Leaf) `
        -or (Get-Item -LiteralPath $visualBundle).Length -le 0) {
    throw "AuraToolsExp shared resource and CG ownership contract is invalid."
}
foreach ($resource in $registration.resources) {
    $source = Join-Path $repoRoot ("AuraToolsExp\SharedResources\" + ([string]$resource.source).Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $source)) {
        throw "AuraToolsExp shared resource source is missing: $($resource.source)"
    }
}

$cardPresentationSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraSharedCore\AuraCardPresentationRuntime.cs")
$skillCgRuntimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\SkillCg\AuraToolsSkillCgRuntime.cs")
$cgSharedRuntimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraCgShared\AuraCgRuntime.cs")
$bundledFoundationSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsBundledFoundationModelRuntime.cs")
if ($cardPresentationSource -notmatch 'AfterCommonCardUse\s*=' `
        -or $cardPresentationSource -notmatch 'FinalCombatCardPass' `
        -or $cardPresentationSource -notmatch 'OrderBy\(pair\s*=>\s*pair\.Value\.Priority\)' `
        -or $skillCgRuntimeSource -notmatch 'AuraSkillActionTransactionRouter\.Register' `
        -or $skillCgRuntimeSource -notmatch 'AuraSkillActionPhase\.Committed' `
        -or $cgSharedRuntimeSource -notmatch 'BeginFightDrain' `
        -or $cgSharedRuntimeSource -notmatch 'BattleSettling\s*=\s*_\s*=>\s*BeginFightDrain' `
        -or $bundledFoundationSource -notmatch 'AuraToolsSharedResourceDiscoveryRuntime\.Changed' `
        -or $bundledFoundationSource -notmatch 'TryAutoSelectSingleCompatibleModel' `
        -or $bundledFoundationSource -notmatch 'TrainedModelMode\s*=\s*"off"') {
    throw "Card presentation, typed CG transaction/drain, or model discovery lifecycle contract is invalid."
}

$discoveryRuntimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\SharedResources\AuraToolsSharedResourceDiscoveryRuntime.cs")
$loadedModCatalogSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\GameApi\AuraToolsLoadedModCatalog.cs")
$coreDiscoverySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraSharedCore\AuraSharedDiscovery.cs")
if ($discoveryRuntimeSource -notmatch "AuraToolsLoadedModCatalog\.Capture" `
        -or $discoveryRuntimeSource -notmatch "AuraSharedDiscoveryLoader\.Load" `
        -or $loadedModCatalogSource -notmatch "loadedModDirectories" `
        -or $loadedModCatalogSource -notmatch "manager\.modConfigs" `
        -or $coreDiscoverySource -notmatch '\*\.modproj' `
        -or $coreDiscoverySource -notmatch "MaximumSharedResourceFiles") {
    throw "AuraToolsExp loaded-Mod discovery, .modproj identity, or resource budget contract is invalid."
}

$moduleSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolsBuiltInModules.cs")
$moduleIdSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolModuleIds.cs")
$expectedModuleIds = @(
    "gameplay.starter-deck",
    "gameplay.card-refresh",
    "gameplay.feast",
    "presentation.feast-cg",
    "gameplay.safe-box",
    "presentation.skin",
    "presentation.battle-bgm",
    "presentation.card-use-audio",
    "presentation.character-voice",
    "presentation.pixel-emoji",
    "presentation.skill-cg",
    "presentation.card-use-cg",
    "presentation.card-visual",
    "records.damage-statistics",
    "records.battle-replay",
    "records.adventure-archive",
    "multiplayer.mod-sync",
    "multiplayer.lobby-status",
    "intelligence.auto-battle",
    "intelligence.strategy-model-lab",
    "system.file-logging",
    "system.preset-library",
    "system.mod-health"
)
foreach ($moduleId in $expectedModuleIds) {
    if ($moduleIdSource -notmatch [regex]::Escape('"' + $moduleId + '"')) {
        throw "AuraToolsExp built-in module catalog is missing: $moduleId"
    }
}

$entrySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Entry.cs")
if ($entrySource -notmatch "AuraToolModuleHost\.Initialize" `
        -or $entrySource -match "AuraTools(?:Audio|AutoBattle|CardRefresh|DamageMeter|CardUiBenchmark|Feast|FileLog|MatchRecords|ModSync|PixelEmoji|SafeBox|SkillCg|Skin|StarterDeck)Runtime\.Initialize") {
    throw "AuraToolsExp Entry must compose feature initialization through AuraToolModuleHost."
}

$hookRegistrySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Infrastructure\AuraToolsHookRegistry.cs")
$sharedHookSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraSharedCore\AuraSharedHooks.cs")
$allAuraToolsSource = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev") `
        -Recurse -File -Filter "*.cs"
) | ForEach-Object { Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName }
$allAuraToolsText = $allAuraToolsSource -join "`n"
if ($allAuraToolsText -match '\[Hook(?:Before|After)\b' `
        -or $hookRegistrySource -match 'AuraSharedHooks\.Register(?:Before|After)\s*\(' `
        -or $sharedHookSource -match 'public\s+static\s+bool\s+Register(?:Before|After)\s*\(' `
        -or $sharedHookSource -notmatch 'OwnerModId' `
        -or $sharedHookSource -notmatch 'HandlerId' `
        -or $sharedHookSource -notmatch 'LeaseCount' `
        -or $sharedHookSource -notmatch 'Generation') {
    throw "AuraToolsExp and AuraShared must use owner-qualified generation-safe routed hooks without attribute or direct registration paths."
}

$moduleHostSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolModuleHost.cs")
$activationPolicySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolsModuleActivationPolicy.cs")
if ($moduleSource -notmatch 'AuraToolsModuleActivationPolicy\.Activate' `
        -or $moduleSource -notmatch 'activation\.Dispose\(\)' `
        -or $moduleHostSource -notmatch 'ReconcileModule' `
        -or $activationPolicySource -notmatch 'ActivationLease') {
    throw "AuraToolsExp built-in module switches must own explicit activation leases and reconcile config changes."
}

$autoBattleRuntimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleRuntime.cs")
if ($autoBattleRuntimeSource -match 'ChooseDecision\(state,\s*"prediction"\)' `
        -or $autoBattleRuntimeSource -match 'private\s+CombatDecision\s+(?:ChooseDecision|RunDecisionEngine)\s*\(' `
        -or $autoBattleRuntimeSource -notmatch 'AutoBattle\.PredictionDecision' `
        -or $autoBattleRuntimeSource -notmatch 'CancelOwner' `
        -or $autoBattleRuntimeSource -match 'OwnerId\s*=\s*AuraToolsIds\.ModId\s*,') {
    throw "AuraToolsExp hot-path work must keep prediction search out of native hooks."
}

$lobbySnapshotSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraOnlineShared\AuraLobbySnapshotRuntime.cs")
if ($allAuraToolsText -match 'GameEntryUI\.UpdateLobby' `
        -or ([regex]::Matches(
                $lobbySnapshotSource,
                '"GameEntryUI\.UpdateLobby"')).Count -ne 1 `
        -or $lobbySnapshotSource -notmatch 'Fingerprint' `
        -or $lobbySnapshotSource -notmatch 'AuraChatModSyncSnapshot\.BuildState') {
    throw "AuraOnlineShared must own the single normalized Lobby snapshot projection consumed by AuraToolsExp."
}

$settingsSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsSettingsRuntime.cs")
$shellSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxSettingsShell.cs")
if ($settingsSource -notmatch "ToolboxSettingsShell\.Build\(panel\)" `
        -or @($settingsSource -split "`n").Count -gt 900 `
        -or $settingsSource -match "AuraToolsConfigService" `
        -or $settingsSource -match "using\s+AuraToolsExp\.Dll\.Features\.(?:Audio|AutoBattle|CardRefresh|DamageMeter|Diagnostics|Feast|Logging|MatchRecords|ModSync|PixelEmoji|SafeBox|SkillCg|Skin|StarterDeck)" `
        -or $settingsSource -match "Show(?:Audio|StarterDeck|Replay|AutoBattle|Logging)Settings" `
        -or $settingsSource -match "AutoInstallBundledSkins\s*=\s*true" `
        -or $settingsSource -match "PreferRoleModProfile\s*=\s*true" `
        -or $settingsSource -match "feast\.PlayCg\s*=\s*true" `
        -or $shellSource -match "using\s+AuraToolsExp\.Dll\.Features\.(?:Audio|AutoBattle|CardRefresh|DamageMeter|Diagnostics|Feast|Logging|MatchRecords|ModSync|PixelEmoji|SafeBox|SkillCg|Skin|StarterDeck)") {
    throw "AuraToolsExp toolbox shell boundary or render-purity contract is invalid."
}

$nativeContentLeaseSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\NativeSettingsContentLease.cs")
$categoryRailSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxCategoryRail.cs")
$moduleListItemSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxModuleListItem.cs")
$iconRegistrySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsIconRegistry.cs")
$toolboxVisualSpecSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxVisualSpec.cs")
$toolboxV2ComponentsSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxV2Components.cs")
$toolboxTooltipSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\ToolboxTooltipV2.cs")
if ($settingsSource -match "NativeContentLease" `
        -or $settingsSource -notmatch "CanvasGroup" `
        -or $settingsSource -notmatch "overrideSorting\s*=\s*true" `
        -or $settingsSource -notmatch "background\.raycastTarget\s*=\s*true" `
        -or $toolboxVisualSpecSource -notmatch "Workspace\s*=\s*new\(0\.031f,\s*0\.016f,\s*0\.227f,\s*1f\)" `
        -or $nativeContentLeaseSource -notmatch "NativeContentVisibilityLease<GameObject>" `
        -or $shellSource -notmatch "ToolboxCategoryRail\.Create" `
        -or $shellSource -notmatch "ToolboxSearchFieldV2\.Create" `
        -or $shellSource -match "ToolboxToolbar|categoryButtons" `
        -or $categoryRailSource -notmatch "ToolboxCategoryRailItem" `
        -or $shellSource -notmatch "category\.extensions" `
        -or $moduleListItemSource -notmatch "ToolboxCheckboxV2\.Create" `
        -or $moduleListItemSource -notmatch "ToolboxIconButtonV2\.Create" `
        -or $moduleListItemSource -match "AuraToolsSwitch\.Create|NativeButton" `
        -or $shellSource -match "NativeButton" `
        -or $categoryRailSource -match "NativeButton" `
        -or $moduleListItemSource -notmatch "Descriptor\.IconKey" `
        -or $iconRegistrySource -notmatch "ToolboxIcons" `
        -or $toolboxVisualSpecSource -notmatch "CategoryWidth\s*=\s*168f" `
        -or $toolboxVisualSpecSource -notmatch "ModuleRowHeight\s*=\s*78f" `
        -or $toolboxV2ComponentsSource -notmatch "ToolboxIconButtonV2" `
        -or $toolboxV2ComponentsSource -notmatch "ToolboxCheckboxV2" `
        -or $toolboxV2ComponentsSource -notmatch '"Square"' `
        -or $toolboxV2ComponentsSource -notmatch "ToolboxSearchPickerV3" `
        -or $toolboxV2ComponentsSource -notmatch "ToolboxTooltipTrigger\.Attach" `
        -or $toolboxV2ComponentsSource -match "AuraUiNativeHoverHint\.Attach" `
        -or $toolboxTooltipSource -notmatch "overrideSorting\s*=\s*true" `
        -or $toolboxTooltipSource -notmatch "blocksRaycasts\s*=\s*false" `
        -or $toolboxTooltipSource -notmatch "ToolboxTooltipPlacementPolicy\.Resolve" `
        -or $moduleSource -notmatch "IconKey\s*=\s*string\.IsNullOrWhiteSpace\(iconKey\)") {
    throw "AuraToolsExp toolbox visual shell or non-mutating native-page overlay contract is invalid."
}
if ($settingsSource -notmatch "PlacePanelAboveNativeCanvases" -or $settingsSource -notmatch "panel render state") {
    throw "AuraToolsExp toolbox panel sorting or render diagnostics are missing."
}

$matchRecordLibrarySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\MatchRecords\MatchRecordLibraryPresenter.cs")
$pixelWorkshopSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\PixelEmoji\PixelEmojiWorkshop.cs")
$pixelWorkshopLayoutSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\PixelEmoji\PixelEmojiWorkshopLayoutPolicy.cs")
if ($matchRecordLibrarySource -notmatch "ToolboxCheckboxV2\.Create" `
        -or $matchRecordLibrarySource -notmatch '"record\.more"' `
        -or $matchRecordLibrarySource -notmatch '"MetadataEditor-"' `
        -or $pixelWorkshopSource -notmatch "PixelEmojiWorkshopLayoutPolicy\.Resolve" `
        -or $pixelWorkshopSource -notmatch '"PrimaryActionSpacer"' `
        -or $pixelWorkshopLayoutSource -notmatch "WideMinimumWidth\s*=\s*820f" `
        -or $pixelWorkshopLayoutSource -notmatch "CompactMinimumWidth\s*=\s*740f" `
        -or $pixelWorkshopLayoutSource -notmatch "408f" `
        -or $pixelWorkshopLayoutSource -notmatch "360f" `
        -or $pixelWorkshopSource -notmatch "SetReferenceControlsExpanded" `
        -or $pixelWorkshopSource -notmatch "referenceDetails") {
    throw "AuraToolsExp compact records or horizontal pixel-workshop layout contract is invalid."
}

$toolboxIconDirectory = Join-Path $repoRoot "AuraToolsExp\ModResource\Images\UI\ToolboxIcons"
$expectedToolboxIcons = @(
    "all.png", "gameplay.png", "presentation.png", "records.png",
    "multiplayer.png", "intelligence.png", "system.png", "extensions.png",
    "file-logging.png", "skin.png", "battle-bgm.png", "card-use-audio.png",
    "starter-deck.png", "card-refresh.png", "feast.png", "feast-cg.png", "safe-box.png",
    "pixel-emoji.png", "mod-sync.png", "lobby-status.png", "damage-statistics.png", "battle-replay.png", "adventure-archive.png",
    "auto-battle.png", "skill-cg.png", "card-use-cg.png", "search.png",
    "preset-library.png", "mod-health.png",
    "clear.png", "folder.png", "settings.png", "warning.png",
    "switch-track.png", "switch-thumb.png"
)
foreach ($iconName in $expectedToolboxIcons) {
    if (-not (Test-Path -LiteralPath (Join-Path $toolboxIconDirectory $iconName) -PathType Leaf)) {
        throw "AuraToolsExp toolbox icon is missing: $iconName"
    }
}

$toolboxV2Directory = Join-Path $repoRoot "AuraToolsExp\ModResource\Images\UI\ToolboxV2"
foreach ($assetName in @(
        "toolbox-surface-9slice.png",
        "toolbox-control-9slice.png",
        "toolbox-category-selected-9slice.png",
        "toolbox-checkbox-atlas.png",
        "toolbox-icon-button-atlas.png")) {
    if (-not (Test-Path -LiteralPath (Join-Path $toolboxV2Directory $assetName) -PathType Leaf)) {
        throw "AuraToolsExp Toolbox V2 asset is missing: $assetName"
    }
}

$moduleSettingsPages = @(
    "AuraToolsExp-Dev\Features\Audio\AuraToolsAudioSettingsPage.cs",
    "AuraToolsExp-Dev\Features\StarterDeck\AuraToolsStarterDeckSettingsPage.cs",
    "AuraToolsExp-Dev\Features\Feast\AuraToolsFeastRoleEditor.cs",
    "AuraToolsExp-Dev\Features\MatchRecords\AuraToolsReplaySettingsPage.cs",
    "AuraToolsExp-Dev\Features\Logging\AuraToolsLoggingSettingsPage.cs",
    "AuraToolsExp-Dev\Features\AutoBattle\AuraToolsAutoBattleSettingsPage.cs",
    "AuraToolsExp-Dev\Features\CardVisual\AuraToolsCardVisualEditor.cs",
    "AuraToolsExp-Dev\Features\PresetLibrary\AuraPresetLibraryPage.cs",
    "AuraToolsExp-Dev\Features\ModHealth\ModHealthPage.cs",
    "AuraToolsExp-Dev\Features\LobbyStatus\LobbyStatusRuntime.cs",
    "AuraToolsExp-Dev\Features\AdventureArchive\AdventureArchivePage.cs"
)
foreach ($relativePage in $moduleSettingsPages) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relativePage) -PathType Leaf)) {
        throw "AuraToolsExp feature-owned settings page is missing: $relativePage"
    }
    $pageSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $repoRoot $relativePage)
    if ($pageSource -match 'AddDecoratedReplayPanelImage\(') {
        throw "AuraToolsExp settings page still uses the decorated toolbox-home surface: $relativePage"
    }
}

$layoutSources = @(Get-ChildItem -LiteralPath (
        Join-Path $repoRoot "AuraToolsExp-Dev\Features") -Recurse -File -Filter "*.cs")
foreach ($layoutSource in $layoutSources) {
    $lines = @(Get-Content -Encoding UTF8 -LiteralPath $layoutSource.FullName)
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -notmatch 'AddComponent<HorizontalLayoutGroup>') {
            continue
        }
        $end = [Math]::Min($lines.Count - 1, $index + 10)
        $layoutBlock = $lines[$index..$end] -join "`n"
        if ($layoutBlock -notmatch 'childForceExpandHeight\s*=') {
            throw "AuraToolsExp horizontal layout leaves vertical expansion implicit: $($layoutSource.FullName):$($index + 1)"
        }
    }
}
if ($moduleSource -match "AuraToolsSettingsRuntime" `
        -or $moduleSource -notmatch "AuraToolsAudioSettingsPage" `
        -or $moduleSource -notmatch "AuraToolsStarterDeckSettingsPage" `
        -or $moduleSource -notmatch "AuraToolsFeastRoleEditor" `
        -or $moduleSource -notmatch "AuraToolsReplaySettingsPage" `
        -or $moduleSource -notmatch "AuraToolsLoggingSettingsPage" `
        -or $moduleSource -notmatch "AuraToolsAutoBattleSettingsPage" `
        -or $moduleSource -notmatch "ShowStrategyLab" `
        -or $moduleSource -notmatch 'showEnableControl:\s*false') {
    throw "AuraToolsExp built-in modules must route to feature-owned settings pages."
}

$configSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsConfigService.cs")
$moduleConfigSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolModuleConfig.cs")
$loggingConfigSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Config\AuraToolsLoggingSettings.cs")
$loggingRuntimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Logging\AuraToolsFileLogRuntime.cs")
foreach ($saveMethod in @(
        "SaveBattleBgm",
        "SaveCardUseAudio",
        "SaveStarterDeck",
        "SaveCardRefresh",
        "SaveFeast",
        "SaveFeastCg",
        "SaveSafeBox",
        "SaveModSync",
        "SaveDamageStatistics",
        "SaveBattleReplay",
        "SaveAutoBattle",
        "SavePixelEmoji",
        "SaveSkillCg",
        "SaveCardUseCg",
        "SaveSkin",
        "SaveLogging",
        "SavePresetLibrary",
        "SaveModHealth",
        "SaveLobbyStatus",
        "SaveAdventureArchive")) {
    if ($configSource -notmatch ("public static void " + $saveMethod + "\s*\(")) {
        throw "AuraToolsExp module-scoped config save is missing: $saveMethod"
    }
}
if ($moduleConfigSource -notmatch 'ConfigSystem = "AuraTools\.Modules"' `
        -or $moduleConfigSource -notmatch "AuraToolConfigChangeBus" `
        -or $moduleConfigSource -notmatch "AuraToolModuleConfigDocument" `
        -or $moduleConfigSource -notmatch "BeginBatch") {
    throw "AuraToolsExp module config store, document, or change bus contract is invalid."
}
if ($configSource -notmatch 'TryUpdateLogging' `
        -or $configSource -match 'SaveModule\(Logging' `
        -or $loggingRuntimeSource -match 'EnabledSources' `
        -or $loggingConfigSource -match 'public\s+List<string>\s+EnabledSources') {
    throw "AuraToolsExp file logging must use transactional module persistence and one mirror gate per source."
}

$codecSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\PresetLibrary\AuraToolConfigCodecs.cs")
$presetServiceSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\PresetLibrary\AuraPresetLibraryService.cs")
$codecNames = @([regex]::Matches(
        $codecSource,
        'Codec\(Audit\(AuraToolModuleIds\.(\w+)') | ForEach-Object { $_.Groups[1].Value })
$expectedCodecNames = @(
    "StarterDeck", "CardRefresh", "Feast", "FeastCg", "SafeBox", "Skin",
    "BattleBgm", "CardUseAudio", "PixelEmoji", "SkillCg", "CardUseCg",
    "EventCg", "DamageStatistics", "BattleReplay", "AdventureArchive", "ModSync",
    "LobbyStatus", "AutoBattle", "FileLogging", "PresetLibrary", "ModHealth")
if ($codecNames.Count -ne 21 `
        -or @($codecNames | Sort-Object -Unique).Count -ne 21 `
        -or @(Compare-Object ($codecNames | Sort-Object) ($expectedCodecNames | Sort-Object)).Count -ne 0 `
        -or $codecSource -notmatch 'payload\.Remove\("cardUseCg"\)' `
        -or $codecSource -notmatch 'payload\.Remove\("eventCg"\)' `
        -or $codecSource -notmatch 'modelRiskAcknowledgements' `
        -or $codecSource -notmatch 'captureTrainingSamples' `
        -or $presetServiceSource -notmatch 'AuraToolConfigChangeBus\.BeginBatch' `
        -or $presetServiceSource -notmatch 'AsEnumerable\(\)\.Reverse' `
        -or $presetServiceSource -notmatch '应用前备份') {
    throw "AuraToolsExp per-module preset Codec inventory, exclusions, or transaction rollback contract is invalid."
}

$healthSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\ModHealth\ModHealthRuntime.cs")
$lobbyStatusSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\LobbyStatus\LobbyStatusRuntime.cs")
$archiveDatabaseSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AdventureArchive\AdventureArchiveDatabase.cs")
$archiveStorageSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AdventureArchive\AdventureArchiveStorage.cs")
$archiveRuntimeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AdventureArchive\AdventureArchiveRuntime.cs")
$archiveModelSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\AdventureArchive\AdventureArchiveModels.cs")
if ($healthSource -notmatch 'loadedModDirectories' `
        -or $healthSource -notmatch 'ModConfig\.json' `
        -or $healthSource -notmatch 'Assembly\.LoadFrom' `
        -or $healthSource -notmatch 'CsvReader' `
        -or $healthSource -match 'Aura(?:Cg|Skin|Audio|Journey|Tool)Registry' `
        -or $lobbyStatusSource -notmatch 'AuraLobbySnapshotRuntime\.Register' `
        -or $lobbyStatusSource -match 'GameEntryUI\.UpdateLobby' `
        -or $lobbyStatusSource -match 'AuraToolsRpcSender|SendCommand|SendRPC' `
        -or $archiveStorageSource -notmatch 'DamageHistoryStorage\.Database\.DatabasePath' `
        -or $archiveDatabaseSource -notmatch 'adventure_archives' `
        -or $archiveDatabaseSource -notmatch 'battle_records' `
        -or $archiveDatabaseSource -notmatch 'MigrateLegacyRows' `
        -or $archiveModelSource -notmatch 'CurrentVersion = 2' `
        -or $archiveRuntimeSource -notmatch 'AttachEventChoiceObservers' `
        -or $archiveRuntimeSource -notmatch 'manager\.onClick\.AddListener' `
        -or $archiveRuntimeSource -notmatch 'CollectionChanged' `
        -or $archiveRuntimeSource -match 'CaptureSnapshots') {
    throw "AuraToolsExp health, lobby-read-model, or shared AdventureId storage boundary is invalid."
}

$consumerConfigSource = @(
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Features") `
        -Recurse -File -Filter "*.cs"
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "AuraToolsExp-Dev\Modules") `
        -Recurse -File -Filter "*.cs"
) | ForEach-Object { Get-Content -Raw -Encoding UTF8 -LiteralPath $_.FullName }
$consumerConfigText = $consumerConfigSource -join "`n"
if ($consumerConfigText -match "AuraToolsConfigService\.(?:Changed|AudioChanged|MatchExperienceChanged|LoggingChanged)\s*\+=" `
        -or $consumerConfigText -match "AuraToolsConfigService\.(?:SaveAudio|SaveMatchExperience)\s*\(" `
        -or $consumerConfigText -match "AuraToolsConfigService\.Root\.(?:Audio|MatchExperience|PixelEmoji|SkillCg|Skin|Logging)\.Enabled") {
    throw "AuraToolsExp feature consumers must use module-scoped config saves, subscriptions, and enablement."
}

$uiSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsUi.cs")
$uiThemeSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsUiTheme.cs")
$viewStateSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraUiShared\AuraUiViewState.cs")
$preparationDockSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsPreparationDock.cs")
$lobbyLauncherSources = ((Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\ModSync\AuraToolsModSyncRuntime.cs")) + "`n" +
    (Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\LobbyStatus\LobbyStatusRuntime.cs")) + "`n" +
    (Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\DamageMeter\AuraToolsDamageMeterUi.cs")))
if (($uiSource + $toolboxV2ComponentsSource) -notmatch "SetIsOnWithoutNotify" `
        -or $uiSource -notmatch "created\.layer\s*=\s*parent\.gameObject\.layer" `
        -or $uiSource -notmatch "ToolboxSurfaceV2\.ApplyControl" `
        -or $uiSource -notmatch "ConfigureHorizontalLayout" `
        -or $uiSource -notmatch "AddSettingsWindowImage\(window\)" `
        -or $uiSource -match "Mathf\.Max\(height, ButtonHeight\)" `
        -or $uiSource -match "button-九宫格|background-九宫格" `
        -or $uiThemeSource -match "AuraUiStyleIds\.WitchNative" `
        -or $preparationDockSource -notmatch 'Toolbox-styled action dock' `
        -or $preparationDockSource -notmatch 'AuraToolsUi\.AddButtonImage' `
        -or $toolboxV2ComponentsSource -notmatch 'enum\s+ActionState' `
        -or $toolboxV2ComponentsSource -notmatch 'SetActionState\(' `
        -or $toolboxV2ComponentsSource -notmatch 'button\.interactable\s*==\s*lastInteractable' `
        -or [regex]::Matches($lobbyLauncherSources, 'AuraToolsPreparationDock\.Register\(').Count -ne 1 `
        -or $lobbyLauncherSources -notmatch 'AuraToolsPreparationDock\.Register\s*\(\s*"lobby-status"' `
        -or $lobbyLauncherSources -notmatch 'AddModalBackdrop[\s\S]*backdrop\.transform\.SetAsFirstSibling\(\)' `
        -or $lobbyLauncherSources -match 'AuraToolsModConfigButton|AuraToolsLobbyStatusButton|button-九宫格|DamageMeterUiAssets' `
        -or $viewStateSource -notmatch "AnchorId" `
        -or $viewStateSource -notmatch "FocusedId" `
        -or $viewStateSource -notmatch "AuraUiKeyedListReconciler") {
    throw "AuraToolsExp UI layer inheritance, stable toggle, scroll-anchor, focus, or keyed-list contract is invalid."
}

$playerDisplaySource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\Settings\AuraToolsPlayerDisplay.cs")
$cardVisualEditorSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Features\CardVisual\AuraToolsCardVisualEditor.cs")
if ($playerDisplaySource -notmatch 'AuraToolsContentIdentity\.Parse' `
        -or $cardVisualEditorSource -match 'AddText\([^\r\n]*parameter\.Key' `
        -or $cardVisualEditorSource -notmatch 'parameter\.Value\.DisplayName' `
        -or $cardVisualEditorSource -notmatch 'ToolboxSearchPickerV3\.Create') {
    throw "AuraToolsExp player-facing content labels or card parameters drifted."
}

$toolingProtocolSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolingShared\AuraToolExtensionProtocol.cs")
$extensionAdapterSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolSharedExtensionAdapter.cs")
$moduleHostSource = Get-Content -Raw -Encoding UTF8 -LiteralPath (
    Join-Path $repoRoot "AuraToolsExp-Dev\Modules\AuraToolModuleHost.cs")
if ($toolingProtocolSource -notmatch "CurrentVersion = 1" `
        -or $toolingProtocolSource -notmatch "MinimumSupportedVersion = 1" `
        -or $toolingProtocolSource -notmatch "ownerModId" `
        -or $toolingProtocolSource -notmatch "RegistrationHandle" `
        -or $extensionAdapterSource -notmatch "IAuraToolExtensionProvider" `
        -or $moduleHostSource -notmatch "AuraToolExtensionRegistry\.Changed" `
        -or $shellSource -notmatch 'Id = "extensions"') {
    throw "AuraToolsExp shared tooling extension protocol or dynamic catalog integration is invalid."
}

Write-Host "AuraToolsExp behavior and Tool-owned content tests passed."
