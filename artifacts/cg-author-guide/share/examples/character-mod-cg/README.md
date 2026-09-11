# 角色 MOD CG 配置示例

完整手册：[角色 MOD 的 CG 配置手册](../../character-mod-cg-configuration-guide.zh-CN.md)。

这是一组可复制的配置模板，不是可单独加载的角色 MOD。不含角色数据、.modproj、ModConfig.json、DLL 或 CG 图片。

## 使用方法

1. 在已有角色 MOD 中合并 `SharedResources` 文件夹。已有清单须合并条目，不要覆盖原有内容。
2. 把三份 JSON 的 `MoonlightMod`、角色 ID、技能 ID 换成实际值；同步修正共享资源路径。
3. 放入自己的图片：`SharedResources/CG/Luna/skill.png`。
4. 保留自己的唯一 `.modproj` 与对应发布身份。
5. 同时启用角色 MOD 和 AuraToolsExp，在工具箱“刷新共享资源”后，到“角色 CG”中选择角色、技能和该资源，先预览，再实战验证。

首次模板中的 CG 显示名称为“露娜 · 月华绽放”，资源包版本为 1。更新图片内容后递增 `packageVersion`。

CG 发现只需要本例的三份 JSON。无需 audio.registry.json；不要新增不存在的可选贡献。

图片故意未附带，请使用作者自己的素材。不要将当前示例目录直接作为发布包，否则它缺少图片且角色、技能 ID 仍是虚构值。

