# AuraToolsExp 版本兼容契约

## 总则

AuraToolsExp 的 `ModVersion` 只用于发布、诊断和界面展示，不作为联机
大厅的全局门禁。`ModConfig.json` 必须保持 `MustSame = false`。
`0.6.0` 是该兼容策略的首个发布基线；仍声明全局同版本要求的旧版客户端
无法由新版单方面解除其大厅门禁。

每个会跨客户端、跨进程或跨持久化版本读取数据的功能独立声明：

- 当前协议版本；
- 最低支持版本；
- 当前理解的必要能力；
- 不兼容时只影响该功能的降级路径。

运行时版本范围存在交集且远端没有声明本机未知的必要能力时，可以使用
双方重叠的最高版本。MOD 发布版本、游戏 Build ID、工具 Build ID 和回放
构建指纹不能替代该判断。

AuraToolsExp 自有 RPC 只在房间内所有玩家都启用了 AuraToolsExp 时发送。
缺少工具的玩家无法解析工具程序集中的命令类型，因此这种情况会把像素表情、
伤害同步和 MOD 清单传输标为降级，而不是冒险发送后造成反序列化或断线。
工具箱、本地作品和本地记录仍可继续使用。

机器可读清单位于 `AuraToolsExp/protocol.compatibility.json`。

## 功能边界

### MOD 配置同步

请求顺序为定向 v2、广播 v2、广播 v1，最后才退回大厅摘要。v1/v2
响应都可以进入当前清单读取路径；请求编号缺失时仅允许匹配当前唯一待处理
请求。发送者绑定、清单大小、分块数量和 SHA-256 校验仍是硬门禁。

### 像素表情

当前读取器支持协议 v2。未来发送端可以声明一个包含 v2 的兼容范围，但
必须保留当前帧数组、色板索引和内容哈希能力。未知必要能力或未知色板只
拒绝这一条表情消息。帧间隔元数据允许安全范围内的旧值，显示时统一归一
到 v2 的 200ms 节奏，避免未被 v2 哈希绑定的时间数据影响播放。

### 伤害统计与结算展示

实时伤害网络协议保持 v4，因为旧 v3/v4 客户端都会精确拒绝对方版本，当前
没有足够的房间能力信息来安全选择双向协议。战斗快照、冒险聚合和历史数据
读取支持 v3..v4；低于 v3 的数据缺少当前所需语义，必须拒绝。历史 v3
数据可以参与迁移和统计，不再因为不是当前 v4 而被静默过滤。后续实时协议
升级必须先提供房间级协商，不能把单向可读取误报为联机兼容。

CG 展示统一使用信号注册表 v4 与处理后队伍场景协议 v1。冒险结算只向
展示链路提交有序角色 ID、`team-tableau.v2` 布局位置、程序主题、可选背景叠层和
逻辑资源 ID 等最小计划，不传输
原始伤害、排名、历史记录、本地路径或缓存键。事件 CG 独立采集当前冒险
参与角色，既不读取伤害账本，也不依赖 DPT 模块是否启用；伤害协议和 CG
协议可以分别演进。胜利场景在主机侧收敛为稳定 `outcomeReason`，点金手脱离、
仪式胜利、诅咒胜利和普通胜利由同一处理后场景计划传输；普通逃跑不映射为失败。

### 对局回放和分析

回放持久化协议和包协议固定为 `Replay Document v17` / `.aurareplay v17`，可读范围为
17/17。联机 replay authority 协议独立为 v1，并要求 causal transaction v2、固定视角可见
状态、resolved instruction stream、全局事务顺序、可见状态检查点、实测原生布局、sanitized
原生 prefab、完整 FightUI、共享 MOD renderer、实测重叠轨道、相机/视觉状态提交、原生卡牌视图、
原生 renderer profile、像素 readback、增量持久化和可恢复 finalization 能力。
任一房间节点缺少能力时，本场不发送 replay RPC，只保留摘要/分析；其它 AuraToolsExp 功能
不受影响。

v17 的全局 sequence 表示摄取和因果顺序。Truth `TimeTicks` 必须随 sequence 单调；Presentation
`TimeTicks` 表示真实观测时刻，允许 owner-qualified MOD 事件因实体物化而以更高 sequence 晚到。
播放统一按 effective presentation time、再按 sequence 排序。增量 batch 只能越过已经关闭的事务和
完成采样的轨迹；已持久化事件不可被后续钩子改写。此语义属于既有
`observed-presentation-timeline.v3` 能力的缺陷修复，不改变 v17 字段或读取范围。

联机只有主机写 canonical 文档。主机通过 sender-bound 公开命令和权威 status 观察远端动作，
终局封存后把 envelope 与精确 asset payload set 分块复制；客户端必须重组并重新验证两条
事件链、检查点、资产清单、truthRoot、presentationRoot 和 documentRoot，全部通过后才能
提交相同 `Ready` 记录。中断或不完整 transfer 不产生客户端第二 writer。

v17 播放器从固定视角状态、实测布局和资源指令创建隔离 battle world，不初始化真实战斗，也不执行玩法
脚本。游戏构建不匹配时拒绝播放，不回退到 DOM、sidecar 或旧播放器。录制依次经过
`Recording -> Finalizing -> Ready`；进程中断后只能从完整 Finalizing 草稿恢复，战斗中断则标为
`Incomplete`。验证失败的结构化记录保存为 `Rejected` 摘要；分析仍可从 canonical truth 文档
重建。所有 pre-v17 记录一次性改为 `SummaryOnly`，保留摘要、分析、收藏信息和已验证 MP4，
删除旧文档/chunk/POV/DOM/asset 引用；旧包不进入 v17 importer。

