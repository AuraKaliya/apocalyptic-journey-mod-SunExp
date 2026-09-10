**多人联机审查证据**

本目录保存 2026-09-07 的审查基线及后续开发验证。产品发布凭据位于 `artifacts/shared-release/Release`。

- `test-results.json` / `test-results-domain.json`：16 项现有检查的退出结果、耗时及日志位置。
- `*.log`：原始检查输出。
- `managed-fingerprints.json`：仓库 Managed 与 v1.0.24831968 快照的比对。
- `source-and-binary-fingerprints.json`：重点源码、现有产品输出和包内 DLL 的指纹。
- `rpc-inventory.txt`：本轮枚举的 50 个自定义 RPC 类型。
- `boundary-probes.jsonl`：修复前源码的最小边界行为复现。
- `BoundaryProbe`：链接 `Baseline` 中冻结的 `e3054a8e8124be0e6d88f346de1f739910af5920` 源码，仅用于重现原始缺陷。NativeStubs 不代表 Mirror、Unity 或真实存档行为。
- `development-*.log` / `dev-Test-*.log`：开发阶段编译与当前正式回归测试输出。
- `unity-validation/latest.json`：开发阶段 Unity PlayMode 结果及生产源码哈希。

在仓库根目录执行：

```powershell
dotnet run --project artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/BoundaryProbe.csproj -c Release
dotnet run --project artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/BoundaryProbe.csproj -c Release --no-build -- epoch 0
dotnet run --project artifacts/multiplayer-audit/2026-09-07/BoundaryProbe/BoundaryProbe.csproj -c Release --no-build -- epoch 1
```

默认探针断言原始缺陷存在；退出成功不表示当前产品有这些缺陷，也不表示联机正确。正式回归归入 Core、Shared Network、Terrias Multiplayer 和 AuraTools 测试套件。本探针没有网络注入、游戏启动、玩家数据访问或产品发布操作。

原始审查的精灵、元素、哥伦比娅检查使用当时已有编译输出；开发阶段已重新编译产品并改用规范共享输出。训练程序与 TestMods 的原有工作区改动保留。未进行真实双端／四端联机验收。
