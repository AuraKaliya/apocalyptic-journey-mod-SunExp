# Replay Document v17：隔离原生表现、可见状态与实测轨道

## 产品契约

v17 是唯一结构化回放协议和播放器。它保存固定玩家视角下的可见战斗事实，并使用录制时实际
发生的原生/MOD 表现事件和稀疏轨道重新演绎；不重新执行战斗规则，也不复制 Unity 对象树。

非协商约束：

- 不调用 `FightInit.Init`、`FightUI.Init`、`CardItem.Init`、脚本、AI、奖励、存档、Command 或 RPC；
- 使用完整原生 prefab 层级、材质、SortingGroup、Mask、字体和安全 view API，不手工仿制卡框、
  血条或战斗 HUD；
- 状态、表现和检查点只有一套权威事件流；
- MOD 通过 `AuraReplayPresentationRuntime` 注册 owner-qualified 模块并发布纯数据事件，
  AuraToolsExp 不引用内容 MOD；
- 缺少必要游戏 build、表现模块、shader、renderer capability 或资源时拒绝结构化播放，保留摘要、
  分析和已验证 MP4，不静默漏画面。

## 为什么不能重跑原生战斗

原生 `FightInit.Init` 会创建并初始化 `FightUI`、执行 `RpcLoadRoles`、初始化牌区、BGM、敌人、职业、
祝福、遗物、技能和初生卡。`StatusManager.Init` 会注册 UI、事件、动作槽和动态状态；`CardItem.Init`
会绑定战斗身份、标签、脚本执行器并运行刷新逻辑。它们不是只读 presenter，不能进入回放。

v17 只复用已证明为表现用途的边界：原生 prefab、`ICard.SetCardStyle`、`ICard.SetPureMsg`、数字字形、
资源目录、效果 prefab、共享卡牌视觉生命周期以及内容 MOD 注册的回放 renderer。

## 权威数据流

```text
真实战斗
  ├─ Truth observer
  │    baseline -> typed visible delta -> truth checkpoint
  └─ Presentation observer / shared MOD bus
       native calls + observed time + transform/material tracks
                         │
                         ▼
                 Replay Document v17
                         │
          nearest checkpoint + ordered events
                         │
                         ▼
       isolated native-prefab battle world / manual clock
                         │
                 interactive replay / MP4
```

### Truth lane

保存实体身份/代、阵营、HP、防御、BUFF、意图、本机可见手牌、牌区计数、Power、技能 HUD、结果和
owner-qualified 扩展状态。稳定边界比较规范化前后状态，写入 typed operation；检查点由同一 reducer
生成，只是定位优化，不是第二事实源。

### Presentation lane

保存原生观察时间，不再把动作改写为固定的“卡牌→角色→命中”串行时间：

- 卡牌丢弃、返回和焚毁记录实际 Canvas 位置、尺寸、旋转、比例、透明度和 CardBurn `_Fade`；原生
  中央卡牌以 Destroy 结束轨迹，池化战斗卡牌以共享 CardPresentation Reset、失活或实例重绑结束；
- 角色动作/受击记录实际世界位置、root/body 比例、body 偏移、sorting layer/order 和相机状态；
- 状态提交保留其实际观察时刻，允许与卡牌、角色、伤害字和效果重叠；
- 原生效果从 `EffectBase` 目录解析 prefab 和持续时间，但不调用 `EffectBase.Play`；
- 音频保存已解析资源或规范 PCM cue；
- MOD 事件保存模块身份、schema、event id、actor/owner/target、规范 JSON、持续时间和
  persistent/transient 语义。

SourcePresented 只表示动作来源，不授权生成一张展示卡。技能和被动仍可引用卡牌描述数据，
但只有带实际采样轨迹的 CardMotionPresented 才生成运动中的原生卡牌视图。不同卡牌的重叠
轨道独立保留，显示实际费用，并按各自结束时间释放；不使用合成飞行弧线替代缺失采样。

