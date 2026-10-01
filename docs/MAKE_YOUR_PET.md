# 一只宠物由哪些文件组成

每只宠物有一个**名字（key）**，比如 `example`、`mochi`。它的文件放在两个文件夹里：

```
art/<key>/          像素图的文本源文件（你改的是这些）
  palette.txt         调色板：一个字符 = 一种颜色
  parts/*.txt         部件：头、眼睛、耳朵、头发、身体、手臂、腿、尾巴、道具、特效……
  clips.txt           动作表：每个动作的每一帧由哪些部件组成、停多久
  variants.txt        造型（比如长发/短发），可选
  outfits.txt         衣柜：每套衣服是一张换色表，可选
assets/<key>/       程序读的东西
  character.txt       角色档：名字、编号、她/他、吃的、功能开关、和谁是伙伴
  lines.zh.txt        内置台词
  *.atlas, icon.ico   由 .\build.ps1 art 生成，别手改
```

最快的开始方式：

```powershell
.\build.ps1 new mochi                 # 复制示例宠物 → art\mochi\ 和 assets\mochi\
.\build.ps1 art -Char mochi           # 生成图集和预览图（art\out\mochi\preview\）
.\build.ps1 run -Char mochi           # 运行看看
```

## 从哪里改起（由易到难）
| 想改什么 | 改哪里 | 难度 |
|---|---|---|
| 名字、她/他、快捷键 | `character.txt` 的 `name`、`nick`、`pronoun`、`hotkey` | ⭐ |
| 说的话 | `lines.zh.txt`（或者运行后在「小窝 → 台词」里加） | ⭐ |
| 头发、皮肤、衣服、耳朵的颜色 | `palette.txt` 改颜色值 | ⭐ |
| 衣柜里的衣服 | `outfits.txt`：复制一段，改颜色 | ⭐⭐ |
| 吃的东西（名字、饱腹、台词） | `character.txt` 的 `[food …]` 段落 | ⭐⭐ |
| 发型、耳朵形状、尾巴、脸 | `parts/*.txt` 里对应的部件 | ⭐⭐⭐ |
| 新的吃的动画、新道具 | `parts/` 加道具，`clips.txt` 加动作 | ⭐⭐⭐⭐ |

部件和动作的格式详见 [art/README.md](../art/README.md)，动画节奏的规则见 [ANIMATION_GUIDE.md](ANIMATION_GUIDE.md)。

## character.txt 要点
- **`id`**：
  - 决定数据文件夹（`%APPDATA%\<id>`）、开机自启、单实例锁。
  - **发给别人以后不要再改**，改了设置和记录就找不回来了。
  - 只能用英文字母和数字。
- **`look`**：默认造型，必须是 `variants.txt` 里的一个名字。没有 variants 就写 `base`。
- **`features`**：
  - `wardrobe`：衣柜和换发型，需要 `outfits.txt`，以及 `change_clothes`、`potion`、`haircut` 三个动作。
  - `calm`：防干扰模式。
- **`[food id]`**：每样吃的一段。
  - `clip`：用哪个动作。
  - `cues = 1:台词场景 9:yummy`：第几帧说哪个场景的台词。
  - `amount`：能吃多饱。
  - `gassy = yes`：吃完更容易放屁。
- **两只做伙伴**：
  - 两只宠物各自把对方的 id 写进 `partner`。
  - `duty` 设成不同的数字，数字大的那只负责举牌、健康提醒和专注监督。
  - 快捷键要不一样。

## 台词
- `lines.zh.txt` 一个 `[场景]` 下面一行一句，随机挑一句说。
- 测试会检查每个需要的场景都有台词，所以删场景前先跑 `.\build.ps1 test`。
- `{name}` 会换成宠物现在的名字（用户可以在设置里改名）。
- 场景列表和每个场景什么时候说，运行后打开「小窝 → 台词」页就能看到。

## 改完怎么检查
1. `.\build.ps1 art -Char <key>`：有错会指出哪个文件哪一行。成功后看 `art\out\<key>\preview\`：
   - `_sheet.png`：所有动作的第一帧
   - `clip_*.png`：每个动作的每一帧
   - `gif_*.gif`：动图
   - `_outfits.png`：所有衣服
2. `.\build.ps1 test`：测试全过。
3. `.\build.ps1 run -Char <key>`：真机看看。
