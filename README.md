# OurTaikoPlayerUnity

从相邻 `OurTaikoPlayer` 提取单人游玩页面的 Unity 2D 工程。使用 **Unity 6000.3.25f1 / Universal 2D / uGUI + TextMeshPro**。所有新增运行逻辑和 Editor 工具均为 **C#**，不包含 C++、Lua 或原项目的原生插件。

## 运行

1. 用 Unity Hub 打开本目录。
2. 打开 `Assets/Scenes/SceneSwitcher.unity`，点击 Unity 的 Play。
3. 选择 TRIPLE HELIX（含音乐）或 Input Calibration（原项目的无音乐校准谱），点击 PLAY / 按 Enter。

也可直接打开 `Assets/Scenes/PlayScene.unity` 运行，默认载入 TRIPLE HELIX。

| 按键 | 功能 |
| --- | --- |
| F / J | 左 / 右咚 |
| D / K | 左 / 右咔 |
| Space | 暂停 / 继续 |
| F1 | 重开 |
| Esc | 返回入口 |
| Tab / A（入口） | 切歌 / 自动演奏 |

页面下方有可点击的咚 / 咔按钮。场景采用 1280×720 固定设计区域，宽高比变化时居中留边，保持判定位置和轨道比例。

默认目标为 **120 FPS**。入口及直接打开游玩场景时都会关闭 VSync 并启用每帧渲染，避免画质档的垂直同步覆盖目标帧率。左上角 FPS 计数器每 0.5 秒显示实际帧率平均值，暂停时仍持续更新。它显示实测值，不是目标 120 的固定文字。

如果 Unity 内仍约为 60 FPS，检查 Game 视图的 VSync 选项、系统显示器刷新率及节能设置，并与独立播放器比较；Editor 自身的负载也可能降低帧率。120 FPS 是渲染目标，不会把 60 Hz 显示器变成 120 Hz。

## 场景与代码

- `SceneSwitcher.unity`：入口。`SceneSwitcher.cs` 作为跨场景的唯一实例，提供 `Play(song, autoPlay)`、`Restart()`、`ReturnToMenu()`，使用异步场景加载并防止重复切换。
- `PlayScene.unity`：可在 Hierarchy 中编辑的 Canvas、音符轨道、判定圈、鼓面、魂槽、歌曲信息、舞者、暂停及结果面板。`PlayScene.cs` 连接输入、DSP 时钟、音乐和画面。
- `Runtime/Core/TjaParser.cs`：纯 C# TJA 读取，支持课程选择、音符 1–9、连打/气球、BPMCHANGE、MEASURE、DELAY、SCROLL（含复数）、GOGO、BARLINE。不支持分歧谱和 BMSCROLL/HBSCROLL 时明确报错。
- `Runtime/Core/PlaySession.cs`：独立于 Unity 的判定、连击、分数、魂槽和自动演奏。判定窗口取自原项目。大音符目前允许单侧击打；计分采用简化真打基分与连打分，魂槽为简化单人模型。
- `Runtime/Core/NoteScroll.cs`：按 BPM、SCROLL 和判定点到轨道右边缘的距离计算流速。连打头尾始终使用头部速度整体移动，长度保持恒定。
- `Runtime/Core/SongDefinition.cs`：在 Inspector 中指定谱面 TextAsset、音乐 AudioClip、难度和音画偏移。谱面以 `.txt` 导入，内容仍是 TJA；WAVE 字段由显式 AudioClip 引用替代。
- `Editor/ProjectBuilder.cs`：通过 Editor API 创建初始场景和 sprite 切片。生成后不自动覆盖场景，后续直接编辑现有场景。

此阶段提取的是独立的单人游玩模块。联网/成绩上传、双人、段位、完整选曲界面、3D 咚角色、原皮肤全部 Lua 特效、逐帧回放、大音符双手判定及与原版完全一致的计分/魂槽尚未移植。

## 素材来源

复用了 `OurTaikoPlayer/Skins/PyTaikoGreen` 的部分轨道、音符、魂槽、背景、舞者帧、字体和打击音效，以及原项目自带的两份谱面和 TRIPLE HELIX 音频。具体文件见 `Documentation/ImportedAssets.json`。原仓库未作修改。保留原 `LICENSE`、`NOTICE`；皮肤、字体和音频沿用各自的权利归属。

## 验证

用 Unity Test Runner 运行 `OurTaiko.Tests`（EditMode）和 `OurTaiko.PlayModeTests`（PlayMode）。前者覆盖谱面与判定，后者覆盖入口 → 游玩 → 暂停/恢复 → 重开 → 返回、独立打开 PlayScene 以及音乐时间同步。PlayMode 测试将两张 1280×720 渲染图输出到 `TestResults/`。

```sh
unity test . --mode EditMode --output TestResults/editmode.xml
unity test . --mode PlayMode --output TestResults/playmode.xml
unity run . -- -executeMethod OurTaiko.Editor.ProjectBuilder.BuildMac
```

Mac 开发构建输出到 `Builds/OurTaikoPlayerUnity.app`。以上构建与测试不代表移动设备的音频延迟已经校准。
