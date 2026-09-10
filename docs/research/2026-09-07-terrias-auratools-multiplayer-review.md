**Terrias / AuraToolsExp 多人联机审查 · 2026-09-07**

更新：用户已授权完整开发，八项修复已接入生产代码。当前实现、兼容边界、回归及发布结果见[开发记录](D:/Project/Apocalypic-journey-mods-creator/ModExp/docs/research/2026-09-07-terrias-auratools-multiplayer-development.md)。下文保留修复前的审查与逐项设计讨论；其中“尚未实施”“本轮未重建”描述的是审查阶段，不能作为当前开发状态。

本轮发现 **8 项需要修复的问题：4 项 P1、4 项 P2**。其中 6 项会在特定的正常操作、配置差异或重新组队流程中出现，另外 2 项属于联机权限边界缺陷。当前不宜据此认定两个 MOD 已通过多人联机验收。

审查基线为工作区源码，Git HEAD 为 `e3054a8e8124be0e6d88f346de1f739910af5920`。Terrias 清单包含 86 张卡、18 件遗物、57 个 Buff、5 个卡包；AuraToolsExp 当前注册 25 个模块，含内部服务。已枚举两个产品及共享 CG/音频中的 50 个自定义 RPC 类型。TestMods 不在审查范围内。

本轮完成模块入口和网络边界检查、重点调用链追踪、16 项现有自动检查，以及链接生产源码的边界复现。没有逐张卡完成双端实机操作，没有验证 Unity 中的实际渲染、真实延迟和断线过程。下文的“未发现独立问题”只表示所查边界未定位到额外缺陷，不代表全部行为已获实机证明。

`Witch.dll`、`Witch.Core.dll`、`AllScripts.dll`、`Mirror.dll` 与本地 v1.0.24831968 反编译输入的 SHA-256 全部一致，见[宿主指纹][fingerprints]。这证明本次源码调用链参考版本匹配仓库 Managed；没有核验实际游戏进程加载的文件。

**需要修复的问题**

**MP-01 · P1 · 本机战斗计数被用作跨机器战斗身份**

[CompanionAuthorityService][epoch] 用进程内静态整数计数，在进入战斗和清理时递增；[精灵捕获校验][capture-validation]、精灵召唤、投影状态和奥莉米娅点金要求对端的计数与本机完全相等。没有房主发布和客机接纳这个计数的入口。元素魔力与结晶挑战另有同样的本机递增计数，存在相同边界问题。

复现条件：一位玩家先完成单机战斗并回到菜单，另一位玩家刚启动游戏，随后组队进入同一场战斗。双方历史不同，计数会持续错开；客机捕获、召唤、点金等请求被当作旧战斗拒绝，部分状态快照也被丢弃。点金客户端在传输返回成功后即进入冷却，服务端拒绝不会回滚该冷却。

源码复现中，两个独立进程分别执行“零场历史”和“一场历史”，当前 epoch 为 1 和 3；真实的 `OlimyaGoldenizationLedger` 拒绝了身份正确、字段合法但 epoch 为 1 的请求。实际完整生命周期可能产生更多递增，问题是两端计数没有共同来源。

修复边界：采用房主生成、按当前战斗握手确认的会话身份；本地生命周期计数仅用于本机清理。精灵、投影、心变、点金、元素魔力和结晶挑战的请求、快照及失效处理应一并覆盖。需要验证不同单机历史、战斗重启、重新组队和延迟到达的旧包。

讨论进展：用户选择按 **Aura.Shared 提供联网战斗身份** 的方向继续设计。共享层提供通用的联网战斗身份、当前就绪状态和生命周期失效契约，Terrias 的受影响领域消费该契约。现有本地生命周期计数继续用于本机资源与异步任务管理，不直接充当联网身份。领域事件序号、实体生成标识、动作 token 仍保留各自用途。

此处记录的是已选择的方案方向，尚未实施。原生初始化的具体承载点、握手顺序、迟到消息处理及动作结果确认仍需细化；联网身份发布依赖 MP-02、MP-03 的真实发送者认证和发布权限。

**MP-02 · P1 · RPC 身份绑定拿到的是目标对象，并非实际发送连接**

[AuraRpcAuthorityRuntime][sender-binding] 从 `context.Target as PlayerManager` 读取 `PlayerId`，据此授予成员和房主身份。匹配版本的 [PlayerManager 接收器][native-receiver] 丢弃了 `senderConnection`，而两个通用 RPC Command 都以 `requiresAuthority: false` 注册。[Mirror 服务端][mirror-authority] 只有在命令要求 authority 时才检查目标对象所属连接。

