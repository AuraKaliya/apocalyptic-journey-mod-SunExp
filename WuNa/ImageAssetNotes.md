# 乌娜图片资源说明

本次资源基于 `WuNa/乌娜角色设计草稿-余烬魔女调整-transparent.png` 派生。

官方参考来源：

- `Data/Career` 字段：`Character`, `Avatar`, `CareerImage`, `Dialogue`
- `Data/RoleData` 字段：`Avatar`, `CharacterImage`, `HouseAvatar`
- 本地参考目录：`开发参考资料/参考图`

## 参考图分析

| 参考图 | 尺寸 | 格式 | 观察用途 |
| --- | ---: | --- | --- |
| `阿米莉娅-resources.assets-2176.png` | 111x108 | PNG / alpha | 带边框的脸部头像，适合作为 `RoleData.Avatar` 参考 |
| `阿米莉娅-resources.assets-2959.png` | 148x162 | PNG / alpha | 透明头肩头像，适合作为 `Career.Avatar` 参考 |
| `阿米莉娅-sharedassets0.assets-153.png` | 83x84 | PNG / alpha | 小尺寸近脸头像，适合作为 `HouseAvatar` 参考 |
| `技能图标&小对话框_27-resources.assets-511.png` | 377x265 | PNG / alpha | 横向上半身状态/小对话框图，适合作为 `Career.Dialogue` 参考 |
| `阿米莉娅-游戏.png` | 384x384 | PNG / alpha | 方形角色展示/游戏内选择图参考 |
| `阿黛拉-立绘.png` | 1174x1080 | PNG / alpha | 大尺寸半身立绘，适合作为 `RoleData.CharacterImage` 参考 |

## 已生成资源

输出目录：`SunExp/ModResource/Images/Role/WuNa`

| 字段用途 | 文件 | 尺寸 | 格式 | 裁切部位 |
| --- | --- | ---: | --- | --- |
| `Career.Character` | `character.png` | 1024x1536 | PNG / alpha | 完整透明立绘主体，保留全身剪影、环冠、月刃、飘带和火焰 |
| `Career.Avatar` | `avatar.png` | 148x162 | PNG / alpha | 头肩胸像，保留脸、头发、环冠和右侧抬手局部 |
| `Career.CareerImage` | `career_image.png` | 384x384 | PNG / alpha | 方形全身展示，完整保留职业选择时的动态轮廓 |
| `Career.Dialogue` | `dialogue.png` | 377x265 | PNG / alpha | 横向上半身图，包含脸、胸口圣徽、右手与火焰 |
| `RoleData.HouseAvatar` | `house_avatar.png` | 83x84 | PNG / alpha | 小尺寸近脸头像 |
| `RoleData.Avatar` | `roledata_avatar.png` | 111x108 | PNG / alpha | 带简化暗金边框的脸部头像 |
| `RoleData.CharacterImage` | `roledata_character_image.png` | 1174x1080 | PNG / alpha | 对话用大半身图，保留头部、上半身、环冠、月刃和部分动态衣摆 |

## 像素风角色展示

基准图：`WuNa/乌娜角色设计图定稿.png`

风格参考：

- `开发参考资料/参考图/阿米莉娅-游戏.png`
- 用户提供的两张低色素、强轮廓像素角色参考图

| 用途 | 文件 | 尺寸 | 格式 | 说明 |
| --- | --- | ---: | --- | --- |
| 像素风站姿推荐版 | `wuna_game_pixel_standing.png` | 384x384 | PNG / alpha | 按定稿图与站姿参考重绘成低色块、强轮廓、强特征角色展示图；无背景，推荐作为当前像素风展示资产 |
| 像素风角色展示旧版 | `wuna_game_pixel_ai_384.png` | 384x384 | PNG / alpha | 低色块、强轮廓、几何底形角色展示图；已被站姿推荐版替代 |
| 本地降采样透明版 | `wuna_game_pixel_transparent_v2.png` | 384x384 | PNG / alpha | 从定稿图直接像素化，最保留原图剪影，但更像缩略图 |
| 本地降采样几何底形版 | `wuna_game_pixel_v2.png` | 384x384 | PNG / alpha | 本地像素化并添加几何底形，可作为备用或对照 |

## 预览

`SunExp/ModResource/Images/Role/WuNa/preview_contact_sheet.jpg` 是检查用拼图，不建议写入游戏配置。

## 技能图标

生成脚本：`tools/Generate-WuNaSkillIcons.ps1`

预览图：`tools/previews/wuna_skill_icons/contact_sheet.png`

本组图标不使用卡包绘制流程，采用 128x128 低色块绘制后近邻放大到 384x384。设计重点是强轮廓、主体居中、32px 缩略图仍能辨认的大剪影，以及明显区别于卡牌插画的“技能按钮”感。

| 技能类型 | 技能名 | 文件 | 尺寸 | 视觉主体 | 设计意图 |
| --- | --- | --- | ---: | --- | --- |
| 主动 | 白曜圣祷 | `action_white_sun_prayer.png` | 384x384 | 白曜圣冠、圣座、日轮弧光 | 用稳定的冠冕和祈祷光环表现“制造授冕窗口”，金白主体配青色仪式弧线，整体更像可按下的增益技能。 |
| 主动 | 圣庭墓曲 | `action_grave_song.png` | 384x384 | 竖向火刃、葬仪门扉、赤紫火幕 | 用贯穿画面的竖刃形成最强剪影，表现消耗余烬后的自焚与全场焚烧；赤紫色块强化高风险爆发感。 |
| 被动 | 日耀魔女 | `passive_solar_witch.png` | 384x384 | 魔女冠冕、中心日核、双侧火片 | 把“每回合通过灼烧获得日耀”的规则压缩成魔女冠与日核，金色环弧表示日耀上限扩展。 |
| 被动 | 于灰烬中重生 | `passive_ash_rebirth.png` | 384x384 | 灰烬底座、重燃日焰、展开火翼 | 以灰堆中重新立起的火焰表现回合开始吸收全体灼烧并转为余烬，底部灰色块与上升金焰形成重生方向。 |
