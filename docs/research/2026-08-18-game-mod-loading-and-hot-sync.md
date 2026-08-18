# 游戏主体 MOD 加载与热同步调研

日期：2026-08-18  
结论适用版本：游戏构建 `v1.0.24605918`

## 结论摘要

1. **当前游戏没有运行时 MOD 重载能力。** 它支持在运行中刷新 MOD 列表、下载/复制创意工坊文件、修改 `Enabled`，但真正的 Data/Text、`Entry.lua`、`Entry.dll` 只在 `GameConfigManager.Init()` 启动阶段载入。界面也明确要求重启。
2. **不修改游戏加载器时，不能可靠地热同步任意 MOD。** 尤其是 `Entry.dll`：当前使用默认运行域中的 `Assembly.LoadFrom`，没有程序集隔离、卸载或 MOD 级 `Shutdown/Dispose` 契约。
3. **可以做“有限热同步”，但必须定义边界。** 推荐先支持“主菜单安全点上的配置、清单、Data/Text 和按需资源重载”，明确排除 `Entry.dll`、共享 DLL、进行中的冒险/战斗和已连接的联机房间。
4. **如果目标是完整 DLL 热更，只能做选择性、开发态的代际切换，不是真卸载。** 更合理的形态是稳定 Bootstrap DLL + 可替换数据/脚本/代际 Payload；旧 DLL 仍驻留内存，因此必须限制重载次数并要求严格的反注册契约。
5. **联机“同步”必须与本地“热重载”分开设计。** 当前 AuraTools MOD 同步只对齐启用状态并通过创意工坊下载缺失 MOD，既不比较内容哈希，也不会激活当前运行时，完成后仍提示重启。

建议产品定义：

- `热发现`：运行中发现、下载、复制 MOD 文件。当前已基本具备。
- `热配置`：运行中修改工具设置或 MOD 自有配置。部分具备。
- `热重载`：让本机当前运行时采用新 Data/Text/资源/代码。当前不具备。
- `联机热同步`：多端先获得同一内容，再在同一代际原子切换。当前不具备。

## 调研依据

- 完整反编译快照：`开发参考资料/反编译文件夹v1.0.24605918`。
- 反编译报告：253 个输入程序集全部成功，`ilspycmd 9.1.0.7988`，见 `artifacts/game-reference/1.0.24605918/decompile-report.md`。
- 当前仓库 MOD 运行时：`Terrias-Dev`、`AuraToolsExp-Dev`、`AuraOnlineShared`、`AuraSharedCore`。
- Terrias 和 AuraToolsExp 均面向 `net472`；当前 `Terrias/Scripts/Entry.dll`、`Aura.Shared.dll` 和 Detour Backend 的 AssemblyVersion 均为 `1.0.0.0`。

说明：项目技能索引仍指向旧快照 `v1.0.24591395`，本次以仓库内更新且完整的 `v1.0.24605918` 为准。

## 当前加载流程

### 1. 唯一正式入口

`GameApp.StartGame()` 在游戏启动流程中调用 `Singleton<GameConfigManager>.Instance.Init()`：

- `GameApp.cs:620-669`
- 正式调用点为 `GameApp.cs:646`
- 全反编译工程只发现这一处 `GameConfigManager.Init()` 调用

`GameConfigManager` 是进程级泛型单例，不会因普通场景切换自动重建。

### 2. 初始化脚本环境和原生表

`GameConfigManager.Init()` 依次：

1. 初始化 `ScriptExecutor` 和 `VisualScriptExecutor`。
2. 从 Addressables 读取原生 `Data` 与 `Text` 表。
3. 记录原生 ID。
4. 创建 `Application.dataPath/Mods`（即 `Globals.ModsPath`）。

证据：`GameConfigManager.cs:1182-1200`；路径定义在 `Witch.Core/Globals.cs:20`。

### 3. 创意工坊先同步到磁盘

启动时先调用 `SyncInstalledWorkshopModsToLocal()`，把已安装的 Steam Workshop 内容复制到 `Globals.ModsPath`，再枚举本地目录。

运行中的 MOD 管理界面刷新也会执行同一磁盘同步，但下载完成后显示“已下载未载入”并标记需要重启：

