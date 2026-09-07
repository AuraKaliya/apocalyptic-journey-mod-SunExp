**Terrias / AuraToolsExp 结构、设计与流程审查 — 2026-09-06**

本轮围绕上一轮性能修复，检查组合入口、领域分层、原生接入、后台任务、持久化、UI、资源、联机和测试发布。对 Terrias 的 590 个、AuraToolsExp 的 314 个非生成生产 C# 文件做了全局检索，并沿下列关键调用链深入核对；不是对约 24 万行源码逐行证明，也没有新增实战或联机验收。共享层按涉及的边界检查。未修改产品代码或游戏文件。

已有的共享动作/生命周期路由、发送者绑定、模块目录、资源清单、不可变回放协议、发布回滚和 Unity 测试值得保留。主要问题是：执行位置、失败结果、所有权、完整交付依赖尚未成为所有入口一致遵守的契约。

“代码路径明确”表示可从当前调用顺序确定触发条件和后果，不表示本轮已在游戏中复现；“结构风险”需要负载或故障注入进一步定量。

| 编号 | 优先级 | 主要责任 | 发现 |
|---|---|---|---|
| A01 | 高 | 发布/部署 | DLL 一致，仍有已安装资源依赖缺失 |
| A02 | 高 | AuraTools / 联机回放 | 接收完成后忽略入队失败，可能静默丢记录 |
| A03 | 高 | Shared / 调度 | 替换任务先取消旧任务，再检查新任务是否能入队 |
| A04 | 高 | AuraTools / 存储与资料库 | 属性访问、UI 回调仍可执行重 I/O；长锁会把后台负担传回 UI |
| A05 | 高 | AuraTools / 配置 | 持久化失败后，操作层仍可能返回成功 |
| A06 | 高 | Terrias / 幂等账本 | 损坏/读取失败与空账本被混为一谈 |
| A07 | 中 | Terrias / 资源 | 纹理预热类型与原生加载契约不符，且空结果被计为成功 |
| A08 | 中 | AuraTools / 新增可靠队列 | 内存积压没有数量/字节预算 |
| A09 | 中 | Terrias / 分层 | 64 条例外仍保留跨层和反向依赖 |
| A10 | 中 | Terrias / 初始化 | 独立失败隔离没有配套表达必需依赖的就绪状态 |
| A11 | 中 | 两产品 / UI | 部分界面把状态读取、动作和整页重建绑定在一起 |
| A12 | 中 | 两产品 / 网络语义 | 网络已启动、主机身份和存在远端接收者的概念混用 |
| A13 | 高 | 测试/构建流程 | 单元、生产组合、原生语义和性能证据之间仍有断层 |
| A14 | 中 | 可观测性 | 日志名称与实际测量含义不够一致 |

**A01：完整交付需要覆盖配置、资源与固定的源码快照。**

本轮核对时，游戏目录的四个 Entry/shared DLL 与项目包完全同哈希。但项目存在 `AuraToolsExp/SharedResources/EventCg/event-cg.art.json`，游戏 Mods 下对应文件不存在。当前 CG 解析器明确读取该文件，读取失败后目录为 null，依赖此目录的背景解析返回 null。这里确认的是安装缺项；没有声称已在游戏中观察到全部 CG 失败。

证据：[解析器](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/Cg/AuraToolsCgSceneAssetResolver.cs:69)、[项目素材清单](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp/SharedResources/EventCg/event-cg.art.json)、[上轮部署记录](D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/replay-performance-deploy/14ff9c4c3f334c4daede0c887800ee0e/deployment.json)。上轮由本任务同步 DLL，因此这个依赖检查缺口也属于本任务的交付流程问题。

当前发布清单只记录 entry/shared 文件及哈希。构建直接消费共享工作目录，没有在脚本里固定被测试的源码、配置和素材快照。上轮 CG 文件并行变化曾让相邻两次编译得到不同结果；单一发布脚本和事务回滚不能证明测试、构建、安装来自同一组输入。

