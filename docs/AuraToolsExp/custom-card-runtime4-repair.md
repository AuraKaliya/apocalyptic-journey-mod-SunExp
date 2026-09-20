# 自建卡牌：运行兼容性与界面修复

日期：2026-09-18。输入证据为 `AuraTools-20260918-005539.log` 和用户提供的属性页、像素画板截图。

## 运行故障

日志在 01:01:15.836 记录 `UseScript:16: attempt to call a nil value (method 'get_Item')`，出错节点为 `a7e7d3b2fcdc4fee927d4dad94ddc732`。错误发生在效果执行前备份 `ScriptExecutor.Object` 的集合遍历，因此会出现消耗能量但没有执行效果。

匹配版本的 `XLua.Utils` 会隐藏整数索引器的 `get_Item` 方法，向 Lua 暴露零基下标访问；`Dictionary<string,string>` 的生成包装器则明确注册了 `get_Item`／`set_Item`。因此只将对象集合、选牌来源与选择结果的整数访问改为 `xs[i]`，保留本牌费用和 Vars 的字符串字典访问。

旧测试替身提供了列表 `get_Item`，与真实宿主不一致，曾掩盖此故障。现在替身只提供零基下标，新增 `tools/Test-CustomCardXLua.ps1`：加载游戏实际 Witch.dll 与 xlua.dll，在 Unity 6000.0.46f1 中执行生产生成的 Lua，使用真实 CLR 集合和字典，复现旧错误并检查修复。战斗服务是隔离替身，不能把这份结果声称为真实对局或联机验收。

`CardBlueprintScriptCompatibility` 修复存量成品的已知片段。`CustomCardNative.RepairPersistedScripts` 在备份旧 RawData 后更新可持久化数据，并移除受影响的 ScriptDict 项。DataConfig 的读取、预编译和重置均接入修复。原始脚本的生成器标题与其余逻辑保持不变，以区分“集合访问修复”和完整重新编译。新生成脚本的编译器标题为 4；蓝图文档仍为 v3，原生卡面标记仍为 2。

## 界面与费用

- 顶层合并为“工坊菜单／当前作品操作”和工作区页签，减少原先多排按钮。
- 对话框根据页面内容限制尺寸；属性表单与实时卡牌预览并排，基础信息和特性风味分组。
- 工坊拥有独立窗口、字段和按钮外观；像素画板的色板使用实际颜色，避免再次乘上按钮主题色。
- 画板自动适合视口，工具和色板集中在侧栏；显示选中色，提供适合窗口、缩放、网格及中键移动。
- 制作提示明确为“制作免费；使用费用为 X 点能量”，制作前提交焦点输入，避免尚未提交的费用编辑与成品不一致。

## 验证与安装

专项逻辑与生成 Lua 测试：559 项断言。模块回归：2126 项断言及工具内容检查。UI 四档尺寸新增制作费用与画板面积／色板检查，覆盖 52 个截图及原有交互、注释生命周期场景。

实际 XLua 检查覆盖旧错误复现、新脚本、迁移后的旧脚本、随机结果／空集合、选牌完成与重复回调、弃牌、焚毁和原生字符串字典。检查结果保存在 `output/custom-card-xlua/latest.json`；UI 结果在 `output/unity/custom-cards/captures/report.json`。

本次使用 `tools/Invoke-AuraValidatedRelease.ps1` 完成 25 项发布检查，随后由原部署器安装并验证，事务为 `84a611c3bc0e483da582fb3da5d7d937`。实际 XLua 检查为 9 个场景，UI 为 52 个场景；源文件哈希与验收镜像一致，diff 空白检查通过。

安装目标为 `D:/Steam/steamapps/common/Witch's Apocalyptic Journey/Witch's Apocalyptic Journey_Data/Mods`。AuraToolsExp 的仓库与已安装 `Scripts/Entry.dll` SHA256 均为 `F6C1C2BC2BA6F86FCBCAA6662FF0AED021490A30B26C7827A1EE383FD1459E7D`。共享 DLL 沿用 `E782AF79C117DB45F45EAA603485E44C2EF03AC7EFB7B66F373E32BE38902E49`。

安装收据和备份位于 `artifacts/product-deploy/84a611c3bc0e483da582fb3da5d7d937/deployment.json`，状态为 `Verified`。完整包发布器同步了 2 个不同文件：AuraToolsExp 的 Entry.dll，以及仓库中原先已有的 `Terrias/ModResource/Images/Card/MoreDimension/zhun_kao_zheng.png`；该资源源文件未由本次修改。

真实战斗与主客机验收仍独立于这些自动化检查，未在本轮启动实际对局。已有成品的自动修复将在下次游戏读取时执行；本轮没有离线改写玩家的账号存档。构建保留 4 处用于兼容 Unity 2022 预览的 TMP 换行属性弃用提示，无产品编译错误。