因此，修改过的客机可以把请求发到房主或其他玩家的 PlayerManager 上；绑定器会将目标玩家误认为发送者。当前只使用 `sender.IsLobbyHost` 或 `sender.PlayerId` 的领域校验无法提供预期的隔离。此问题同时影响 Terrias、AuraToolsExp、共享 CG 和音频。

链接生产绑定器的桩件复现确认：目标为 host 时会绑定为 host 并授予房主标志。真实连接为 guest 这一前提来自上述原生调用链分析；本轮没有向实际房间发送伪造包。

修复边界：必须在仍持有实际 `NetworkConnectionToClient` 的接收边界绑定身份，或者使用能保证对象拥有权的命令通道；不能用 payload、目标对象的角色 ID 或可填入的 Accepted 标志替代连接认证。

讨论进展：用户选择 **在现有接收通道保留真实连接**。由 Aura.Shared 在实际发送连接仍可用的接收边界建立可信上下文，供 Terrias、AuraToolsExp、共享 CG 和音频的命令绑定器使用。命令拥有者从服务器维护的连接与玩家映射取得，目标 PlayerManager 仅作为调用目标。

此处记录的是已选择的方案方向，尚未实施。具体接入需覆盖两条通用接收通道、房主本地发送、异常与嵌套调用后的上下文恢复，并验证重复初始化和其他 MOD 共存。该选择解决身份来源；命令类型的准入、发布权限及拒绝后停止转发由 MP-03 的方案负责。

**MP-03 · P1 · 多类结果／快照可以绕过服务端校验直接广播**

[RpcElementalEnemyMagicSnapshot][unguarded-snapshot]、精灵状态／敌人移除、投影状态、心变结果、深渊冲击结果等只有 `RpcExecute`，没有服务端身份标记和 `CmdExecute` 校验。原生 [通用接收实现][native-relay] 对任何可反序列化的 RpcCommandBase 都先调用 `CmdExecute()`，随后无条件广播；没有覆盖的方法就是空操作。

即使 MP-02 修复，这类命令仍可以经客机自己的合法 PlayerManager 提交并转发。例如元素魔力快照只验证协议、epoch、StatusId 和数值形状，随后直接写目标状态。知道当前会话标识并不等于拥有状态写权限。

同一结构也存在于皮肤选择、CG 播放、音频事件和 MOD 清单分片：皮肤选择没有把 Snapshot.PlayerId 绑定到发送者，清单分片没有在转发前验证房主身份。各自的数据形状、哈希、去重检查不能替代身份校验。详见 [RPC 清单][rpc-inventory]。

修复边界：区分客户端请求与服务器发布的结果，结果类型必须拒绝非授权发布者；能由服务器重新捕获的快照应从服务器状态构造。RPC 扫描也应覆盖“没有 CmdExecute 的可写入结果类型”，而不只检查声明了 CmdExecute 的类。

讨论进展：用户选择 **共享入口统一准入，领域负责具体校验**。Aura.Shared 复用 MP-02 的可信接收上下文，按已注册的 Aura 命令类型执行准入和发布权限规则。非法命令应在进入业务执行及原生广播前终止；未注册的 Aura 命令默认拒绝。具体角色归属、当前战斗、目标、资源及效果合法性由对应领域校验。

该方案方向尚未实施。后续需要完成现有 RPC 类型分类及注册迁移，区分玩家请求、服务器结果、拥有者表现请求与分片传输，并覆盖原生／其他 MOD 共存、房主本地发布、拒绝回执、重复初始化及默认拒绝行为。共享层的类型准入不能替代领域校验，也不能仅通过 CmdExecute 内 return 来阻止原生继续广播。

**MP-04 · P1 · 策略实验室实机验证会写入当前联机房间的战斗状态**

[实机验证入口][validation-entry] 只检查自动战斗开关、非战斗状态和 RoleTable，不排除多人房间；界面按钮使用同一个检查。[启动验证战斗][validation-start] 直接调用当前 `FightManager.ReadyToInit(testLevel)`，然后仅在本机设置 `IsFake = true`。

匹配宿主实现中，ReadyToInit 是服务器 Command，会增加全房间的 `waitCount`、重置准备状态、覆盖 `wantLevel/level` 并执行 RpcFightCheck。它不是独立模拟器入口。单个调用就会污染当前房间准备状态；调用累计达到连接数时，会走全队原生战斗初始化。其他客户端不会因此自动具有调用者本机的 IsFake 标志。

