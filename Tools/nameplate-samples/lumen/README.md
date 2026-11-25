# Lumen 名牌全集

这套资源从 `name_plate.nulm` 解出的 `layout.json` 和 `name_plate.nutexb` 解出的 `atlas.png` 重建。图形和动画均使用标准 v1 `.otanim`。完整名牌的 `composition/catalog.json` 记录每种布局的绘制顺序、动态文字框以及可替换部件。

## 取得这套素材

烘焙结果 `set/` 不进仓库（被 `.gitignore` 忽略），街机的图不放进公开仓库，同舞台素材的 `Assets/OurTaiko/StageSource`。放进来有两种方式：

- 有解出的街机名牌：用 `bake.py` 烘焙，见「重新生成和验证」。
- 没有：取已发布的那套，也就是玩家下载的那套。目标文件夹必须不存在或为空，不会覆盖烘焙出的：

  ```sh
  python3 -I Tools/nameplate-samples/lumen/fetch.py Tools/nameplate-samples/lumen/set
  ```

  它读分发根上的 `nameplate/lumen.json`，逐个核对大小和 SHA-256，按烘焙的布局写成 `<文件夹>/<名字>.otanim` 和 `composition/catalog.json`。

没有 `set/` 时，Nameplate Preview 只画文字，并提示上面两种方式；Unity 里要看这套图的测试会被忽略（Ignored），Go 测试会跳过。CI 的 Test 工作流在跑测试前先用 `fetch.py` 取线上那套。

## 在 Unity 里查看

1. 打开 **OurTaiko → Nameplate Preview**。预览读本地的 `Tools/nameplate-samples/lumen/set`（发布出去的也是它）。游戏不带这套素材：每次登录检查时从分发根读 `nameplate/lumen.json`，缺的文件按哈希下载进对象缓存，之后离线也能用；一次也没下载成功时，名牌只有名字和称号文字。
2. **Layout** 选 `auto` 时，座位、称号和段位决定主布局。例如 2P 同时有称号和段位时使用 `2p_shougou_dani`。也可以直接选择 36 种源布局中的任意一种，包括 AI、新玩家、角色联动、Boss 和愚人节。选 **custom** 可在相同的组合布局上分别装备 Band、Effect 和 Icon（见第 3 条）。
3. **Layers** 随布局显示可替换的部件。**Badge** 可跟随布局，也可单独指定 1P 或 2P。**Title** 提供 88 种普通称号，**AI title** 提供 8 种 AI 称号，按系列分子菜单（`toho ▸ toho_y22_3`），◀ ▶ 逐个切换。下面三行列出名牌为这个称号戴的底条、特效和角标，以及每个部件还出现在哪些称号里、特效是否裁在带内；**Customize** 切到 custom 布局并把这三个部件填进三个槽，方便只换其中一个。**Blank** 有 `off`、`on`、`on_ai` 三个状态。姓名和称号可直接编辑，**Load source text** 可读入当前布局的源占位文字。文字使用游戏自己的 UI 字体。
   custom 布局里 Band、Effect、Icon 的菜单分 **Set**（这套的部件，按来自的称号系列分组）和 **Items folder**（自己放进文件夹的文件），◀ ▶ 逐个切换，下方注明出处；**From title** 加 **Wear** 一次填满三个槽。
4. 在段位布局用 **段位**、**Ring**、**Gold pass** 切换 19 个段位、三种底和两种文字颜色。播放区的 **init / start / wait / end** 控制当前段位的升段动画。Pause 后可用 ±1 frame 检查遮罩边缘与阶段衔接。**Show** 的 Band、Effect、Icon、Plate 四个开关只影响画面（也影响 **Save picture…**），名牌照常戴着、照常播放：关掉 Plate 只看三个图层，关掉其中两层就能单独看一个部件。
5. **Wear as 1P** 将当前外观保存到玩家配置。游戏在段位结算进入庆祝时触发开始片段，离开庆祝时触发结束片段。装备目录收录的底条时使用原始衬底，保留伸出称号带的装饰。所有布局共用这套的段位图形、缩放和文字基线校正。三个槽的素材都来自这套称号，可以任意搭配，也可以加 `icon/lumen-rankup`。**Render all…** 出几张表：`titles.png`（全部 96 个称号按玩家戴的样子画，称号栏写着称号名）、`items.png`（每个部件单独戴，称号栏写着 id，各取 6 个时刻）、`layouts.png`（36 种布局）、`combinations.png`（只组合 Items folder 里自己的文件，最多 64 种；文件夹没有文件就不出）。全部 155 份文件约 2.3 MB，各平台都不内置，第一次登录检查时下载，以后只下载新发布的。旧版 player.json 在首次加载时用新版默认名牌配置替换。

## 数据与制作方法

