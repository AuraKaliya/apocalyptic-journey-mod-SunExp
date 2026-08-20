# AuraToolsExp 对局记录、v10 回放与视频

## 产品边界

- 对局摘要、DPT 统计和冒险关联可以在没有可播放回放时独立保留。
- 可播放记录只接受经过完整验证的 `Replay Document v10`。
- v8/v9 没有运行时播放器，只能进入只读迁移扫描和授权清理流程。
- 交互播放和离线视频使用 AuraToolsExp 自有的确定性 2D 回放场景。
- 回放不启动 Mirror、不创建 `FightManager`、不打开 `FightUI`，也不执行脚本、
  AI、随机数、RPC 或战斗行动队列。

完整架构和验收要求见
[回放 v10 终局架构](./match-replay-v10-final-architecture.md)。

## Replay Document v10

v10 保存：

- owner-qualified 内容 ID 和文档内唯一实例 ID；
- 关卡、背景、角色、敌人、卡牌、Buff 和意图的初始状态；
- 已本地化的名称、描述、标签和数值展示；
- 图标、卡图、头像、立绘和背景的内容寻址附件；
- 严格递增的权威状态事件、分析语义、表现提示和音频提示；
- 初始、定期和最终检查点；
- 规范化状态哈希、事件链哈希、文档哈希和附件 SHA-256；
- 游戏、工具、渲染 profile 和内容来源信息。

逻辑状态由纯投影器应用 after-value 增量产生。投影器不重新计算伤害、治疗、
Buff、费用、AI 或目标。任一事件、检查点或附件验证失败时，只保存摘要和统计，
不会生成可降级播放的记录。

## 专用回放场景

`ReplaySceneRuntime` 完全拥有自己的 Camera、Canvas、Actor/Card/Intent View、HUD
和时间轴。角色和敌人使用工具自有的 2D 站位和通用 attack、hit、heal、block、
buff、death 表现语义。

交互控制包括播放/暂停、速度、上一/下一动作、上一/下一回合、时间轴跳转和退出。
跳转从最近检查点恢复并静默应用到目标事件，瞬态数字和动画不会跨跳转残留。
退出只销毁回放场景拥有的对象，不修改大厅、网络、ChatUI、RoleTable 或冒险状态。

## `.aurareplay` v10

v10 包是带严格清单的 ZIP 容器，包含：

```text
manifest.json
document.json.gz
timeline/*.json.gz
checkpoints/*.json.gz
attachments/<sha256>.<extension>
analysis/summary.json.gz
```

导入器拒绝旧 package version、额外或缺失 entry、重复路径、目录穿越、大小超限、
chunk/附件哈希错误和无法重建最终状态的文档。包在 staging 中完成验证后才提交到
数据库和内容寻址附件目录。

## MP4 导出

导出器按固定 30 FPS 时钟驱动同一个回放场景并渲染专用 `RenderTexture`。RGB24
原始帧进入容量固定的内存管道，管道满时暂停离线推进，不丢帧，也不生成 JPEG
spool。音频根据 v10 的样本时间事件离线混合，不读取实时 `AudioListener`。

AuraToolsExp 只调用工具自带且通过版本/许可证/文件哈希校验的 FFmpeg 和 ffprobe，
不读取 PATH 或用户配置路径。输出固定为 MP4，不存在 AVI fallback。

编码完成后依次执行 ffprobe 元数据检查、全部帧和音频完整解码、帧数/时长/profile
核对及 SHA-256。只有验证成功的文件才能原子移动并登记媒体。

任务状态为：

```text
Planned -> Rendering -> Encoding -> Validating -> Committing -> Ready
```

任务、staging/目标路径、哈希和恢复证据保存在 SQLite。启动时会恢复 Validating
和 Committing，回收 Rendering/Encoding 中断留下的 partial，并标记缺失或哈希错误
的 Ready 媒体。进入 Committing 后不再接受取消。

媒体库只接收经过完整解码验证的 MP4。旧 AVI/MOV/WebM 由迁移报告隔离，不能作为
第二种持久格式继续登记。

## v8/v9 迁移

设置页提供“扫描旧录像”“查看报告”“确认清理旧回放”三个明确动作。扫描是只读
操作，会列出每个旧记录、chunks、媒体和孤儿文件的精确处理结果。

现有旧 `ContentSha256` 只是旧回放负载哈希，不能证明录制时游戏/MOD 内容。缺少
owner-qualified 敌人/角色 ID 或原始附件的记录只保留统计、事件摘要和迁移报告。
只有另有逐资源可验证原始归档时，迁移器才允许转换成 v10。

确认清理默认只删除旧 chunks 和已报告的旧临时媒体，保留对局统计及冒险关联。
旧媒体和未知文件先进入隔离目录；迁移器拒绝任何请求统计删除的报告。

## 验收

1. 在没有原 MOD/CSV 的环境中导入 v10 包并完成播放、跳转和退出。
2. 播放前后确认 Mirror、FightManager、ChatUI、RoleTable 和冒险状态未变化。
3. 重复从检查点跳到相同动作，确认逻辑状态 SHA-256 一致。
4. 分别导出 720p30/1080p30、有无 HUD、有无音频，并完成全文件解码。
5. 在每个任务状态强制终止进程，确认启动恢复结果与任务日志一致。
6. 修改任一包 entry、chunk 或附件，确认导入在写数据库前失败。
7. 扫描 v8/v9 后核对报告数量和字节范围，未确认时不得删除任何旧数据。
8. 发布包检查只存在协议 10/10、一个播放器和一个 MP4 编码路径。