复现条件：两人房间处于地图等非战斗阶段，任一玩家从策略实验室点击实机验证。该流程可能等待其他成员而超时，也可能与随后正常战斗准备相互干扰。退出验证只恢复调用者 RoleTable 和本机界面，不能撤销服务器已经修改的计数与关卡。

修复边界：实机验证应使用明确的独立单机环境；排队时和每次真正启动战斗前都检查环境，联机房间应给出明确的不可运行原因。原生 Command、角色快照、准备计数和退出恢复必须属于同一验证环境。

讨论进展：用户选择 **限定在受控单机会话中验证**，保留原生游戏执行路径。排队和每场实际启动前都要验证当前环境确实属于受控单机；不能只凭网络是否活跃或房间当前人数为一来判断。联机期间明确阻止原生实机验证入口，模型管理与正常自动战斗按各自契约运行。

该方案方向尚未实施。验证任务和恢复快照必须绑定原会话，取消、超时、异常及会话切换需要明确的终止与清理规则；旧角色快照不得恢复到新房间或新冒险。具体单机判定、验证期间会话变化的处理和临时状态恢复仍需落实并验证。

**MP-05 · P2 · 日耀整备的未确认提交会跨房间阻塞新提交**

[SolarMemoryRoleCommitApi][solar-pending] 持有静态 PendingState 和完成回调。成功发送后，只有收到匹配的回执才会释放；取消只出现在同步提交失败分支，没有菜单退出、断线、换房或超时的清理入口。

复现条件：客机提交最终整备后，在房主回执到达前离开房间；随后不重启进程，再进入新的日耀冒险。新 token 的 TryBegin 被旧 pending 拒绝，失败分支移除的是新角色的 token，旧 pending 保持不变。此后重试仍会失败。旧回调也未绑定可校验的房间／冒险身份。

源码复现确认：old-run pending 建立后，new-run 的 TryBegin 和 Cancel 都不能释放 old-run。

修复边界：提交状态应归属于房间和冒险；在所属会话结束时终止并清理回调，同一会话内使用同 token 的可确认重试。收到回执时应验证仍属于当前整备流程，避免旧回执完成新流程。

讨论进展：用户选择 **建立可恢复的整备提交事务**。待提交内容和事务身份需要可恢复，服务器保存可查询的提交结果；断线或重新进入同一冒险时先确认权威结果，再决定是否以原 token 和固定提交内容重试。持久事务归属于冒险、玩家及提交 token，房间连接身份用于接收授权与回调生命周期管理。

该方案方向尚未实施。服务器提交标记与最终角色保存必须保持一致，重复已接受事务只返回结果，不再覆盖后续角色进度；同 token 不同提交内容应明确拒绝。超时保留结果未知状态并提供有界重试与恢复入口，离房清理界面等待及回调，同时保留属于原冒险的恢复记录。具体冒险身份、持久化边界、旧存档恢复和状态查询路径仍需与后续相关问题一起细化。

**MP-06 · P2 · 余烬同步序号没有会话范围，重启客机会被判为旧数据**

[EmberAdventureStateService][ember-sequence] 的 LastSequences 是进程内静态字典，只按 OwnerPlayerId／OwnerStatusId 索引；本地发送计数也是静态值。快照没有房间、冒险或发送者实例的会话身份，也没有对应字典的清理入口。

复现条件：房主保留进程，客机在上一轮发送过 Sequence=20，随后客机重启，再与同一房主开始新冒险。客机从 1 开始的合法提交在房主端被丢弃，直到超过旧序号。客机已先应用本地状态，因此会出现两端余烬状态不一致。

链接生产源码的复现先接受 20，清空模拟的新冒险存储，再提交 1；结果为拒绝，存储仍为空。服务端 RPC 还会忽略 ApplySnapshot 的 false 返回而设置 Accepted，使发送方无法从结果识别这一拒绝。

修复边界：序号以房主认可的会话／冒险和拥有者为范围，或者通过快照握手建立新的序号基线；同时区分已应用、幂等重复和陈旧拒绝的结果。

讨论进展：用户选择 **房主管理持久化版本，客户端提交更新请求**。余烬仍按冒险和玩家隔离，房主维护已确认的余烬值及版本，客户端通过可幂等识别的请求提交更新；重连或进程重启后获取权威状态，不以客户端本地递增计数决定持久版本。