建议：继续保留单一发布者，在发布前冻结工作副本或生成明确的输入快照；清单记录受影响配置、素材、Managed 指纹、测试收据和 DLL。部署在实际安装目录核对整组依赖，保留回滚。相关入口：[构建](D:/Project/Apocalypic-journey-mods-creator/ModExp/tools/Build-MainSharedConsumers.ps1:17)、[发布清单](D:/Project/Apocalypic-journey-mods-creator/ModExp/tools/Publish-MainSharedConsumers.ps1:70)。

**A02：客户端收到完整回放，并不保证它进入保存流程。**

接收端在分片完整后从 `Transfers` 移除缓存，再调用 `QueueReplicaCommit`。后者忽略共享调度器的 bool 返回值。如果队列满，既没有保存任务，也不会触发后台失败回调；当前函数没有保留待提交对象或安排重试。

证据：[移除接收缓存](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Network/ReplayNetworkAuthorityV17.cs:266)、[忽略接纳结果](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Network/ReplayNetworkAuthorityV17.cs:316)。这是代码路径明确的条件性丢失，不是本次单人日志里发生的现象。

建议：接收、完整性检查、接纳、耐久提交分别有状态；交给可靠存储所有者之前保留输入。压力测试应验证：共享队列满时收到完整回放，恢复容量后仍保存一次并得到明确结果。上轮修复覆盖的是主机录制和发送准备，这个接收入口仍未统一。

**A03：共享调度器混合了“必须完成”和“只要最新”的工作。**

`Queue` 会先取消同 key 的旧任务，再检查容量。旧任务正在执行、等待队列又已满时，新请求可返回 false，而旧任务已被取消。对刷新结果，这需要显式的失败策略；对保存和复制，这种隐式替换尤其危险。

证据：[取消和容量判断顺序](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraSharedCore/AuraSharedBackgroundWorkScheduler.cs:109)。共享队列已有并发/容量限制和 owner 隔离，这些应保留。

建议：区分可靠顺序工作与可替代刷新；明确 Accepted、Replaced、BackPressure、OwnerStopped 等接纳结果。替换应在新任务确定被接纳时提交。工作者错误、入队失败、取消是三类不同结果，调用者不能只处理其中一种。

**A04：存储边界还会让主线程承担数据库和序列化工作。**

`MatchRecordStorage.Database` 的首次属性访问会创建数据库、迁移和恢复 Finalizing 文档。初始化直接访问它，所以重型恢复仍可能发生在主线程。资料库的导出选择回调逐条同步调用 Export；文件选择回调同步 Inspect；页面查询与计数也直接访问数据库。

证据：[属性中的恢复](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/Storage/MatchRecordStorage.cs:12)、[同步导出](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/MatchRecordLibraryPresenter.cs:510)、[同步检查导入包](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/MatchRecordLibraryPresenter.cs:584)。

写库也还有可避免的工作：为生成骨架而深复制整个回放后清空日志；在数据库锁内编码检查点计算长度；保留数量清理在同一把锁内删除媒体和扫描附件。UI 的 Count/LoadPage 要获取这把锁，因此后台执行本身不能保证 UI 不等待。

证据：[保存与锁内编码](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/Storage/MatchRecordDatabaseV17.cs:73)、[清理与计数共用锁](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/Storage/MatchRecordDatabase.cs:400)。

建议：显式初始化/恢复状态；资料库通过后台用例返回读模型。构造最小骨架，编码结果复用，缩短数据库临界区。需要事务保护的文件清理使用明确的提交后清理账本，不能简单挪出锁制造删除竞态。重点测锁等待、分配量、草稿/正式保存、UI 响应，不以后台总时长替代冻结时长。

**A05：配置写入结果没有完整传递到操作层。**

