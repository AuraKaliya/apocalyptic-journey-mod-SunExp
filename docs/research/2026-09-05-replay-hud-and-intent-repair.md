# 回放中断与血条贴图修复

本轮针对记录 `212dfae0243c4de8a668d95ddb23bd30`。游戏与上一轮安装包
DLL 哈希一致。记录包含 546 条真值事件、313 条表现事件，以及各 12 个检查点。

## 首个错误与原因

`Player.log` 表明首帧、主帧屏障和激活均成功；随后精灵意图加载
`Icon/ActionIcon/给予异常` 失败，播放器关闭。后续错误上报服务异常不是首因。
安装包 ActionIcon bundle 实际只有 `给与异常`，没有 `给予异常`。
Terrias 的精灵/投影表现发布者过去直接保存配置字段，遗漏了原生
`OtherObj.UpdataActionShow` 对不存在图标使用“蓄力”、不存在底图使用“攻击底”的规则。

修复后的发布者先解析真实资源，声明 `native-intent-resolved.v1`；录制入口
验证这一声明，播放器在创建场景前预检全部扩展意图。新声明的路径缺失会明确失败。
历史未声明的 schema-1 路径按同一游戏版本的原生规则只读解析，保持封存字节不变。
空路径、未知协议/版本、主资源和原生替代资源同时缺失均被拒绝。
同时修正 Terrias EnemyCard 表中 14 处同名错误引用；34 行的原生图标和底图
均已与安装包的 56 个实际 Sprite 路径核对。

## 血条贴图

原生 `HpItem/background` 与护盾装饰使用 `Sprite-Lit-Default`，血量填充
使用 unlit `Shader Graphs/FillAmount`。原来的回放相机只绘制 layer 30，
URP 将 layer 0 的全局光源一并剔除；场景仍被判为 lit，背景/护盾因而获得黑色光照。

新增的回放专用 renderer feature 在原生剔除后、创建 2D 光照批次前，将原有
活动全局光源加入回放自身的剔除结果。保留相机对象隔离、原材质和实际贴图。
没有复制光源、注册重复 Global Light、改动原场景灯光或替换血条着色器。

## 验证范围

- AuraTools 全套验证通过，行为测试 1444 条断言；产品 Release 编译零警告/错误。
- Terrias C# 878 条断言通过；架构、内容、MOD 资源检查通过。
- Unity 6000.0.46f1 / URP 17.0.4 GPU PlayMode 共 13 项通过。
  合成颜色贴图复现旧行为；从实际安装包提取的血条边框及护盾 Sprite
  修复后与正常相机参考帧逐像素一致。重复手动帧及返回正常相机检查通过。
- 这轮实际记录通过生产 v17 文档校验；21 条扩展意图全部通过生产资源
  预检算法，使用安装包真实图标目录。历史错误请求仍选择当时原生显示的“蓄力”。
- 数据库只读；原始 document root：
  `8a5d82601189a6a304d433688e991498f7a157a8d4a5ee9a5e722bedb98b8296`。
  压缩文档 SHA256：`afd0318b38b9a86a077096763c7a7df360a9bf6d614eb3c9f53073b2f18652ea`。

本轮没有控制用户游戏窗口。完整游戏中的播放到结束、拖动、变速、导出、
关闭后重开及下一场战斗尚未执行，不以自动测试替代这组验收。

## 发布与安装

最终产品发布事务：`5d5765d4b4024315803271ecafc7af17`。
已在确认游戏进程关闭后更新安装目录内两个 MOD 的 Entry/共享 DLL 及
Terrias EnemyCard 表，逐文件验证与发布目录一致：

| 文件 | 安装后 SHA256 |
| --- | --- |
| AuraToolsExp Entry.dll | `5CF872AB54FF442B55921F68A04BC8372F3F1904C8FEC9915B0F96CD9FAFFC2C` |
| Terrias Entry.dll | `27903F10A4366D516E7B70544F5D1E34E09FA0732ED48C84C66DE7E6BF400F10` |
| 两个 Aura.Shared.dll | `F297B2AE54E5F97CDD671A02E09999E139759352A572F5FEEA2517476B9EFDCD` |
| Terrias EnemyCard/terrias.csv | `1542CFE4CFFC876BDBD1090A8FB12308A5228500C14C8217D83C90336347ED74` |

原文件及安装清单位于 `output/replay-guide-fix/hud-backup-20260905-171411`。
此次不改动共享 ABI，不删除或重写用户回放和存档。