- `GameConfigManager.cs:1200-1201, 1364-1374`
- `ModItem.cs:599-605`
- `SteamWorkshopDownloadService.cs:1007-1020` 会删除/重建目标目录并恢复 `Enabled`

因此这里的 “Sync” 是**文件同步**，不是运行时激活。

### 4. 发现与读取 MOD 元数据

加载器枚举 `ModsPath` 的一级目录，对每个目录：

1. 读取 UTF-8 `ModConfig.json`。
2. 设置运行时 `DirectoryName`。
3. 执行封禁名单检查。
4. 以 `ModId = ModName + "." + ModAuthor` 去重。
5. 把所有发现到的 MOD（包括未启用的）加入 `modConfigs`。
6. 可选读取 `Configuration.json` 到 MOD 自有配置字典。

证据：`GameConfigManager.cs:1201-1271`；`ModConfig.ModId` 在 `Witch/Mod/ModConfig.cs:475-477`。

### 5. 依赖排序

`LoadModWithDependencies()`：

1. 全局清空 `ModHookRegistry`。
2. 检查依赖是否存在且启用。
3. 以 Kahn 拓扑排序决定加载顺序。
4. 跳过未启用、缺依赖、依赖未启用、循环依赖的 MOD。

证据：`GameConfigManager.cs:1377-1518`。

当前 `ModHookRegistry` 只有全局 `Clear()`，没有 owner、token 或单个回调的 Remove API：`Witch.Core/Witch/Core/ModHookRegistry.cs:6-71`。

### 6. 按 MOD 加载 Data/Text

对每个可加载 MOD：

1. 从 `Data/` 读取各类 CSV/XLSX。
2. 从 `Text/` 读取本地化表。
3. 通过 `GameConfigData.Concat(old, new)` 合并进 `_tables`。
4. 记录数据行的 owner；只要存在表文件，就把 `MustSame` 强制设为 `true`。

证据：

- `GameConfigManager.cs:1471-1483`
- `GameConfigManager.cs:1610-1653`
- `Witch.Core/ExcelTableReader.cs:68-125`
- `Witch.Core/GameConfigData.cs:80-121`

注意：`Concat(old, new)` 遇到同 ID/同字段时保留旧值；这对直接重复调用加载函数非常关键。

### 7. 执行 Entry.lua

`ModConfig.Setup()` 为每个 MOD 创建继承全局 Lua 环境的独立 `modLuaTable`，执行 `Scripts/Entry.lua`，然后调用 `Setup`。

证据：`Witch/Mod/ModConfig.cs:484-522`。

Lua 的全局 `LuaEnv` 在 `ScriptExecutor` 第一次初始化后常驻；加载器没有 MOD Lua table 的卸载/重载路径。

### 8. 加载 Entry.dll

若存在 `Scripts/Entry.dll`：

1. `Assembly.LoadFrom(path).GetTypes()`。
2. 扫描所有静态方法。
3. 调用带 `[ModInitialize]` 的方法。
4. 将 `[HookBefore]/[HookAfter]` 方法注册到全局 `ModHookRegistry`。

证据：`Witch/Mod/ModConfig.cs:523-548`。

加载器没有保存 `Assembly`/activation handle，也没有调用 MOD 的 `Shutdown`、`Dispose` 或 `Unload`。`hasDLL` 只被写入、不参与卸载。

当前 Terrias 的 `Entry.Initialize()` 会注册共享层、数据所有权、资源、角色、视觉、音频、模式、网络、调度器和大量 hooks；没有对称的停用入口，见 `Terrias-Dev/Entry.cs:22-55`。AuraToolsExp 同样只有初始化入口，见 `AuraToolsExp-Dev/Entry.cs:16-41`。

### 9. 建立二级缓存和消费者

全部 MOD 加载后，游戏继续：

1. 汇总 LockedIds。
2. 生成关键词预览行。
3. 预编译每个数据项的脚本并放进全局 `Globals.DataConfigCache`。
4. 初始化 DialogueManager。
5. 返回 `GameApp.StartGame()` 后继续初始化 GameRuntimeData、Achievement、TextTranslator 和大量 UI。

证据：

- `GameConfigManager.cs:1272-1281`
- `GameConfigManager.cs:1810-1838`
- `GameApp.cs:646-692`

