# AuraToolsExp 对局记录、独立回放与视频

## 功能边界

- 对局摘要、DPT 统计、分析、结构化回放和媒体统一从“对局记录”进入。
- 摘要和分析可独立保留；只有 `Replay Document v12` 且状态为 `Ready` 的记录显示“回放”。
- v12 canonical 文档是权威数据；POV 是不进入 canonical 哈希的本机可选 sidecar，MP4 是派生媒体。
- 回放不初始化真实战斗，也不执行卡牌、Buff、职业、遗物、敌人、Partner、AI、随机数、
  Command、RPC、奖励或存档逻辑。
- 完整协议与实现边界见 [独立表现回放 v12 实现架构](./match-replay-v12-portable-canonical-design.md)。

## Replay Document v12

v12 只保存可验证、可移植的通用事实：

- 主机权威公共状态及 typed delta；
- 全局有序的因果事务、truth lane 与 presentation lane；
- 动态实体 spawn generation、稳定槽位、卡牌来源、actor/target、动画、效果 delay 和音频 cue；
- reducer 派生的配对完整检查点；
- Aura Replay Presentation ABI 的 scene/entity/card/buff/intent/effect descriptor；
- 内容寻址的 PNG 与 PCM16 WAV 资产；
- truthRoot、presentationRoot 和 documentRoot。

本机手牌和私有牌堆只进入可选 POV sidecar。内容 MOD 的 owner/id/version 仅作为来源信息，
不构成播放依赖，也不进入任何玩法脚本入口。

## 独立回放

播放器创建 AuraToolsExp 自有的隔离根对象、Camera、Canvas、背景、双方单位、HUD、Buff/intent
文本、卡牌、效果和音频。它按 journal 的 logical time、transactionId 和 stepOrdinal 投影记录
事实；表现完成不会写回 reducer。

定位从最近的 truth/presentation checkpoint 恢复，静默应用后续事件，并从完整 presentation
lane 重建目标时刻仍有效或尚在 delay 中的卡牌、效果和音频。退出只销毁播放器拥有的对象，
不需要恢复 `RoleTable`、`FightManager`、`FightCardManager` 或 `FightUI`，因为它们从未被创建或修改。

## `.aurareplay` v12

默认导出包包含：

- `manifest.json`；
- `document.json.gz`；
- `timeline/truth/*` 与 `timeline/presentation/*`；
- `checkpoints/truth/*` 与 `checkpoints/presentation/*`；
- `assets/<sha256>.png|wav`；
- `analysis/summary.json.gz` 派生缓存。

默认包不包含 POV、DLL、脚本或 MP4。导入严格拒绝未知/重复 JSON 字段、非法 entry、路径穿越、
可执行内容、超预算数据、chunk 链不一致、检查点伪造、资源清单不等和任一根哈希错误。导入后
分析会从 canonical 文档重新生成，不信任包内派生缓存。

## MP4 导出

- 视频只通过 v12 独立播放器的专用 Camera 与固定 RenderTexture 取帧，不截取桌面或游戏窗口。
- 时间由固定帧步推进，不依赖实时帧率；离线音轨从同一组 PCM cue 混音。
- 输出固定为受控 FFmpeg 的 MP4 profile；失败时保留结构化回放，不生成静音或 AVI 降级文件。
- 已验证 MP4 与 canonical 文档分开存储，删除或重建媒体不会改变 documentRoot。

## 旧记录

v11 已退出结构化可播放集合。数据库升级会先写备份和迁移审计报告，再把 pre-v12 记录改为
`SummaryOnly`，保留摘要、分析、收藏信息和已验证 MP4，删除旧 document/chunk/asset 引用。
旧 `.aurareplay` 包不进入 v12 importer，也不存在隐藏 v11 播放器或双 writer。

## 验证状态

纯 reducer、事务、检查点、资产、POV、SQLite、包、分析、媒体、协议清单和发布门禁已有自动
覆盖。发布前仍需在游戏进程中完成单机、2/3 人联机、动态友方单位、Terrias Projection、
重启后卸载源 MOD、seek/倍速/退出以及 MP4 音画同步黑盒矩阵。
