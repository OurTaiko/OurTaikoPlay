# OurTaikoPlayerUnity

从相邻 `OurTaikoPlayer` 提取单人游玩页面的 Unity 2D 工程。使用 **Unity 6000.3.25f1 / Universal 2D / uGUI + TextMeshPro**。所有新增运行逻辑和 Editor 工具均为 **C#**，不包含 C++、Lua 或原项目的原生插件。

## 运行

1. 用 Unity Hub 打开本目录。
2. 打开 `Assets/Scenes/SceneSwitcher.unity`，点击 Unity 的 Play。
3. 选择 TRIPLE HELIX（含音乐）、Input Calibration（原项目的无音乐校准谱）或 Branch Training（分支练习谱），点击 PLAY / 按 Enter。

也可直接打开 `Assets/Scenes/PlayScene.unity` 运行，默认载入 TRIPLE HELIX。

| 按键 | 功能 |
| --- | --- |
| F / J | 左 / 右咚 |
| D / K | 左 / 右咔 |
| Space | 暂停 / 继续 |
| F1 | 重开 |
| Esc | 返回入口 |
| Tab / A（入口） | 切歌 / 自动演奏 |

页面下方有可点击的咚 / 咔按钮。两个场景采用 Nijiiro 原生 **1920×1080** 画布与固定设计区域，宽高比变化时居中留边，保持判定位置和轨道比例。

默认目标为 **120 FPS**。入口及直接打开游玩场景时都会关闭 VSync 并启用每帧渲染，避免画质档的垂直同步覆盖目标帧率。左上角 FPS 计数器每 0.5 秒显示实际帧率平均值，暂停时仍持续更新。它显示实测值，不是目标 120 的固定文字。

如果 Unity 内仍约为 60 FPS，检查 Game 视图的 VSync 选项、系统显示器刷新率及节能设置，并与独立播放器比较；Editor 自身的负载也可能降低帧率。120 FPS 是渲染目标，不会把 60 Hz 显示器变成 120 Hz。

## 分支游玩

支持 `#BRANCHSTART p,玄人阈值,达人阈值`（命中率）和 `r`（连打数），以及 `#N`、`#E`、`#M`、`#BRANCHEND`、`#SECTION`。按相邻 OurTaikoPlayer 的规则，在分支首个对象进入画面时确定路线；未选路线不会显示、判定或计分。分支谱面沿用原皮肤的轨道配色：普通为深色、玄人为蓝色、达人为紫色；轨道右侧显示「普通譜面／玄人譜面／達人譜面」图片字样。路线变化时按原版滑动、淡入淡出，并显示升降级提示。没有分支的谱面不显示这些标识。

Branch Training 是新增的无音乐练习谱：前三个咚的命中率低于 50% 进入普通，50% 至不足 80% 进入玄人，80% 及以上进入达人；后半段连打不足 5 下进入普通、5–14 下进入玄人、15 下及以上进入达人。可以打开 AUTO PLAY 验证两次达人分支，或手动游玩尝试不同路线。

当前参考模拟器只实现 `p` 和 `r` 条件。本移植对 `s` 分数条件、`#LEVELHOLD`、BMSCROLL/HBSCROLL 明确报不支持，避免按错误规则游玩。

## 场景与代码