CanvasPosition 的 X 以屏幕中心为零，Y 从屏幕底部计量，均按录制分辨率保存。播放先转换到
捕获 Canvas，再换算到原生手牌或中央容器的局部空间；不能直接把它作为容器 anchoredPosition。
UI 尺寸使用同一游戏的原生 CanvasScaler 合同，保留预制体支点和相对比例。

动作采样在原生调用前后建立观测，并在调用后读取原生最终采用的 animatedState；
不能仅用卡牌 Action 字段代替实际动作状态。原生角色动作代次发生替换时，前一个观测结束，
不把后续动作继续追加进旧轨道。整个 FightUI 的 NowAnimation 标志不再决定单个动作的结束。

动作来源的 descriptor 目录由原生数据类型决定，而不是由 MOD 名称、内容 ID 或资源路径猜测：
`Card` 使用卡牌 descriptor；`EnemyCard` 和 `PartnerCard` 使用意图 descriptor 与 `Intent` 事务。
只有卡牌 descriptor 承担卡面/卡框资源契约，意图 descriptor 保存已解析的前景与底图。文档封存前会
交叉验证事务类型与 descriptor 目录；未知原生动作类型或交叉引用错误都会拒绝本次录制。

同一事务仍保留 phase 标签用于类型校验，但 phase ordinal 不重新安排播放时间。Truth lane 的
`TimeTicks` 随全局 sequence 严格单调；Presentation lane 的 `TimeTicks` 是实际观测事件时间，允许
事件因实体/owner 延迟绑定而以更高 sequence 晚到。播放器统一按 `TimeTicks + DelayTicks`、再按
sequence 确定性排序；v17 正常录制的 `DelayTicks` 为零，旧式固定 choreography 已删除。已经进入
耐久 batch 的事件不可再改写；开放事务和仍在采样的动作/卡牌轨迹共同形成 durability watermark。

## 隔离原生表现宿主

播放器创建独立 RenderTexture、手动 Camera、完整原生 `UI/FightUI` 外壳和专属捕获 Canvas。
prefab 先在 inactive quarantine root 下实例化，移除玩法/输入 MonoBehaviour，随后才进入活动层级。
因此保留：

- `SortingGroup`、SpriteRenderer/MeshRenderer 的原生材质与 render queue；
- Canvas/Mask/RectMask、TMP 字体/材质、布局和附加 shader channel；
- `FightUI` 的 Power、技能、抽牌堆、弃牌堆、回合提示和原生容器；
- `StatusBarUI`、HpItem、BuffBarUI、BuffItem、ActionContent/ActionMsg 的 prefab 结构；
- `UI/CardItem` 的 Mesh/Image 分支、费用数字、动态文本、附魔、冻结主题/皮肤/效果。

所有 Graphic、Selectable、Collider 和 raycaster 均不可交互。回放从不注册到 UIManager、
FightManager、FightCardManager 或 RoleTable 的运行时索引。

## MOD 表现协议

`AuraReplayPresentationShared` 是共享权威边界：

- `IAuraReplayPresentationModule` 声明 owner/type/schema、build、portability 和 renderer capability；
- capture lease 为事件分配 battle session、sequence 和 Stopwatch 时间，在一场战斗内去重，并在共享
  边界把 payload 解析为 canonical JSON：对象键递归按 Ordinal 排序、数组保持顺序，重复属性、尾随
  内容、超深度和超预算直接返回 `Invalid`；
- `IAuraReplayPresentationRendererModule` 可创建仅表现 renderer，接收手动逻辑时钟；
- provider-required 模块在播放时按 owner/type/schema、portability 和 renderer capability 精确匹配。
  SchemaVersion 版本化事件数据及解释规则，RendererCapability 版本化渲染合同；破坏性变化必须
  更新对应声明。BuildIdentity 保留录制时的构建来源，用于诊断，不作为兼容键：重新编译或依赖
  变化可能改变整个 DLL 的 MVID，但不代表表现模块合同改变。portable 模块只使用通用原语和可用资源；
- 兼容检查不重写已封存记录的 BuildIdentity、payload 或 root；游戏资源版本、数据完整性、
  必要模块和实际 renderer 可用性仍独立校验；