该方案方向尚未实施。需要区分更新成功、幂等重复、版本冲突和冒险不匹配，保证值与版本一致持久化；本地待确认操作应防止迟到回执覆盖新操作。保留可确认归属的旧存档余烬值并建立初始版本。冒险身份应与 MP-05 的提交事务及 MP-08 的档案关联共同设计，具体余烬规则仍属于 Terrias。

**MP-07 · P2 · DPT 批量上限取本机设置，会永久丢失客机伤害**

[DPT 接收循环][damage-batch] 以房主自己的 MaxEventsPerBatch 限制请求条数，而发送端以客机自己的同名配置切批。配置允许 1—64，协议没有协商实际接收上限。

复现条件：房主使用默认 24，客机使用 64，并在一个发送周期产生 64 条合法事件。房主只处理前 24 条，在第 25 条退出循环；客机发送后已经清空待提交列表。回包中的 RejectionReasons 没有触发重发，服务器序号也只分配给已接收部分，因此普通快照补齐不能找回另外 40 条。该配置差异影响战斗和冒险总计。

修复边界：协议接收上限应是所有兼容节点共同支持的常量或协商结果；本地配置只控制发送频率和切批大小。若支持部分确认，发送端必须保留未确认事件并幂等重试。

讨论进展：用户选择 **协议固定上限＋可靠确认**。兼容协议定义共同的最大条数和字节预算，本地配置只控制更小的发送批次与发送频率；事件在服务器明确确认后才释放，临时失败可按原事件身份重试。

该方案方向尚未实施。需要保证重发不重复计数、连续确认不跳过序号缺口，明确每个事件的确认或拒绝结果并区分可重试原因；仅保留最大已见序号不足以支持早期事件补发。封存前应处理剩余提交，无法补齐时明确标记统计不完整。具体上限、缓存边界、确认协议兼容性和结算收尾仍需验证。

**MP-08 · P2 · 客机冒险历程无法关联房主复制来的战斗回放**

[AdventureArchiveRuntime][archive-identity] 在冒险开始时缓存本机生成的 AdventureId。房主录制的 MatchRecord 使用房主的 AdventureId；[ReplicaStore][replica-store] 保留该 ID 写入客机数据库。[冒险详情查询][archive-query] 仅通过 adventure_id 相等查找战斗记录，没有网络冒险与本地视角档案的映射。

复现条件：双方开启冒险历程与回放，开始全新联机冒险并完成一场已成功复制的回放。客机档案使用 B、回放使用 A；回放库能有该记录，但当前冒险历程的战斗列表查不到。即使 DPT 在战斗结束后更新 CurrentAdventureId，档案的 activeAdventureId 仍缓存 B，已有记录也没有迁移。

修复边界：在记录之前取得共同的网络冒险身份，或维护网络冒险与本地视角档案的显式关联。保留已封存回放内容的不可变性；已有档案需要有证据的关联修复，不能仅改写回放 Header。

讨论进展：用户选择 **Aura.Shared 统一联网冒险身份**。房主从权威存档取得或建立稳定的 AdventureId，各端接纳后用于冒险档案、DPT 和回放关联，并供 MP-05 的整备事务及 MP-06 的余烬版本复用。身份服务独立于 DPT、回放等功能开关，同一冒险的重连、重启和正常读档沿用身份，新冒险使用新身份。

该方案方向尚未实施。共享冒险身份不合并各玩家的牌组、选择与视角数据；战斗身份和回放文档 RecordId 保留各自语义。身份就绪前产生的数据需要明确的待关联状态及后续完成机制。新记录统一使用共享身份，历史兼容映射仅用于有确定证据的旧记录；已封存回放内容和哈希保持不变，无法确定归属的旧记录保留并标记未关联。具体存档承载点、原生同步接入和数据库迁移仍需验证。

**八项已选择的修复方向**

以下方案方向均已在逐项讨论中由用户选择。此汇总表示设计方向选定，不表示代码已实现或联机验收通过。

