# Aura Foundation Trainer ModelData

该目录保存可迁移的底模训练状态，只包含：

- 当前便携训练参数
- 专家回放案例
- 奖励残差观测

不包含旧运行结果、检查点、完整成功案例副本或日志。

## 安装

新机器拉取仓库并安装 Git LFS 后，在仓库根目录执行：

```powershell
.\ModelData\Install-ModelData.ps1
```

脚本会自动：

1. 校验压缩包 SHA-256。
2. 解压并核对专家案例与观测文件数量。
3. 备份新机器已有的训练参数。
4. 将参数和案例合并安装到正确的 `ModsData` 目录。

仅校验压缩包、不写入 `ModsData`：

```powershell
.\ModelData\Install-ModelData.ps1 -VerifyOnly
```

运行安装脚本前请关闭 Aura 底模训练控制台和 Worker。安装完成后启动控制台，点击“开始 / 恢复训练”即可开始一轮新训练。