- actor 尚未进入 truth state 的事件成为有界 pending obligation，在实体物化后按原始 capture
  sequence 排空；Presentation event time 保留发布时刻。排空只有在事务、Journal 和 Ledger 全部提交
  后才移除事件，失败会原子回滚；终局仍未排空会拒绝 Ready，不能只记 warning。

卡牌视图可能由内容 MOD 保留并回池，AuraToolsExp 不以“C# 对象仍存在”推断动画仍在播放，也不引用
内容 MOD 的池类型。`AuraCardPresentationRuntime.RequestReset` 是跨消费者池回收的权威结束信号；
visual root 与 source instance 必须精确匹配。30 秒 watchdog 只用于真正没有 Destroy、Reset、失活或
重绑的异常活动视图，不能作为普通动画完成计时器。

当前 Terrias 接入：

- 精灵：出场/退场、owner attached proxy、纵向血条、元素徽章、意图和动作聚焦；
- 投影：原生 Partner/Status、HP/BUFF/动作轨迹由通用 recorder 记录，`ProjectionDeployment` 保存扩展
  状态，portable `ProjectionBattlePresentation` 保存出退场和专属意图；
- Star Score：持久 HUD snapshot 使用 Terrias renderer，并由回放逻辑时间驱动 shader；
- Wuna orbit fire：可见性和动作 boost 由 Terrias renderer 重建，`ConfigureReplay` 绕过玩家性能开关，
  几何/材质时间由回放逻辑时钟驱动。

owner-attached v1 的尺寸和位移以 1080p 参考像素定义，透视和正交相机均在 owner 深度上进行
屏幕/世界换算。独立聚焦脉冲与底层状态轨道各自推进，底层的单帧轨道不能清除聚焦脉冲；
攻击/干扰朝已记录目标移动，支援沿上方移动。DetachedRightVertical 保留声明的血条缩放，
使用独立的紧凑意图比例与原生数字字形，意图间距按原生条目宽度计算。

## Renderer 与首帧门禁

回放 renderer 从当前游戏默认 RendererData 克隆基础 2D/材质/光照配置，但 Renderer Feature 采用显式
兼容性 profile，而不是浅复制后执行全部主相机 Pass：

- 可保留 Feature 必须克隆为回放独占的 ScriptableObject，不能与原生 Renderer 共享 `Create/Dispose`
  状态；
- 当前 `FullScreenPassRendererFeature` 以独占克隆保留，回放自有的无绘制 Color-input Pass 让
  `Renderer2D` 在记录 RenderGraph 前创建中间 `cameraColor`；
- 只实现旧 `Execute`、没有 `RecordRenderGraph` 且服务主相机全局 UI 模糊的 `UIBlurGrabPassFeature`
  不进入回放 profile；
- 未知活跃 Feature 在渲染前明确拒绝，不能冒险执行，也不能静默漏掉；
- inactive Feature 可以省略。native renderer 数组既有槽位、Feature 实例和 active 状态均不修改。

旧的“`Object.Instantiate(RendererData)` 即等价于独占全部 Feature”路径已删除；它既没有证明 Feature
对象所有权，也没有证明专用相机满足每个 Pass 的颜色/深度/RenderGraph 输入契约。

首帧门禁顺序：Feature profile/资源/模块验证、离屏 render、64×36 像素 readback、非空/非黑/非平面
统计、游戏主渲染 frame barrier、显示提交。离屏调用本身必须无 Command/Unity/RenderGraph 异常；
像素门禁只验证已成功完成的 render，不能把执行期间的引擎异常降级为黑屏。只要 shader、材质、排序或
renderer profile 产生黑画面，即使 `Camera.Render()` 没抛异常也不能进入 Active。

## 定位、倍速和导出

定位先恢复最近 truth/presentation checkpoint，清理所有暂态卡牌、效果、音频和 MOD renderer，
再应用后续 truth，并按目标时间重建：

- 最近相机状态；
- 仍在持续时间内的卡牌/角色/伤害字/效果/音频；
- 目标时间之前所有 persistent MOD 事件的最终投影。