| 编号 | 已选择方向 | 主要责任 |
| --- | --- | --- |
| MP-01 | Aura.Shared 提供联网战斗身份 | 共享层提供身份及生命周期；Terrias 受影响玩法接入 |
| MP-02 | 在现有接收通道保留真实连接 | 共享接收适配层建立可信发送者上下文 |
| MP-03 | 共享入口统一准入，领域负责具体校验 | 共享层管理类型准入和发布权限；各领域管理业务合法性 |
| MP-04 | 限定在受控单机会话中验证 | AuraToolsExp 实机验证入口、任务会话及恢复流程 |
| MP-05 | 建立可恢复的整备提交事务 | Terrias 日耀整备提交、服务器持久结果与恢复 |
| MP-06 | 房主管理持久化版本，客户端提交更新请求 | Terrias 按冒险、玩家管理余烬值和版本 |
| MP-07 | 协议固定上限＋可靠确认 | AuraToolsExp DPT 事件传输、账本及归档收尾 |
| MP-08 | Aura.Shared 统一联网冒险身份 | 共享身份服务；两个产品及档案、DPT、回放消费者 |

**整合后的契约与实施依赖（设计建议）**

身份需要区分三种生命周期。房间会话用于连接授权和回调失效，网络会话重建时更新；冒险身份随权威存档保存，正常续玩跨房间重建保持稳定；战斗身份对应一次原生战斗初始化或重启，由房主发布。房间身份不应成为可恢复整备事务的持久主键。本地对象池代数、领域事件序号和回放文档身份继续承担各自用途。

实施顺序建议如下；每步均落实正式实现与相应消费者，最终形成一次完整切换：

1. **可信接收与准入（MP-02、MP-03）。** 验证实际连接的获取点和终止广播的位置，建立统一接收上下文、命令分类与注册规则；检查 50 个已枚举 RPC 的完整覆盖，并验证其他 MOD 共存。
2. **共享冒险与战斗身份（MP-08、MP-01）。** 先确定权威存档和原生同步承载点，再定义初始化、接纳、就绪、重启与失效规则。接入 Terrias 全部受影响战斗路径，处理点金等动作的结果确认。
3. **可恢复的玩家进度（MP-05、MP-06）。** 共用稳定冒险身份和已认证玩家身份，分别完善整备提交事务、余烬值与版本的持久化、幂等确认和恢复。共享层不承担日耀选择或余烬玩法规则。
4. **统计与档案接入（MP-07、MP-08）。** 固定传输预算，落实事件确认与缺口恢复；统一新档案和新回放的冒险关联，完成有证据的历史数据迁移。记录缺失、过期拒绝和无法关联都应有可观察的终态。
5. **受控单机验证（MP-04，可独立落实）。** 准确判定环境，排队及逐场启动复核；任务退出、超时及会话切换只清理原验证会话持有的状态。
6. **统一验证与发布。** 根据变更边界选择共享网络、兼容性、Terrias 领域及 AuraToolsExp 检查，串行执行共享 DLL 输出相关命令。产品发布使用唯一发布事务；在一致的实际加载 DLL 上完成双端／四端验收，分别报告自动检查和实机证据。

进入实现设计时仍须核实的技术点包括：原生接收适配的注册与恢复方式；冒险身份的存档创建／读档时机；战斗身份与原生初始化的对应关系；服务器状态和提交结果的一致持久化；DPT 固定条数、字节与缓存预算；旧协议能力降级和旧数据的可证明关联。历史检查日志是审查基线，不能直接作为未来修改后的验证结果。

**模块覆盖记录**

以下结论基于当前入口、状态写入点、RPC 收发和相关自动检查。MP-02 是所有依赖共享发送者绑定的模块的共同问题；表中的“无新增问题”不排除这一共同影响。

