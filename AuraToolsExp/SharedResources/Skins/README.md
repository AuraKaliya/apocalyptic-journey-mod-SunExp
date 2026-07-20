# AuraSkinShared 皮肤资源包

Mod 内的 `SharedResources/Skins` 是发布源，不是运行时读取目录。Mod 加载时通过
`AuraSkinRuntime.RegisterPackage` 注册到共享层。v4 运行时目录为
`ModsData/AuraShared/Skin/Role/<角色ID>/Skin/<MOD>/<皮肤ID>/content/`。
旧版 `ModsData/AuraShared/Skins/<角色ID>/<皮肤ID>/` 仅作为已注册资源的兼容读取别名；
未在本次会话注册的残留目录不会被扫描启用。

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

`package.json` 使用整数版本，并可通过 `participantKind` 声明 `Content` 或 `Tool`。
同一所有者和包的重复注册是幂等的；多个包不会互相覆盖活动租约。

`skin.json` 必须使用 `schemaVersion: 2`。资源路径只能指向当前皮肤目录内部，至少声明一项有效资源。

皮肤选择保存在 `ModsData/AuraShared/Config/Shared/Skin/selections.json`。移除来源 Mod
不会自动删除已安装资源，但没有当前会话租约时不会生效。