### 10. MOD 图片等松散资源

`ResourceLoader` 对 `Mods/...` 路径转成本地文件路径；PNG/JPG/TXT/JSON 在请求时从磁盘读取。资源本身没有统一的 MOD 级缓存清理契约，但调用方和共享视觉/音频运行时可能持有生成后的 Unity 对象。

证据：`Witch.Core/ResourceLoader.cs:77-89, 173-214, 350-379`。

所以修改松散图片后，**后续新加载**可能看到新文件；已经创建的 Sprite、Texture、UI、音频对象不会自动刷新。

## 当前所谓“同步”的实际能力

### 游戏原生联机检查

本机加入房间时把 `GameConfigManager.modConfigs` 作为 `LobbyInfo.PlayerInfo.Mods` 发送，见 `PlayerManager.cs:2392-2411`。Mirror 序列化了路径、名称、版本、Enabled、依赖和 MustSame，见 `Mirror/GeneratedNetworkCode.cs:353-372`。

客户端只在 Lobby 更新时弹出差异提示：

- 只按 `ModName + "." + ModAuthor` 比较。
- 只检查 `Enabled`。
- 不比较 `ModVersion` 或内容哈希。
- `BuildMustSameModSet()` 实际没有检查 `MustSame`。
- 只提示，不阻止开局。

证据：`GameEntryUI.cs:1752-1823`。

### AuraTools MOD 配置同步

项目现有 `AuraOnlineHostModSyncSession`：

- 获取房主 MOD manifest。
- 对缺失且有 Workshop ID 的 MOD执行下载。
- 修改本地 `ModConfig.json.Enabled`。
- 若有变化，明确提示“需要重启游戏生效”。

更重要的是，`BuildPlan()` 在房主和本机 `Enabled` 相同时直接跳过，因此**双方都启用但版本不同，也不会更新**：`AuraOnlineShared/AuraOnlineHostModSyncSession.cs:140-170`。

本地 manifest 也只有 `ModVersion`，没有文件清单/MVID/SHA-256；见 `AuraOnlineLocalModManifestBuilder.cs:51-84`。

## 为什么不能直接再次调用 Init

`GameConfigManager.Init()` 不是幂等或可重入 API：

- 不清空 `modConfigs`。
- 不清空 `_tables`。
- 不清空 `NativeIds`、`LockedIds`。
- 不清空/替换 `Globals.DataConfigCache`。
- Data/Text 用旧值优先的 `Concat`，新行不能覆盖旧行，删除的行也不会消失。
- `PreCompileScripts()` 使用 `DataConfigCache.TryAdd`，不会替换旧编译结果。
- 再次加载所有 DLL 会重复触发初始化；只有 ModHookRegistry 被全局清空，EventCenter、静态事件、协程、UI、网络命令、共享注册表等没有统一清理。
- 清空全局 ModHookRegistry 还会顺带移除 Achievement 等非 MOD 后注册的 hook，除非完整重建后续系统。

因此“在 MOD 管理器点一下后再次调用 `Init()`”会得到混合代际运行时，而不是热重载。

## DLL 热重载的硬边界

当前项目目标框架是 `net472`，游戏使用默认运行域内的 `Assembly.LoadFrom`，当前 Managed 目录也没有 `System.Runtime.Loader.dll`/`AssemblyLoadContext` 使用痕迹。

.NET Framework 不能单独卸载已经载入的普通程序集；只能卸载承载它的整个 AppDomain。现代 .NET 的 collectible `AssemblyLoadContext` 可以成组卸载，但要求运行时支持，且所有线程、静态引用、回调和对象引用都已释放。参考：

- <https://learn.microsoft.com/dotnet/standard/assembly/load-unload>
- <https://learn.microsoft.com/dotnet/standard/assembly/unloadability>

为每个 MOD 建立子 AppDomain 在这里也不实用：MOD 需要直接持有 UnityEngine 和游戏对象，而这些对象不能按现有 API 透明跨 AppDomain 使用。

另外，当前主 DLL 与共享 DLL长期保持 `1.0.0.0` 程序集身份。即使覆盖磁盘文件，再次 `LoadFrom` 也无法构成可靠的新代际依赖图；更新后的 `Aura.Shared.dll` 等依赖尤其容易继续绑定到已加载版本。