页面直接修改公开的可变配置对象，再调用 void Save。`SaveModuleSetting` 保存失败只记录失败并返回；通用模块 `SetEnabled` 调用 setter 后直接返回“已启用/已关闭”，宿主继续应用当前配置。磁盘失败或 revision 冲突时，可以出现内存和运行时采用新值、磁盘保留旧值，而操作仍报成功。

证据：[先修改后保存](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAutoBattleSettingsPage.cs:75)、[失败返回](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Config/AuraToolsConfigService.cs:746)、[无条件成功](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Modules/AuraToolsBuiltInModules.cs:980)。只读 schema 的宿主检查已有保护，但不能覆盖磁盘等写入故障。

建议：统一返回配置提交结果，以候选配置完成校验和持久化，成功后替换生效快照并通知；失败保留已提交状态。日志配置的 `TryCommitLogging`、预设的提交校验已经提供可借鉴的局部模式。旧聚合配置镜像的维护/退役也应明确，避免一次 UI 操作持续写两种布局。

**A06：幂等账本不应把损坏解释为“从未执行”。**

`EndlessAbyssRunLedger.Load/Parse` 在读取或反序列化失败后返回空文档；`TryClaim` 会把该空文档作为可领取依据，添加 key、保存并返回 true。错误与首次创建共用结果，会使幂等保护失效，并可能覆盖原有损坏内容。该结论是故障输入下的代码路径，本轮没有改写用户存档进行复现。

证据：[领取判定](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Mechanics/EndlessAbyssRunLedger.cs:48)、[失败变空](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Mechanics/EndlessAbyssRunLedger.cs:104)。

建议：区分不存在、有效、损坏、暂不可读。幂等/进度数据损坏时保留原始数据并停止新的 claim；恢复必须有明确依据。对显示缓存允许空降级，对奖励账本使用相同降级方式则会改变游戏结果。

**A07：资源预热没有验证真实成功。**

预热调用 `Load<Texture2D>`。当前匹配的原生 `CustomLoad` 对图片只在 `T == Texture` 时返回纹理；其它类型走 Sprite 创建后 `as T`。以 Texture2D 请求时可以解码完图片却得到 null。预热任务是 `Action<string>`，调用完成就计 ItemLoaded，空结果不会传回预热状态。旧日志中 `more_dimensions.png` 的 Texture2D 加载耗时且 hit=False，与此一致。

证据：[预热类型](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Hooks/TerriasResourcePreloader.cs:61)、[成功计数](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Hooks/TerriasResourcePreloader.cs:232)、[匹配的原生图片加载](D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch.Core/ResourceLoader.cs:350)。本轮重新确认游戏和仓库 Witch.Core.dll 哈希相同。

建议：由 GameApi 提供符合原生契约的纹理加载入口；预热返回 Loaded/Missing/Failed 及资源身份。`Background` 帧阶段仍运行在主线程，单张图片解码不可抢占；延后下一次加载只能控制密度。还需离线尺寸/格式处理及可用的异步读取路径。该问题主要解释预热阶段的顿挫，不能单独解释战后长停顿。

**A08：新增可靠队列仍缺少积压预算。**

上一轮增加的 `ReplayWorkQueue` 保留被共享调度器拒绝的工作，避免丢写入和主线程同步退回。但它的 `Queue<Func<Action>>` 没有数量/字节上限。慢盘、保存长期赶不上产生速度或连续战斗时，会保留越来越多的批次、文档与闭包。当前日志没有证明已经出现内存耗尽。

证据：[队列与入队](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/Recording/ReplayBackgroundWork.cs:23)。

建议：补充待写字节数、最老等待时间、当前保存阶段和明确的容量策略。容量策略要保护已接纳的可靠工作，不能静默丢掉记录或重新阻塞战斗线程。该需求也说明可靠任务应逐步形成共享契约，避免其它模块各写一套近似队列。

**A09：Terrias 的分层尚未完成实际切换。**

