# 魔女档案 GPT-IMAGE2 生成方案

## 输入参考图

- `SunExp/ModResource/Images/Character/WuNa.png`
  - 角色：乌娜 / 曜日魔女
  - 参考重点：金发、黑白礼装、日轮、白曜火焰、白金神圣感
- `SunExp/ModResource/Images/Character/Loneer.png`
  - 角色：洛奈尔 / 晨星魔女
  - 参考重点：蓝紫长发、星幕薄纱、怀表/星坠、晨星与星谱气质

## 核心文案

```text
魔女档案
角色专题 / Witch Archive
黑日之后，黄金梦碎，晨星晦暗，白曜升濯

乌娜
曜日魔女
日耀 · 余烬 · 圣冕显化

洛奈尔
晨星魔女
星谱 · 奇迹时钟 · 关键牌复制
```

## 推荐调用

GPT-IMAGE2 使用多参考图编辑路径，让模型只参考角色形象并重绘整张宣传海报：

```powershell
python C:\Users\75601\.codex\skills\.system\imagegen\scripts\image_gen.py edit `
  --model gpt-image-2 `
  --image SunExp\ModResource\Images\Character\WuNa.png `
  --image SunExp\ModResource\Images\Character\Loneer.png `
  --prompt-file docs\Terrias\Posters\witch_archive_gpt_image2_prompt.txt `
  --no-augment `
  --size 2160x3840 `
  --quality high `
  --output-format png `
  --out docs\Terrias\Posters\witch_archive_role_poster_gpt-image2.png
```
