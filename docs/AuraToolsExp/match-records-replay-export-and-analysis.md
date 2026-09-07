# AuraToolsExp 对局记录、回放与视频

## 功能边界

- 对局摘要、DPT、分析、结构化回放和媒体统一从“对局记录”进入。
- 只有 `Replay Document v17` 且状态为 `Ready` 的记录显示“回放”。
- truth lane 保存固定玩家视角下的可见状态增量；presentation lane 保存已解析表现指令、共享 MOD
  事件和实测轨道；MP4 是
  派生媒体。
- 播放器不初始化真实战斗，也不执行卡牌、BUFF、职业、遗物、敌人、精灵、奖励、Command 或 RPC。
- 完整契约见 [Replay Document v17 原生表现与可见状态指令流](./match-replay-v17-native-presentation-design.md)。
- 其它 MOD 的自动/显式接入边界见 [跨 MOD 兼容性评价](./match-replay-cross-mod-compatibility-assessment.md)。

## v17 保存的内容

- HP、防御、BUFF、意图、Power、技能和原生 FightUI 资源；
- 实体 root/body/head/bottom/center 实测布局、原生动画参数、允许重复别名的有序帧序列与 sorting；
- 固定视角手牌的实测最终布局与牌堆/弃牌堆/衍生牌堆数量；
- 因果事务、typed delta、truth/presentation 双 lane 和配对检查点；
- 场景、角色、卡牌、BUFF、意图、特效和音频资源身份；
- 卡牌移动/焚毁材质轨道、角色/受击世界轨道、相机、伤害字、回合提示和扩展指令；
- owner-qualified 可见扩展状态；
- 可选动态附件和三层根哈希。

不保存抽牌堆顺序、不可见对手数据、Unity DOM、屏幕截图或本机 POV sidecar。

## 录制可靠性

materialized baseline、不可变增量 batch 和终局草稿由同一个后台 FIFO 依次落库。
战斗线程封闭终局轨迹后交出文档所有权，不执行全量 JSON 复制、压缩、写库或同步保存回退。
共享调度器暂时无容量时，待写数据仍由录制队列持有，常驻驱动在后续帧继续提交；关闭录制或进入
下一场不会清掉已交出的工作。数据库提交成功才表示该前缀耐久，内存排队不等于已保存。
完整 `Finalizing` 草稿提交后才开始后台规范化、校验和正式保存。启动时自动恢复已提交的
finalization；只有录制前缀的中断记录标为 `Incomplete`，不会冒充 `Ready`。
自动保留数量只计算已结束记录，不清理仍在 `Recording` / `Finalizing` 的记录。

手牌到达和移动继续逐视图记录，创建/排布引起的状态采集合并到已有的帧或动作边界；打出下一张牌
之前仍会强制结算该边界。单人房间不准备 canonical 同步负载；存在其他玩家时，在后台编码，发送
前再次检查接收者。既有 v17 文档、根哈希和读取格式保持兼容，不重写用户旧记录。

`[MatchRecords:perf] terminal handoff` 显示战后主线程耗时及排队情况；`background FinalizingDraft`、
`Finalize`、`NetworkPrepare` 分别报告慢任务的后台执行和排队时间，不能把这些后台时间当作冻结时长。

## 回放与定位

`ReplayBattleSceneRuntimeV17` 在独立 RenderTexture 中重建资源背景、实测比例角色、完整原生
FightUI、状态 HUD、原生卡牌视图和实测重叠动作。FightUI、StatusBar、HpItem、BuffItem、ActionMsg
和 CardItem prefab 在 inactive 隔离根中移除玩法/输入行为后才启用，因此保留原生材质、排序、Mask、
字体和层级但不会注册玩法单例。MOD 通过共享模块发布纯数据事件；通用原语由 AuraTools 渲染，
provider-required 表现由当前兼容 MOD 的仅表现 renderer 重建，AuraTools 不依赖其程序集类型。
敌方意图描述符保存实战最终解析的前景/背景图标，渲染器不维护第二套配置回退路径。
隐式动作也按原生数据类型进入同一契约：普通 `Card` 使用卡牌描述符，`EnemyCard`/`PartnerCard`（包括
Terrias 精灵意图适配器）使用意图描述符；录制完成前会拒绝事务类型与描述符目录不一致的文档。
专用 URP RendererData 只深克隆显式兼容的 Feature；FullScreen Pass 由回放 Color-input Pass 提供
RenderGraph 中间颜色，旧式主相机 UIBlur Pass 不进入回放，未知活跃 Feature 在执行前拒绝。游戏原生
Renderer Feature 对象和 active 状态始终不修改。
第一帧离屏 render、非黑像素 readback 与游戏主渲染 frame barrier 都通过后才显示。journal 保存
实际观察时间；Truth time 随 sequence 单调，Presentation event time 可因实体延迟绑定而晚到，卡牌、
角色、相机、冲击和状态提交允许重叠，不再用固定相对延迟重新编排。
池化卡牌的轨迹在共享 CardPresentation Reset、失活或实例重绑时结束；对象回池后继续存活不再触发
普通的 30 秒观察超时。
定位从 truth checkpoint 恢复权威状态，再按目标时间重建实体 binding、已提交视觉状态和仍有效的
暂态表现。

## `.aurareplay` v17

包包含 manifest、canonical document、双 lane chunk、配对 checkpoint、内容寻址附件和分析摘要。
导入拒绝 DLL、脚本、路径穿越、未知必要能力、超预算数据、哈希错误和重复 document root。

包内包含记录玩家当时可见的手牌，因此分享前应按“玩家视角战斗记录”处理；它不包含抽牌堆顺序或
不可见对手数据。

## MP4 导出

- 视频与交互回放使用同一个 v17 battle world 和固定 RenderTexture；
- 固定帧时钟驱动画面；内嵌 PCM cue 可离线混音，只有资源引用的 cue 不阻塞视频导出；
- MP4 与 canonical 文档分开存储，不改变 document root。

## 旧记录

数据库 schema 14 审计并退役所有 protocol `< 17` 的 structured replay。摘要、分析、收藏、标签、
备注和已验证 MP4 保留；pre-v17 document/chunk/checkpoint/POV/DOM 与无引用资产删除。不存在旧播放器、
sidecar 或双 writer。

## 验证

自动门禁覆盖协议、可见状态 reducer、表现指令、持久化恢复、SQLite、包、媒体、网络、共享扩展和
发布结构。实机验收覆盖背景、HP、BUFF、意图、手牌、牌堆、Partner/精灵、动作、伤害字、特效、
音频、定位、倍速、视频和退出后的下一场战斗。
