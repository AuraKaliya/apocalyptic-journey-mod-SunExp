$ErrorActionPreference = 'Stop'
$cgGuideRepo = (Get-Location).Path
$cgGuideOutput = Join-Path $cgGuideRepo 'artifacts/cg-author-guide'
$cgGuideStage = Join-Path $cgGuideOutput 'share'
$cgGuideExampleSource = Join-Path $cgGuideRepo 'docs/AuraToolsExp/examples/character-mod-cg'
$cgGuideExampleTarget = Join-Path $cgGuideStage 'examples/character-mod-cg'
[IO.Directory]::CreateDirectory($cgGuideExampleTarget) | Out-Null
foreach ($cgGuideSourceFile in Get-ChildItem -LiteralPath $cgGuideExampleSource -Recurse -File) {
    $cgGuideRelative = [IO.Path]::GetRelativePath($cgGuideExampleSource, $cgGuideSourceFile.FullName)
    $cgGuideDestination = Join-Path $cgGuideExampleTarget $cgGuideRelative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($cgGuideDestination)) | Out-Null
    [IO.File]::Copy($cgGuideSourceFile.FullName, $cgGuideDestination, $true)
}
$cgGuideOriginal = [IO.File]::ReadAllText((Join-Path $cgGuideRepo 'docs/AuraToolsExp/character-mod-cg-configuration-guide.zh-CN.md'))
$cgGuideShare = $cgGuideOriginal.Substring(0, $cgGuideOriginal.IndexOf('## 13.'))
$cgGuideShare += @'
## 13. 核对范围

本手册按 2026-09-09 的当前实现核对。三份完整 JSON 与附带模板一致；在临时目录补入测试图片后，通过当前共享 DLL 的发现、资源注册、逻辑路径解析与重复注册检查，也通过正确技能匹配、错误技能及错误角色不匹配检查。

测试没有执行 Unity 画面播放。作者仍需按第 10 节完成游戏内与联机验证。模板中的身份为虚构值，图片由作者提供。

本外发包只含手册与配置模板，不含角色 MOD、身份文件、DLL 或 CG 素材。
'@
[IO.File]::WriteAllText((Join-Path $cgGuideStage 'character-mod-cg-configuration-guide.zh-CN.md'), $cgGuideShare, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $cgGuideStage 'README.md'), "# 角色 MOD CG 配置手册与模板`n`n先阅读 [配置手册](character-mod-cg-configuration-guide.zh-CN.md)，再使用 [JSON 模板](examples/character-mod-cg/README.md)。`n`n请替换为自己的 MOD、角色和技能 ID，并提供图片；本包不能作为独立 MOD 安装。`n", [Text.UTF8Encoding]::new($false))
$cgGuideLinkCount = 0
foreach ($cgGuideMd in Get-ChildItem -LiteralPath $cgGuideStage -Recurse -File -Filter '*.md') {
    $cgGuideMdText = [IO.File]::ReadAllText($cgGuideMd.FullName)
    foreach ($cgGuideLink in [regex]::Matches($cgGuideMdText, '\]\(([^)]+)\)')) {
        $cgGuideTarget = $cgGuideLink.Groups[1].Value
        if ($cgGuideTarget -match '^https?://|^#') { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $cgGuideMd.DirectoryName $cgGuideTarget))) { throw ('Broken handout link: ' + $cgGuideTarget) }
        $cgGuideLinkCount++
    }
}
$cgGuideZip = Join-Path $cgGuideOutput '角色MOD-CG配置手册与模板.zip'
Compress-Archive -Path (Join-Path $cgGuideStage '*') -DestinationPath $cgGuideZip -Force
$cgGuideArchive = [IO.Compression.ZipFile]::OpenRead($cgGuideZip)
try {
    [pscustomobject]@{ Package = $cgGuideZip; Entries = @($cgGuideArchive.Entries.FullName); InternalLinksChecked = $cgGuideLinkCount } | ConvertTo-Json -Depth 3
} finally { $cgGuideArchive.Dispose() }