| Terrias 功能范围 | 已检查的联机责任 | 结论 |
| --- | --- | --- |
| 卡牌、Buff、遗物、卡包和特殊标签 | Scripting 分发、原生 ScriptExecutor 路由、远程目标配置恢复、运行时附着 | 手部附着涉及 MP-03；内容清单和通用 C# 检查通过 |
| 乌娜、白曜、余烬 | 本地拥有者写入、按玩家持久化、跨战斗恢复 | MP-06；余烬不能视为已通过重新组队验收 |
| 命座、命星、本源奖励 | 房主 roster、GUID 战斗会话、拥有者请求、结果去重 | 所查领域路径无新增问题；仍受 MP-02 影响 |
| 洛奈尔、晨星、星谱、晨祷、众生相 | 本地动作入口、状态拥有者、选择与临时事务 | 所查入口无新增问题；多人相互增益与同角色组合需实机验证 |
| 哥伦比娅、月反应、归家的月亮 | 角色脚本、动作提交、状态结算、回合清理 | 角色和主题卡包检查通过；元素网络见 MP-01、MP-03 |
| 奥莉米娅、黄金梦、点金 | 本地技能、服务器标记、拥有者与序号校验 | MP-01；点金需覆盖拒绝后的本地冷却 |
| 元素魔力、结晶生成／争夺 | 房主计算、请求归属、事件身份、结算快照 | MP-01、MP-03 |
| 场地与场地 HUD | 房主场地状态、激活意图、快照修复、战斗 GUID | 所查领域路径无新增问题；仍受 MP-02 影响 |
| 精灵捕获、培养、圣遗物、出战与换位 | 本地藏品、服务器战斗实体、捕获落库、状态生成、卡牌返还 | MP-01、MP-03；静态与程序集测试不能证明 Partner 队列实机行为 |
| 投影、独立出牌、心变 | 语义拥有者与执行路由、生成标识、回合状态、移除清理 | MP-01、MP-03 |
| 变形及角色表现 | 角色拥有者校验、视觉快照、本地恢复 | 已有拥有者校验；仍受 MP-02 影响，需实机覆盖跨角色还原 |
| 使魔成长、祝福、桑多涅喵 | 当前使魔、本地进度、胜利奖励幂等、生命周期 | 使魔检查通过；四人混合伙伴的奖励与离场仍需实机验证 |
| 日耀回忆整备、固定节点、首领／终局 | 玩家独立整备、最终角色提交、地图投影修复、旧存档隔离 | MP-05；事件与地图数据检查通过 |
| 无尽之海／深渊地图、压力、增援 | 客机观察者、房主地图状态、原生动态敌人同步 | 所查主路径无新增问题；冲击结果存在 MP-03 |
| 深渊里程碑、冲击、撤离、结算 | 玩家独立奖励、房主冲击决策、结算屏障、延迟发送 | MP-03；屏障和多人同时选奖励需实机验收 |
| 必要战斗表现、CG 声明、资源注册 | 资源归属、本地呈现、共享生命周期 | 声明边界无新增问题；Unity 多端表现未执行 |

| AuraToolsExp 注册模块 | 已检查的联机责任 | 结论 |
| --- | --- | --- |
| 文件日志 | 本机日志和命令观察 | 无新增联机状态写入问题 |
| 角色皮肤 | 本地选择、远端角色选择、重复广播 | MP-03，远端选择可冒用 PlayerId |
| 卡牌外观 | 本地卡牌表现和配置覆盖 | 无新增游戏状态同步问题；多端外观允许不同 |
| 战斗背景音乐 | AudioArbiter 本地生效与生命周期 | 共享音频网络路径受 MP-02、MP-03 影响 |
| 卡牌使用音效 | 本地动作发起、会话和重复抑制 | MP-02、MP-03 |
| 角色语音 | 拥有者、触发时机和本地资源 | MP-02、MP-03；缺资源不应影响玩法 |
| 自定义开局 | CmdSyncRoleTable 序列化前应用本地角色配置 | 所查主路径正确使用原生提交；需双端不同配装实机验收 |
| 一键美餐 | 调用本地原生 FoodItem.EatFood | 无新增共享玩法写入问题；原生多人结算需实机验证 |
| 美餐 CG（内部模块） | 开关联动、角色请求、表现同步 | MP-02、MP-03 |
| 随身保险箱 | 原生本地角色库存、临时限额恢复、阻塞界面 | 无新增跨玩家写入问题；双方同时存取需实机验证 |
| 刷新选牌 | 本地奖励窗口和独立 Dice、选定后停用 | 无新增共享进度写入问题；多人奖励完成顺序需实机验证 |
| 像素表情 | 发送者校验、尺寸与内容哈希、限流 | 领域策略已有检查；受 MP-02 影响 |
| 自动战斗 | 本地 FightPlayer、动作窗口、异步决策新鲜度和提交 | 所查在线动作入口无新增问题；多人轮次和远端效果需实机验证 |
| 策略模型实验室 | 本地模型／外部训练与实机验证入口 | MP-04；本轮未运行训练和验证战斗 |
| MOD 配置同步 | 房主清单、定向回包、分片、超时回退 | MP-02、MP-03；已有定向和广播回退清理 |
| 大厅状态面板 | 从原生大厅快照读取玩家、准备和 MOD 信息 | 无新增写入问题；三／四人信息刷新需实机验证 |
| DPT 统计 | 房主账本、玩家事件序号、批量、快照补齐和归档 | MP-07；另需验证终局迟到事件，不以本轮静态审查认定已通过 |
| 战斗回放 | 房主录制、能力协商、分片复制、持久化与播放隔离 | MP-08；发送者受 MP-02 影响。播放入口已有活动网络隔离检查 |
| 冒险历程 | 本地角色／选择、冒险标识、与回放数据库关联 | MP-08 |
| 卡牌 UI 诊断（内部服务） | 本地观察和性能诊断 | 无新增自定义联机写入问题 |
| 角色 CG | 角色拥有者、技能／濒危信号、远程表现 | MP-02、MP-03 |
| 卡牌 CG | 注册卡牌信号、资源 ID、动作去重 | MP-02、MP-03 |
| 事件 CG | 房主终局／团队信号、团队快照、本地资源解析 | MP-02、MP-03；首帧与双方加载顺序需实机验证 |
| 妙妙方案库 | 本机配置预检和事务式应用 | 本机方案与房主 MOD 清单分工明确；DPT 配置差异仍触发 MP-07 |
| MOD 健康检查 | 本机加载、依赖、入口与资源诊断 | 无新增共享游戏状态写入问题 |