门禁报告 590 个文件、37 条层级边、64 条登记例外。64 条例外统一使用 owner `TerriasArchitecture` 和里程碑 `architecture-layer-cutover-v2`。这能控制新增债务，但不能说明旧债已清除。

具体例子：`GameApi/BuffApi` 包含角色状态分类和冒险余烬提交；`Mechanics/ProjectionSummonService` 同时负责本地/网络请求、发送者校验、座位和回合事务、Unity 对象创建、结果广播及退款。其问题是修改规则时必须同时推理网络和场景生命周期，不只在于文件较长。

证据：[例外账本](D:/Project/Apocalypic-journey-mods-creator/ModExp/tools/architecture-boundary-exceptions.json:1)、[BuffApi 跨职责提交](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/GameApi/BuffApi.cs:834)、[投影服务](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Mechanics/ProjectionSummonService.cs:45)。

建议：按投影召唤等完整能力收束到 Application，用 Mechanics 计算规则、GameApi 操作宿主、Network 处理传输。每批例外指定可验证的关闭条件和对应测试。保留单一共享 DLL 的现有约束，先做逻辑边界和测试隔离，避免用一次大规模目录调整代替能力迁移。

**A10：初始化应区分独立步骤与必需前置条件。**

Terrias Entry 使用统一 RunStep 隔离异常，防止一个可选功能拖垮全部加载，这是合理的。但底层 RunStep 的 bool 结果被入口包装丢弃；共享基础或路由初始化失败后，依赖它的步骤仍继续。最后的 loaded 信息也不是所有能力可用的证明。

证据：[组合入口](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Entry.cs:25)、[结果被忽略的包装](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Entry.cs:55)、[底层结果](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraSharedCore/AuraSharedHooks.cs:99)。

建议：必需依赖失败阻止对应能力激活，可选依赖失败只降低该能力；发布 Ready/Unavailable/Degraded 及原因。AuraTools 的模块状态系统已有这种表达，可以共享原则。保持对未知原生能力的拒绝策略，不能靠吞错或猜测宿主行为恢复“可用”。

**A11：UI 的局部操作经常触发过大的刷新范围。**

资料库搜索、选中、元数据编辑均可调用 Build，先清掉整个 body 再查询、重建。精灵面板已有虚拟网格，这是可保留的优化，但 Refresh 仍同时读取收藏、更新网格、重建身份/详情/队伍/操作区域。部分 AI 页面另有多个轮询组件分别取状态并更新控件。

证据：[资料库 Build](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/MatchRecordLibraryPresenter.cs:82)、[选中即重建](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/MatchRecordLibraryPresenter.cs:473)、[精灵 Refresh](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Hooks/Ui/SpiritManagementPanel.cs:308)。

建议：分离查询读模型、UI 选中状态和动作结果，按变化刷新区域；已有虚拟化与池复用继续使用。轮询应共享同一版本的状态快照，不同刷新源避免重复读取/格式化。该部分尚需用大资料库和大收藏定量测量，不能把类大小直接换算成卡顿程度。

**A12：网络会话、权威身份和同步需求需要分别表达。**

`DamageMeterNetworkRuntime.IsMultiplayer` 判断 `PlayerManager.Instance != null`；它在单人也可能成立。回放又借用伤害统计的网络判断，使两个功能的网络语义形成间接依赖。上轮已在 canonical 发送前添加远端接收者判断，但整体概念仍混用。

证据：[现有判断](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/DamageMeter/Network/DamageMeterNetworkRuntime.cs:37)、[回放依赖](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/ReplayV17/Network/ReplayNetworkAuthorityV17.cs:91)。

建议：分别表示会话是否启动、当前节点是否有权威、是否有远端接收者、谁是语义所有者。不能全局把 IsMultiplayer 改成人数大于一；单人使用的本地服务器命令仍可能是必要的权威路径。发送完成也需要与远端接纳/保存完成区分。

