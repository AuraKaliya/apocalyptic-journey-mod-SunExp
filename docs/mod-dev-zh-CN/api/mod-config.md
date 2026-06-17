# ModConfig API

`Witch.Mod.ModConfig` 是 Lua 与 DLL MOD 在初始化时接触到的设置对象。

源码锚点：

- `开发参考资料/反编译文件夹v1.0.23715745/Witch/Mod/ModConfig.cs`

## 加载期职责

MOD 加载时，游戏大致会：

1. 为 MOD 创建 Lua table
2. 绑定 `self` 与 `ModConfig`
3. 如果存在 `Scripts/Entry.lua`，执行它
4. 调用 `Setup`
5. 如果存在 `Scripts/Entry.dll`，加载 DLL
6. 调用带 `[ModInitialize]` 的静态方法
7. 注册带 Hook 属性的静态方法

因此对 DLL 项目来说，`Entry.lua` 是可选的，但发布态 DLL 必须命名为
`Scripts/Entry.dll`。

## 数据与资源 API

`SetDataConfig(id, newData)`

按 ID 更新已加载配置行。不会替换 `Id` 字段。

`ModifyDataConfig(id, key, value)`

更新已加载配置行里的单个字段。

`MergeDataConfig(source, target)`

把一个前缀下的行合并到另一个前缀。

`RedirectSourcePath(originalPath, newPath)`

重定向资源路径。

## Hook API

`AddMethodHookBefore(typeDotMethod, fn)`

注册前置 Hook。目标字符串形如 `SettingUI.OnEnable`。

`AddMethodHookAfter(typeDotMethod, fn)`

注册后置 Hook。

DLL 重载可以使用 `Action<ModHookContext>`、类型名加方法名，或 `Type` 加方法名
注册 Hook。

## DLL 属性

`[ModInitialize]`

标记初始化时调用的静态方法。通常接收当前 `ModConfig`。

`[HookBefore]` 与 `[HookAfter]`

注册静态 Hook 方法。实例方法 Hook 在适配器能绑定时，会把目标实例作为第一个
参数传入。

## 实用规则

- 初始化逻辑应尽量幂等。
- 大系统优先在 `Hooks/*Runtime.cs` 中显式注册 Hook。
- 依赖 Hook 前，先在反编译快照中确认目标方法签名。
- C# 项目中，发布文件名虽然都是 `Entry.dll`，但内部 assembly name 应保持唯一。