**自动检查与复现证据**

16 项现有检查全部退出成功。执行记录分别保存在[第一批检查][test-results]和[领域检查][domain-results]，对应目录保留原始日志。

| 检查 | 结果／证据范围 |
| --- | --- |
| Test-NetworkRpcAuthority | 通过；扫描 4198 个文件、4 种服务端标记。扫描通过不覆盖 MP-02 的真实连接身份或 MP-03 的无 CmdExecute 结果类型 |
| Test-AuraSharedCore | 1296 assertions；有一项原有的可空分析警告 |
| Test-AuraCgShared | 207 assertions |
| Test-AudioArbiterShared | 476 assertions |
| Test-TerriasCSharp -SkipBuild | 863 assertions；未执行产品发布构建 |
| Test-SpiritRuntime | 138 assertions |
| Test-TerriasElemental | 元素目录与行为检查通过 |
| Test-AuraToolsExp -SkipModelIntegration | 1573 assertions；URP 合同检查和工具内容检查通过，未运行模型注册集成 |
| Test-TerriasContent | 86 cards / 18 relics / 57 buffs / 5 packs / 3 enemies，0 warnings |
| Test-TerriasEvents | 6 events / 10 map rows，0 warnings |
| Test-TerriasColumbina | 37 assertions 和内容检查 |
| Test-TerriasMoonHomecoming | 27 assertions 和内容检查 |
| Test-TerriasOlimya | 59 assertions 和内容检查 |
| Test-FamiliarGrowth | 成长、迁移、祝福和桑多涅喵生命周期检查通过 |
| Test-AuraSkinShared | 36 assertions |
| Test-WitchEventDataContracts | 35 assertions；匹配 Managed 的事件数据合同 |

精灵、元素、哥伦比娅测试项目引用现有 Terrias 编译输出及包内 Aura.Shared；本轮没有重建这些产品 DLL，也没有把这些结果表述为“当前源码重新发布后的联机验证”。奥莉米娅和新增边界复现等检查直接链接对应源码。现有产品／训练程序的工作区改动均保留。

同时核对了现有程序集：Terrias 编译输出与其包内 Entry.dll 哈希相同，两个产品包内的 Aura.Shared.dll 哈希也相同。相关源码与程序集的[指纹记录][source-fingerprints]已保存；这不替代实际游戏加载路径和双端安装内容的核验。

[BoundaryProbe 源码][probe-project]直接链接生产绑定器、战斗身份服务、点金账本、日耀 pending 状态和余烬状态服务。宿主对象、存储与注册接口使用测试桩；不会启动游戏、发送网络包或读取玩家存档。其[原始结果][probe-results]为：

| 探针 | 当前源码的实际观察 |
| --- | --- |
| RPC 目标身份绑定 | Target=host ⇒ BoundPlayer=host，IsLobbyHost=true；实际连接没有进入该绑定接口 |
| 不同进程战斗历史 | 零场历史 ⇒ epoch 1；一场历史 ⇒ epoch 3 |
| 点金账本 | host epoch 3 拒绝 guest epoch 1 的合法拥有者命令 |
| 日耀旧 pending | new-run 被拒绝；Cancel(new-run) 不释放 old-run |
| 余烬发送者重启 | 先收到 20 后，新会话序号 1 被拒绝；新存储无写入 |