**A13：测试需要按证据层次和变更责任组织。**

行为测试项目使用 net8.0 并直接编译挑选的生产文件；产品使用 net472/游戏 Managed。回放核心和存储有覆盖，但生产 Recorder/网络组合未完整纳入同一行为运行。测试用帧调度器会立即执行延迟动作；Unity 手卡夹具执行生产适配器，但仍使用替代的宿主和 journal sink。因此这些测试分别证明局部性质，不能合起来当作真实对局验证。

证据：[源码链接](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev.Tests/AuraToolsExp-Dev.Tests.csproj:116)、[即时调度替身](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev.Tests/BackgroundSchedulerTestStubs.cs:20)、[Unity 宿主夹具](D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp.ReplayUnity.Tests/Assets/Tests/ReplayHandCaptureStubs.cs:34)。上轮确实出现“行为编译通过、生产组合编译发现 API 参数不符”的情况。

`Test-AuraToolsExp` 又串联了工具行为、模型注册集成、资源/CG 数量和大量源码形状检查。回放检查曾被奥莉米娅 CG 加入后过时的总数挡住。现有影响矩阵是好的基础，应让子检查可以独立选择，整包检查在发布阶段聚合。配置/资源校验以真实引用、身份唯一性和完整性为主；源码扫描适合禁止越界 API，不能替代行为验证。

建议：保留纯规则单测，补齐生产组合编译、可控的异步/故障时序测试、Unity 原生适配测试和实战矩阵。已有 benchmark 需绑定可重复工作负载、分配量和相同最终状态，而不把断言数量作为性能标准。测试收据应绑定源码/配置/资源/Managed 快照；避免同一工作区在测试和发布之间混入无关改动。

**A14：日志字段应表达其真实含义。**

`TerriasResourceCache` 的慢加载字段 hit 实际传入 `loaded != null`，表示取得对象而不是命中缓存。预热的 Background 是主线程帧阶段；回放 Ready 是格式校验与保存状态；窗口 totalMs 是累计耗时。将它们理解为缓存命中、后台线程、实时流畅和单帧时间都会误导诊断。

证据：[hit 参数](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/GameApi/TerriasResourceCache.cs:43)、[帧阶段与同步加载](D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Hooks/TerriasResourcePreloader.cs:193)。

建议：固定区分 cacheHit / loaded / failed，入队 / 提交 / 已保存，以及 mainThread / worker、等待 / 执行 / 应用耗时。上轮新增的 terminal handoff 和后台分段计时可继续扩展，同时补数据规模和队列积压，保留实际帧时间的独立证据。

**本轮验证记录与讨论顺序**

| 本轮执行的检查 | 结果 |
|---|---|
| Test-TerriasArchitecture | 通过；590 files、37 edges、64 exceptions |
| Test-SharedArchitectureGuidelines | 通过；shared 扫描 327 files |
| Test-ContentToolSharedBoundary | 通过；扫描 1090 files |
| Test-SharedWriteEntrypoints | 通过；扫描 991 files |
| Test-NetworkRpcAuthority | 通过；扫描 4150 files、4 类 server-bound marker |
| 游戏与项目四个 DLL 哈希 | 相同 |
| 新事件 CG 素材清单的安装存在性 | 项目有、游戏目录没有 |

这些是结构规则和当前文件状态检查。本轮没有重新构建/发布产品，没有运行 AI 训练，也没有宣称对新发现完成实战复现。

建议先处理 A01/A02/A05/A06/A07 的明确缺口，并为这些失败路径补用例；随后统一任务接纳、可靠保存与预算（A03/A04/A08），再按具体能力迁移分层和局部 UI 刷新（A09/A10/A11）。A12/A13/A14 应伴随各次能力修改同步落实。每轮都固定输入、检查对应失败边界、发布完整依赖并核对实际安装结果。