## 可行性矩阵

| 内容 | 当前运行中更新文件 | 当前运行时自动采用 | 可新增安全热重载 | 约束 |
|---|---:|---:|---:|---|
| MOD 列表/描述/图标预览 | 是 | 仅管理界面 | 是 | 不等于激活 |
| `Enabled` 配置 | 是 | 否，只改内存标志 | 是 | 必须区分 desired/active |
| Workshop 下载/复制 | 是 | 否 | 是 | 需 staging 和原子替换 |
| MOD 自有 JSON 配置 | 是 | 视模块而定 | 是 | 模块需订阅 revision |
| 松散 PNG/JPG/TXT/JSON | 是 | 仅未来新请求 | 是 | 销毁/替换旧 Unity 对象 |
| Data/Text CSV/XLSX | 是 | 否 | **可以，主菜单安全点** | 必须从空快照重建并替换缓存 |
| `Entry.lua` | 是 | 否 | 有条件 | 需要 owner 化反注册和 Lua table dispose |
| `Entry.dll` | 是 | 否 | 不可做通用真重载 | 只能稳定 bootstrap + 开发态代际 payload |
| `Aura.Shared.dll` 等共享 DLL | 是 | 否 | 不建议 | 多 MOD共享身份和静态状态 |
| 战斗中已有卡牌/Buff/角色实例 | 不适用 | 否 | 不建议 | 对象内已复制旧 data 和 delegate |
| 联机房间内原子切换 | 不适用 | 否 | 后期可做 | 需要 Prepare/Ready/Commit 屏障与失败回滚 |

## 推荐方案

### 方案 A：安全点内容热重载，推荐

目标是“**不退出进程**”，但只允许在主菜单、无活动冒险、无战斗、无联机连接时应用。首版不支持 DLL 变化。

建议流程：

1. **构建 active manifest**：`modId + modVersion + contentHash + assemblyMvid + enabled + dependencies + reloadCapability`。
2. **磁盘 staging**：下载/复制到临时目录；校验路径穿越、大小、文件清单、SHA-256、Workshop 来源；禁止边写边读正式目录。
3. **预检**：解析所有 ModConfig、依赖图、Data/Text 和声明式资源，不触碰当前运行时。
4. **进入安全点**：拒绝战斗、冒险、存档载入、房间连接和正在执行的脚本；暂停 MOD 调度器。
5. **停用可重载 generation**：通过 owner/token 统一移除 hook、事件、协程、网络注册、UI、缓存资源。
6. **从零构建新快照**：原生 Data/Text + 所有启用 MOD，生成新的 tables、owner index、keyword tables 和 DataConfig cache，不能调用现有增量 `LoadResource()` 叠加。
7. **原子发布**：一次性替换快照并增加 `generation/epoch`，让数据、视觉、音频、角色、模式等订阅者失效旧缓存并重新解析。
8. **重建消费者**：至少包含 Dialogue、TextTranslator、Achievement、共享注册表、MOD 自有 registries 和主菜单 UI。
9. **失败回滚**：新快照未完整建成前不改变 active generation；失败继续使用旧运行时。

实现位置有两种：

- **游戏主体支持**：最稳，能直接管理私有 `_tables` 和完整初始化顺序。
- **AuraToolsExp/Bootstrap 注入**：可以用反射/现有 hook 后端实现原型，但对游戏版本字段和生命周期高度敏感，应视为兼容层，不应直接把 `GameConfigManager.Init()` 当重载 API。

### 方案 B：稳定 Bootstrap + 可重载声明式内容，最适合当前仓库

保持 `Entry.dll`、`Aura.Shared.dll` 不变，把高频迭代内容放到可重读的 JSON/CSV/资源 registry 中：

- 稳定 DLL 提供行为解释器、handler registry 和 generation router。
- 配置/资源更新后产生新的 immutable snapshot。
- 新创建的游戏对象使用新 generation；主菜单切换时清理旧 generation。
- DLL 代码变化仍提示重启。

这不是任意代码热重载，但可靠性和收益比最高，也符合当前 Terrias 已有 registry/shared-runtime 架构。

### 方案 C：开发态 DLL 代际切换，可选