其它 MOD 的原生 Status/卡牌/FightUI 行为由通用观察器记录；原生表面之外的状态、布局和表现必须通过
Aura 共享回放 provider/module 以 owner-qualified 身份声明。Portable 模块使用通用原语；
ProviderRequired 模块缺少匹配 renderer/build 时拒绝播放，而不是静默漏画面。
共享表现发布边界负责把合法 JSON 递归规范化为 v17 canonical payload；重复属性、尾随内容、超深度和
超预算在进入 capture sink 前拒绝。内容 MOD 不承担属性排序责任，v17 文档验证仍独立要求 payload
已经 canonical。

池化战斗卡牌必须在回池/重绑前通过共享 CardPresentation Reset 释放表现世代。回放 recorder 按精确
visual root 与 source instance 关闭轨迹；原生非池化中央卡仍以 Destroy 结束。失活与实例重绑是通用
防线，固定 timeout 只拒绝真正没有到达任何生命周期边界的视图，不能替代 Reset 或被延长来掩盖泄漏。

原生动作来源按 `IDataConfig.Type` 进入唯一表现目录：`Card` 对应卡牌 descriptor，`EnemyCard` 和
`PartnerCard` 对应意图 descriptor/`Intent` 事务。MOD 名称、内容 ID 和图标路径不得参与分类；未知类型
以及事务/descriptor 目录不一致会使录制验证失败。这是 v17 writer 对既有原生意图语义的缺陷修复，
不改变字段或 17/17 可读范围。由旧缺陷 writer 封存的错误 v17 文档不做播放端转换，因为它没有完整
意图字段且转换会破坏根哈希；需要在修复版本中重新录制。

v17 的 `native-renderer-profile.v1` 要求回放使用独占 Feature 实例和显式兼容性 profile：保留的
FullScreen Pass 通过回放自有 Color-input Pass 获得中间 `cameraColor`；仅兼容旧渲染路径的主相机
UIBlur Pass 被明确排除；未知活跃 Feature 在首帧前拒绝。不得把 RendererData 浅克隆、Feature 类型
列表相同或反射成员存在误报为运行时兼容，也不得修改原生 Renderer 的 Feature 实例/active 状态。
该修复不改变回放文档字段或 17/17 读取范围。

### 卡牌视觉

CardVisual 当前协议范围固定为 9/9，渲染器为
`aura.card-visual.material-v7`，覆盖契约为 `frame/native-frame-v5`。静态卡框映射与动态
效果映射彼此独立：默认静态映射展开完整日耀/晨星卡包，动态效果只映射【炽冕崩落】
和四张【星辰序曲】。VisualBundle 必须由游戏同版本 Unity `6000.0.46f1`、
StandaloneWindows64 与 URP `17.0.4` 构建，并同时提供 UI Image 和 URP Mesh 材质。原生
卡框节点同时存在 Image/Mesh 时必须复刻原生 `ICard.SetCardStyle`：若
`Front/background` 存在 MeshRenderer，只修改对应的 `Front/FrontBack.MeshRenderer`；
否则只修改 Image。主题、动态材质、租约和恢复不得双写。两个材质都必须声明
`RenderPipeline=UniversalPipeline` 并提供约定的命名
Pass；运行时还要求已启用 SRP。Mesh shader 使用游戏内其它稳定 Mesh 特效同源的
`UnityCG.cginc` 顶点/采样契约，同时继续显式声明 UniversalPipeline 与 SRPDefaultUnlit，
避免把构建器专用的 URP include/常量缓冲布局带入游戏卡牌 Mesh。旧 renderer/coverage、
缺失材质、不兼容或不支持的 shader 直接拒绝应用，不使用旧 bundle fallback。发布构建
必须在真实 Direct3D11 设备上分别通过 ScreenSpaceCamera Canvas Image 与 WorldSpace Canvas
内 `MeshFilter + MeshRenderer` 的像素读回，并通过“动态材质 → 原生材质 → 新动态材质”
shader smoke。共享行为测试另行覆盖“退出材质覆盖 → 下层提前释放保持 pending → 顶层释放
后按 LIFO 恢复 → 新 generation 重绑”。运行时由共享协调器按 view root、generation、
Renderer 与 Material 的 Unity InstanceID 维护唯一材质栈，恢复完成前不得销毁动态材质；
回池时栈非空必须销毁该卡牌 view。任一路径出现 `NullGfx`、紫色像素、空白像素或池化卡
残留上一张卡的材质均不合格。

### 配置与模型

旧配置迁移到当前结构；高于当前结构的配置以当前内置默认值运行，并把
原文件标记为只读，旧代码不得覆盖。模块独立配置和聚合兼容配置都遵守该
规则。模块行会显示降级状态；对应总开关和衍生设置入口在只读期间不可
修改，避免“本次运行看似生效、重启后丢失”的假保存。

AI 模型的特征顺序、张量尺寸、动作空间、权重哈希、内容绑定和权威证据
继续作为硬门禁。实验模型的能力回退只限制启用等级，不影响文件检查和
候选信息展示。模型版本名称与训练来源只作身份和诊断信息。

## 发布检查

每次修改网络负载或持久化结构时，必须同时更新机器清单、行为测试和混合
版本场景。新功能不得重新把局部协议升级提升为 AuraToolsExp 全局同版本
要求。