- `SceneSwitcher.unity`：入口。`SceneSwitcher.cs` 作为跨场景的唯一实例，提供 `Play(song, autoPlay)`、`Restart()`、`ReturnToMenu()`，使用异步场景加载并防止重复切换。
- `PlayScene.unity`：可在 Hierarchy 中编辑的 Canvas、音符轨道、判定圈、鼓面、魂槽、歌曲信息、舞者、暂停及结果面板。`PlayScene.cs` 连接输入、DSP 时钟、音乐和画面。
- `Runtime/Core/TjaParser.cs`：纯 C# TJA 读取，支持课程选择、音符 1–9、连打/气球、BPMCHANGE、MEASURE、DELAY、SCROLL（含复数）、GOGO、BARLINE，以及三路线分支与 SECTION。每条路线从分支起点恢复时刻、BPM、SCROLL、拍号等状态。
- `Runtime/Core/PlaySession.cs`：独立于 Unity 的判定、连击、分数、魂槽、自动演奏和分支选择。分支统计按事件时间处理，基础分和魂槽分母沿用原版的公共段＋达人路线音符数。大音符目前允许单侧击打；计分与魂槽仍是简化实现。
- `Runtime/Play/BranchLaneView.cs`：分支轨道底色、右侧谱面字样及升降级动画，使用打平的 Nijiiro 素材与原生坐标。
- `Runtime/Play/BalloonCounterView.cs`：7 号气球的剩余次数、Nijiiro 气泡、数字弹动、膨胀与破裂淡出，跟随游玩时钟及画布缩放。
- `Runtime/Play/SoulGaugeView.cs`：50 格魂槽、加高的黄色过关区、450 ms 新格淡入、满槽彩虹与魂火。Easy／Normal+Hard／Oni+Edit 过关阈值分别为 60%／70%／80%，与结算一致。每次彩虹帧过渡为 75 ms，8 帧循环 600 ms；失去满槽或过关状态时恢复对应样式。
- `Runtime/Core/NoteScroll.cs`：按 BPM、SCROLL 和判定点到轨道右边缘的距离计算流速。连打头尾始终使用头部速度整体移动，长度保持恒定。
- `Runtime/Core/SongDefinition.cs`：在 Inspector 中指定谱面 TextAsset、音乐 AudioClip、难度和音画偏移。谱面以 `.txt` 导入，内容仍是 TJA；WAVE 字段由显式 AudioClip 引用替代。
- `Editor/ProjectBuilder.cs`：通过 Editor API 创建初始场景和 sprite 切片。生成后不自动覆盖场景，后续直接编辑现有场景。

此阶段提取的是独立的单人游玩模块。联网/成绩上传、双人、段位、完整选曲界面、3D 咚角色、原皮肤全部 Lua 特效、逐帧回放、大音符双手判定及与原版完全一致的计分/魂槽尚未移植。

## 素材来源

当前只有一套 **Nijiiro** 皮肤。轨道、音符、魂槽、背景、19 帧舞者、字体和打击音效直接复制自相邻仓库的 `Skins/YataiDONNijiiro`；其缺少的分支字样等资源已从 Green 直接复制补齐，全部打平到 `Assets/OurTaiko/Art` 和 `Audio`。没有皮肤继承、回退加载或 Green 切换模式。素材来源逐项记录在 `Documentation/ImportedAssets.json`，原仓库未作修改。原项目自带的两份谱面和 TRIPLE HELIX 音频继续使用；保留原 `LICENSE`、`NOTICE` 及各资源的权利归属。

魂槽行为按当前原模拟器的 `gauge.cpp`、Nijiiro `skin_config.json` 和 `game/animation.json` 实现。源码没有“每 0.5 秒整条闪黄”的循环；其新增格子采用 450 ms 淡入，因此这里没有另加未经源码确认的周期闪黄。

## 验证

用 Unity Test Runner 运行 `OurTaiko.Tests`（EditMode）和 `OurTaiko.PlayModeTests`（PlayMode）。前者覆盖谱面、判定、流速和分支阈值／时序，后者覆盖入口 → 游玩 → 暂停/恢复 → 重开 → 返回、独立打开 PlayScene、音乐时间同步，以及三路线配色和字样、升降级动画、非分支谱面的隐藏行为和分支自动演奏结算。PlayMode 测试将实际场景渲染图输出到 `TestResults/`。

```sh
unity test . --mode EditMode --output TestResults/editmode.xml
unity test . --mode PlayMode --output TestResults/playmode.xml
unity run . -- -executeMethod OurTaiko.Editor.ProjectBuilder.BuildMac
```

Mac 开发构建输出到 `Builds/OurTaikoPlayerUnity.app`。以上构建与测试不代表移动设备的音频延迟已经校准。
