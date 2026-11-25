# Lumen 名牌全集

这套资源从 `name_plate.nulm` 解出的 `layout.json` 和 `name_plate.nutexb` 解出的 `atlas.png` 重建。图形和动画均使用标准 v1 `.otanim`。完整名牌的 `composition/catalog.json` 记录每种布局的绘制顺序、动态文字框以及可替换部件。

## 在 Unity 里查看

1. 打开 **OurTaiko → Nameplate Preview**，点击 **Install Lumen collection**。安装器把资源复制到游戏的 `Nameplate` 目录，保留已有同名文件。
2. **Layout** 选 `auto` 时，座位、称号和段位决定主布局。例如 2P 同时有称号和段位时使用 `2p_shougou_dani`。也可以直接选择 36 种源布局中的任意一种，包括 AI、新玩家、角色联动、Boss 和愚人节。选 **(standard)** 可回到普通名牌和三个独立装备槽。
3. **Layers** 随布局显示可替换的部件。**Badge** 可跟随布局，也可单独指定 1P 或 2P。**Title band** 提供 88 种普通称号，**AI title band** 提供 8 种 AI 称号。布局决定哪种部件可见。例如在 `1p_shougou` 上选 `pjsekai` 看联动称号，在同一布局上选 `ai_3` 看扫光。**Blank** 有 `off`、`on`、`on_ai` 三个状态。姓名和称号可直接编辑，**Load source text** 可读入当前布局的源占位文字。文字使用游戏自己的 UI 字体。
4. 在段位布局用 **段位**、**Ring**、**Gold pass** 切换 19 个段位、三种底和两种文字颜色。播放区的 **init / start / wait / end** 控制当前段位的升段动画。Pause 后可用 ±1 frame 检查遮罩边缘与阶段衔接。
5. **Wear as 1P** 将当前外观保存到玩家配置。游戏在段位结算进入庆祝时触发开始片段，离开庆祝时触发结束片段。普通三槽名牌装备目录收录的称号时使用原始衬底，保留伸出称号带的装饰。安装了对应段位部件时，普通名牌与 auto 共用段位环和段位字的原始图形、位置与缩放；缺少部件时使用游戏自带段位贴图。独立 Effect 仍限制在带内，未安装时显示空槽提示。普通三槽名牌也可以单独装备任一称号底图或 `icon/lumen-rankup`。桌面、移动端使用相同的玩家资源目录结构，完整外观需要该设备也安装整套 `items/` 内容。

## 数据与制作方法

| 内容 | 文件或映射 |
|---|---|
| 36 种名牌布局、45 个动态文本定义 | `composition/catalog.json` 和底图 `plates.otanim`。保留图层深度、颜色、中心、文字框宽度、字号和占位文字 |
| 普通及 AI 称号 | `band/lumen-title-*.otanim`、`band/lumen-ai-title-*.otanim`，每个文件都有可独立播放的 `idle`。早期示例名 `lumen-ai-3` 也保留 |
| 1P / 2P 徽章、空白状态 | `composition/badges.otanim`、`composition/blank.otanim` |
| 段位底与字 | `composition/dan-{clear,full,donfull}.otanim`，每份包含 38 个命名片段 |
| 升段 | `icon/lumen-rankup.otanim`。初始化对应透明的 `idle`，每个段位都有 `rankup_start`、`rankup_wait`、`rankup_end` |

源画布原点映射到名牌中心 (204, 48)。转换器先求出每帧的显示列表，沿实例树合成变换，再将绘制顺序、顶点、UV、颜色和混合方式写入片段。局部矩形遮罩和三角网格遮罩会裁剪受影响的图形并插值边界 UV。颜色加值在对应的贴图变体中烘焙；加法发光仍由播放器和下方画面混合。

学习嵌套时间轴时可以从 `ai_3` 开始。扫光子精灵有 800 帧，星光有 60 帧；持续显示的子实例保留自己的播放进度，完整循环取 2,400 帧（60 fps 下为 40 秒）。升段则由外部事件驱动：开始片段包含源第 5 至 46 帧，停在第 46 帧等待，结束片段播放第 47 至 70 帧（从 0 计数）。

## 重新生成和验证

需要 Python 3、Pillow、Go，以及与 OurTaikoPlay 同级的 otanim 仓库。从 OurTaikoPlay 根目录执行以下命令。将输出指向空目录即可与仓库内的成品做逐文件比对。

```sh
python3 Tools/nameplate-samples/lumen/bake.py ~/Downloads/name_plate_decoded Tools/nameplate-samples/lumen/items
(cd Tools/nameplate-samples && go test ./...)
```

Go 测试用参考解码器和软件渲染器检查全部称号、布局引用、扫光裁剪、段位组合及升段片段衔接。Unity 的 `NameplateLayerTests` 另外通过实际预览入口检查布局切换、片段触发，以及恢复普通名牌。