稳定 `Entry.dll` 只做 Bootstrap，实际行为编译成带内容哈希或递增版本的 `Payload.<generation>.dll`，通过稳定接口启动并返回一个统一 activation handle：

```csharp
public interface IModGeneration
{
    string GenerationId { get; }
    IDisposable Activate(IModHost host);
}
```

切换时先 Dispose 旧 activation，再把稳定 dispatcher 指向新 generation。旧程序集仍留在进程内，所以必须：

- 所有游戏回调只经过稳定 dispatcher。
- 禁止把 payload 自定义类型放入游戏长期状态。
- 所有 hook、event、coroutine、Unity object、线程和静态引用都可追踪释放。
- 共享依赖保持稳定，不随 payload 热换。
- 限制重载次数，标记为开发工具，不在公开联机中启用。

### 方案 D：通用完整热重载，不建议在现运行时上投入

要可靠支持任意第三方 `Entry.dll`，需要游戏主体提供：

- 可卸载程序集隔离环境；
- 明确的 `Activate/Deactivate` 生命周期；
- owner 化的 hooks/events/network/UI/resources；
- immutable runtime snapshot 与 generation barrier；
- MOD 能力声明和不兼容回退；
- 联机一致性协议和安全信任链。

这接近重做 MOD Loader，而不是在现有 `Assembly.LoadFrom` 上加一个 Reload 按钮。

## 联机热同步的附加设计

如果未来要在多人模式不退出进程地同步，建议只在**房间准备阶段**执行，应用时允许断开并自动重连，但不要求退出游戏进程。

协议建议：

1. Host 发布的是 **active manifest**，不是磁盘上的 desired Enabled。
2. Client 通过可信 Workshop ID 或用户确认的来源下载，不接受房主直接下发并自动执行任意 DLL；否则等价于远程代码执行。
3. `Prepare(generation, manifestHash)`：所有客户端 staging、校验、预构建。
4. `Ready(playerId, generation, hash)`：客户端报告同一哈希。
5. Host 只有在全部 Ready 后发送 `Commit(generation)`。
6. 任一失败则 `Abort`，所有端继续旧 generation。
7. DLL/MVID 变化默认要求进程重启；内容级变化才允许安全点热应用。

当前 AuraTools manifest 应先补：

- `contentHash`、`assemblyMvid`、`activeGeneration`、`reloadCapability`。
- 版本/哈希差异的下载更新计划，而不只是 Enabled 差异。
- active 与 desired 分离。
- 不发送本地绝对 `DirectoryName` 给其他玩家。

## 当前需要先修正的语义问题

MOD 管理器和 AuraTools 同步都会在不真正加载/卸载的情况下修改运行中 `modConfigs[i].Enabled`：

- 启用一个启动时未加载的 MOD后，它可能被联机 manifest 当作已启用，但运行时没有它的数据和代码。
- 禁用一个已经加载的 MOD后，它可能被 manifest 当作已禁用，但 hooks、Data 和 DLL仍然活跃。

证据：

- 游戏原生：`ModManagerUI.cs:1099-1112`
- AuraTools：`AuraOnlineHostModSyncSession.cs:451-467`
- manifest 读取 `modConfigs.Enabled`：`AuraOnlineLocalModManifestBuilder.cs:21-40, 74-84`

无论是否继续做热同步，都应先建立两个状态：

- `DesiredEnabled`：磁盘配置，下一次加载希望启用。
- `ActiveGeneration/IsActive`：当前运行时实际加载的代际。

联机兼容检查必须使用后者。

## 最终判断

- **当前版本能否直接不重启热同步？不能。** 现有 Sync 只到磁盘和启用状态，当前 Loader 也不具备可重入、反注册和程序集卸载能力。
- **能否通过工程改造实现？能，但应从有限范围开始。** 推荐先做主菜单安全点的 Data/Text/声明式资源热重载，并把 DLL 改动继续列为 restart-required。
- **能否实现任意 MOD、战斗中、联机中的无感 DLL 热同步？在当前 `net472 + Assembly.LoadFrom + 全局静态注册` 架构下不现实，也不应承诺。**

推荐决策：先落地方案 A + B；方案 C 仅服务开发调试；暂不做方案 D。