**实机验收清单（本轮未执行）**

| 场景 | 必须观察的结果 |
| --- | --- |
| 双方全新启动；一方先打单机；同进程换房／换房主 | 同场战斗身份一致，精灵、点金、结晶均成功，旧包拒绝不伤及新场 |
| 日耀最终提交时离房、重进旧存档、开启新冒险 | 无永久 pending，旧回执不能完成新整备，双方最终角色独立 |
| 房主不重启、客机重启后重新组队 | 余烬新序号正常落库，两端归属和恢复一致 |
| 两端 DPT batch 24／64／1，密集多段伤害 | 每条合法事件只统计一次，批量拒绝有可完成的恢复，终局总计一致 |
| 高延迟下最后一击、持续伤害、死亡和退出 | DPT 所有者最后一批事件在归档前完成，回放终局完整 |
| 两人、三人、四人；相同角色及不同角色组合 | 白曜、星谱、月反应、黄金化、共享增益不重复且不遗漏 |
| 投影／精灵达位置上限、换位、撤回、死亡与战斗重启 | 所有端实体、回合队列、原生执行路由和下一场清理一致 |
| 日耀固定首领／终局，无尽深渊冲击／撤离／结算 | 仅授权房主推进共享进度，玩家各自完成奖励，屏障正确排空 |
| 开局牌组／保险箱／美餐／刷新选牌分别只在一端开启 | 只影响相应玩家，原生角色提交及奖励完成正常 |
| 某端没有 AuraTools；工具版本不同；CG／语音资源不同 | 对应协议明确降级，玩法继续，本地资源和配置归属正确 |
| 客机先／后进入战斗，CG 和音频 session 消息先／后到达 | 会话建立不会被本地重置丢失，同一动作不重复播放 |
| 客机接收回放、接收后重启、从冒险详情打开 | 分片完整性、复制归档、冒险关联及查看入口一致 |
| 联机房间启动策略实验室实机验证 | 明确拒绝进入当前房间的原生战斗通道；房间计数与关卡不变 |
| 隔离测试环境的越权请求和伪造结果 | 以真实连接授权；目标对象或 payload 无法冒充房主／其他玩家 |

建议优先处理 MP-01 至 MP-04，再处理会话恢复、DPT 批量和档案关联。修复时应把上述失败场景纳入拥有该行为的正式测试，并在同一组已发布 DLL 上完成双端／四端验收。

[epoch]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/Baseline/CompanionAuthorityService.cs:13
[capture-validation]: D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Mechanics/SpiritCaptureService.cs:217
[sender-binding]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/Baseline/AuraRpcAuthorityRuntime.cs:79
[native-receiver]: D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/PlayerManager.cs:1913
[mirror-authority]: D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Mirror/Mirror/NetworkServer.cs:274
[native-relay]: D:/Project/Apocalypic-journey-mods-creator/ModExp/开发参考资料/反编译文件夹v1.0.24831968/Witch/PlayerManager.cs:2777
[unguarded-snapshot]: D:/Project/Apocalypic-journey-mods-creator/ModExp/Terrias-Dev/Network/RpcElementalMechanics.cs:9
[validation-entry]: D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAutoBattleGameValidationRuntime.cs:189
[validation-start]: D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/AutoBattle/AuraToolsAutoBattleGameValidationRuntime.cs:557
[solar-pending]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/Baseline/SolarMemoryRoleCommitApi.cs:55
[ember-sequence]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/Baseline/EmberAdventureStateService.cs:137
[damage-batch]: D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/DamageMeter/Network/DamageMeterNetworkRuntime.cs:313
[archive-identity]: D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/AdventureArchive/AdventureArchiveRuntime.cs:139
[replica-store]: D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/MatchRecords/Storage/ReplayReplicaStoreV17.cs:29
[archive-query]: D:/Project/Apocalypic-journey-mods-creator/ModExp/AuraToolsExp-Dev/Features/AdventureArchive/AdventureArchiveDatabase.cs:251
[fingerprints]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/managed-fingerprints.json
[test-results]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/test-results.json
[domain-results]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/test-results-domain.json
[rpc-inventory]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/rpc-inventory.txt
[probe-project]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/BoundaryProbe.csproj
[probe-results]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/boundary-probes.jsonl
[source-fingerprints]: D:/Project/Apocalypic-journey-mods-creator/ModExp/artifacts/multiplayer-audit/2026-09-07/source-and-binary-fingerprints.json