交互回放和 MP4 使用同一个 battle world。播放器和 provider renderer 都接收整数微秒逻辑时钟；
倍速只改变逻辑时钟推进量，不改变文档事件时间。

## 持久化、迁移与兼容

状态依次为 `Recording -> Finalizing -> Ready`。增量 batch 只包含 durability watermark 之前的不可变
事件：任何开放事务、动作世界轨迹或卡牌 Canvas 轨迹都会阻止其自身及更高 sequence 进入耐久前缀。
它是崩溃诊断/恢复边界。单一后台存储 FIFO 按 seed、增量 batch、完整 Finalizing draft 的顺序提交；
终局在主线程只移交封闭后的文档，不复制全量 JSON 或执行数据库写入。队列满时保留输入并由常驻
驱动重新申请调度，不回退到主线程执行。只有草稿提交成功才允许规范化、生成检查点/根哈希并原子
保存 Ready；关闭录制不取消已移交的保存。自动清理排除仍在 Recording/Finalizing 的记录。
草稿提交前发生进程崩溃时，保留已落库的录制前缀并标记 Incomplete；草稿已提交时沿用现有恢复器。

手牌创建/排布仅提出合并的状态采集请求，帧边界和下一动作之前结算；逐视图移动、到达与即时消耗
的表现归属不变。canonical 发布先检查主机是否有其他房间成员；没有接收者时不建立传输对象，
有接收者时在后台编码已经封存的文档，发送前再检查成员。保存和网络入口不再深复制整个封存文档。

SQLite schema 14 执行 `replay-pre17-to-v17-native-presentation-cutover`：

- protocol `< 17` 的结构化文档、chunk、checkpoint、capture、POV/DOM 和无引用资产退出运行路径；
- 摘要、分析、收藏、标签、备注和已验证 MP4 保留；
- 迁移先写带 SHA-256 的报告，再事务更新数据库，最后清理资产并把账本从 PendingCleanup 提升为
  Applied；
- v16 缺少原生 prefab 契约、共享表现事件和实测轨道，不能伪造升级为 v17；
- 生产中不存在 v16 writer/player 或双协议兼容播放器。

已经封存但由缺陷 writer 把 `EnemyCard`/`PartnerCard` 写成卡牌 descriptor 的 v17 文档也不做播放端
修补：错误文档没有保留完整意图字段，原地转换既不能无损也会破坏根哈希。播放器继续拒绝该记录，
修复后的 writer 只为新战斗生成正确文档。

## 验收

自动门禁覆盖 reducer、hash、checkpoint/seek、late-bound presentation event time、durability watermark、
真实重叠轨道、共享模块注册/去重/清理、嵌套 payload canonicalization、重复字段/深度/预算拒绝、数据库切换、
包导入导出、renderer lease、像素预检策略、产品消费者编译和共享 DLL 一致性。
卡牌视觉生命周期矩阵另行覆盖精确 Reset、错误 root/source、Destroy、失活、实例重绑和真正卡住的 watchdog。
动作来源矩阵覆盖 `Card`、`EnemyCard`、`PartnerCard`、未知类型、精灵意图适配器的目录路由，以及
意图事务错误引用卡牌 descriptor 的封存拒绝。
Renderer 矩阵覆盖 Feature 保留/排除/未知拒绝、Feature 深克隆所有权、FullScreen 中间颜色声明、
UIBlur RenderGraph 能力探针、相机/target lease 和原生 Renderer 非修改；真实游戏仍必须执行首帧、
正常帧屏障、定位、导出、关闭重开和下一场战斗。

实机必须对同一场战斗设置语义截图锚点并比较实战/回放：背景、完整 FightUI、血条颜色、BUFF、意图、
手牌卡框/卡面/文本/费用/附魔/效果、丢弃/焚毁、角色与受击、相机、精灵、Star Score、Wuna 环火、
伤害字、倍速、定位、MP4、退出后下一场战斗。还必须确认无脚本/RPC/奖励/存档副作用，且退出后无
Camera、Canvas、material、provider renderer、capture lease 或静态引用残留。
