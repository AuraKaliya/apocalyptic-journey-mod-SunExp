# AuraSkinShared 皮肤资源包

Mod 内的 `SharedResources/Skins` 是发布源，不是运行时读取目录。Mod 加载时通过
`AuraSkinRuntime.RegisterPackage` 将资源安装到 `ModsData/AuraShared/Skins`，运行时只扫描共享目录。

## 包结构

```text
SharedResources/Skins/
├─ package.json
└─ TargetMod_FullCareerId/
   ├─ character.json
   └─ summer_cool/
      ├─ skin.json
      ├─ Character.png
      └─ Idle/
         ├─ config.json
         └─ Idle_00.png
```

`package.json` 使用整数版本。相同 `(targetCareerId, skinId)` 和相同内容哈希会被去重；同一所有者只有提高
`packageVersion` 才能更新不同内容。不同所有者提供不同内容时会被拒绝，避免由 Mod 加载顺序决定覆盖结果。

`skin.json` 必须使用 `schemaVersion: 2`。资源路径只能指向当前皮肤目录内部，至少声明一项有效资源。

皮肤选择保存在 `ModsData/AuraShared/Config/Shared/Skin/selections.json`，安装记录保存在
`ModsData/AuraShared/Registries/Skin/resources.json`。移除来源 Mod 不会自动删除已安装的共享资源。