| 内容 | 文件或映射 |
|---|---|
| 36 种名牌布局、45 个动态文本定义 | `composition/catalog.json` 和底图 `plates.otanim`。保留图层深度、颜色、中心、文字框宽度、字号和占位文字 |
| 普通及 AI 称号 | 每个称号是一组部件，记在 `catalog.json` 的 `titles`、`aiTitles`（`{band, effect?, icon?}`）。部件文件：`band/lumen-title-*.otanim`、`effect/lumen-title-*.otanim`、`icon/lumen-title-*.otanim`，AI 称号用 `lumen-ai-title-*`；每个文件一个循环的 `idle`。`unclipped` 列出可以伸出底条的特效 |
| 1P / 2P 徽章、空白状态 | `composition/badges.otanim`、`composition/blank.otanim` |
| 段位底与字 | `composition/dan-{clear,full,donfull}.otanim`，每份包含 38 个命名片段 |
| 升段 | `icon/lumen-rankup.otanim`。初始化对应透明的 `idle`，每个段位都有 `rankup_start`、`rankup_wait`、`rankup_end` |

源画布原点映射到名牌中心 (204, 48)。转换器先求出每帧的显示列表，沿实例树合成变换，再将绘制顺序、顶点、UV、颜色和混合方式写入片段。局部矩形遮罩和三角网格遮罩会裁剪受影响的图形并插值边界 UV。颜色加值在对应的贴图变体中烘焙；加法发光仍由播放器和下方画面混合。

学习嵌套时间轴时可以从 `ai_3` 开始。扫光子精灵有 800 帧，星光有 60 帧；持续显示的子实例保留自己的播放进度，完整循环取 2,400 帧（60 fps 下为 40 秒）。升段则由外部事件驱动：开始片段包含源第 5 至 46 帧，停在第 46 帧等待，结束片段播放第 47 至 70 帧（从 0 计数）。

## 称号的组成

原版把一个称号画成一个精灵：底条、扫光和星光、再加一个站在底条右端的角标（角色头像、皇冠、段位环）。`titles.py` 按每个图形画了什么把它们分开，每个称号变成三个部件，名牌按原版的先后叠回去：

| 部件 | 是什么 | 画在 |
|---|---|---|
| `band` | 铺满底条的图形（含彩虹轮换、皇冠的渐变）。静态的只有 1 帧 | 外框之下 |
| `effect` | 角标之下的扫光、星光、叠加发光，彩带 | 底条之上、外框之下 |
| `icon` | 右端的角标，以及画在它上面的东西（盖着它的闪光） | 外框之上、徽章之下 |

- 角标是不出底条、始终不透明、从底条右端（x ≥ 280）开始的图形；原版里画在它之上又与它重叠的图形，连同重叠链上的一并算角标，否则叠回去时前后会反。
- 特效伸出底条的（金条、紫条、世界大会的彩带等 18 个）记在 `unclipped`，名牌不对它们裁切；只在带内的特效照旧裁在带形里。玩家自己放进文件夹的特效一律裁切。
- 形状相同的部件共用一个文件，名字取使用它的称号：用到的称号只差序号（`toho_y22_1…5`）就写词干，否则写第一个。`pjsekai`、`toho_y22_qr` 与 `rainbow` 用同一条彩虹底条。
- 烘焙时检查每个部件的先后不会在重叠处颠倒，并断言三个部件逐帧合起来恰是原版这一帧的全部图形。叠回去与原整幅称号的差，只在贴图坐标取整造成的边缘（最多约 5/255）；原版把整个称号画在外框之下，现在角标和盖着它的闪光画在外框之上（徽章仍盖住它们）：34 个带角标的称号里，外框与角标重叠处有至多几百个像素与原版不同，多在闪光最强的那几帧。
- 去掉了早期的示例 `band/lumen-ai-3`，它与 `band/lumen-title-ai_3` 相同（那时整个称号是一个文件）。

## 重新生成和验证

需要 Python 3、Pillow、Go，以及与 OurTaikoPlay 同级的 otanim 仓库。从 OurTaikoPlay 根目录执行以下命令。要与已发布的那套逐文件比对，可以用 `fetch.py` 把它取到另一个空目录再 `diff -r`。

```sh
python3 Tools/nameplate-samples/lumen/bake.py ~/Downloads/name_plate_decoded Tools/nameplate-samples/lumen/set
(cd Tools/nameplate-samples && go test ./...)
```

Go 测试用参考解码器和软件渲染器检查全部称号的部件（各在自己的文件夹、没有闲置的文件、底条不出带、角标在右端、`unclipped` 与几何一致）、布局引用、扫光裁剪、段位组合及升段片段衔接。Unity 的 `NameplateLayerTests` 另外通过实际预览入口检查布局切换、片段触发，以及恢复普通名牌。

## 发布

游戏读的是分发根上的这套，改了 `set/` 要发布才会到玩家手里；`set/` 不进仓库，发布出去的就是它的记录，仓库里只提交烘焙工具的改动。在 OurTaikoNet 仓库执行（需要 `aws login`，有 `ourtaiko-public` 桶和 CloudFront 分配 `E29SNWDH0GYCHC` 的写权限）：

```sh
tools/nameplate/publish-lumen.sh <OurTaikoPlay>/Tools/nameplate-samples/lumen/set
```

它把每个文件按 `lumen/<文件夹>/<名字>-<哈希前 12 位>.otanim` 上传（已发布的文件从不替换），最后上传 `nameplate/lumen.json` 并让 CDN 上的它失效。游戏在下一次登录检查时只下载新出现的文件。`set/` 新增的部件或称号，不发新版游戏也能用；目录表格式变了才需要发新版。
