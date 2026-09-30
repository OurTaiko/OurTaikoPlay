# 标准交接摘要

更新日期：2026-09-30。本文记录当前有效结论；`Documentation/PortingNotes.md` 中的早期 Green／1280×720 记录仅是历史，不代表当前规格。

## 1. 核心项目目标

将相邻 OurTaikoPlayer 的单人游玩页面与玩法移植为纯 C# 的 Unity 2D 项目，通过全局 SceneSwitcher 控件和 PlayScene 提供采用 Nijiiro 皮肤、行为参照原模拟器的可运行游玩模块。SceneSwitcher 不是场景；测试入口为 Test_DefaultScene，所有运行时场景切换从全局控件开始，并交由它完成。

## 2. 当前已知事实/约束条件

- 工作项目：`/Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity`；玩法参考源码：`/Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayer`；全局场景切换架构参照相邻 MajdataPlay 的 `Assets/Scripts/Global/SceneSwitcher.cs`。两个参考项目都只读，不修改。
- 技术栈：Unity **6000.3.25f1**、Universal 2D／URP **17.3.0**、uGUI、TextMeshPro、Input System。运行逻辑和 Editor 工具全部使用 **C#**，不引入 C++、Lua 或原模拟器的原生插件。
- 两个场景设计画布与默认窗口均为 **1920×1080**；宽高比变化时保持设计区域比例并居中留边。贴图对齐与局部偏移使用相对锚点或尺寸比例，不能写成固定屏幕像素补丁。
- 当前只做 **Nijiiro**。素材主要来自 `Skins/YataiDONNijiiro`；缺少的资源已从 Green 直接复制补齐并打平。不实现皮肤继承、运行时回退或 Green 皮肤切换。图片保持原文件，使用 Sprite 切片；来源见 `Documentation/ImportedAssets.json`，保留 LICENSE／NOTICE 与资源权利归属。
- 默认目标 **120 FPS**：关闭 VSync，`renderFrameInterval = 1`，`targetFrameRate = 120`。FPS 计数器每 **0.5 秒**显示实际平均帧率，暂停时仍更新；目标帧率不保证显示器实际达到 120 Hz。
- 音符位移公式：`(判定时间 - 当前时间，秒) × BPM / 240 × SCROLL × 判定点到右边缘的距离`。当前判定点设计 X=**618**，右边缘 X=**1920**，距离 **1302**；192×192 音符的加载边界半宽为 **96**。音画偏移只改变相位，不改变速度。
- **连打头尾必须同速**：统一采用头部 BPM／SCROLL，长度按头尾时间差乘头部速度计算并保持恒定。中途变速只影响后续音符，不得恢复为头尾各用独立速度。
- 动画时长、裁切、布局和判定行为先查原模拟器源码与 Nijiiro 配置。用户对魂槽“约每 0.5 秒闪黄”的描述是观察猜测；实际原代码是新增格子 **450 ms 淡入**，没有整条周期闪黄，不应另加猜测效果。
- 分支谱面通过轨道颜色及轨道右侧「普通譜面／玄人譜面／達人譜面」贴图识别，不使用独立分支文本框。
- Git 已初始化，当前分支为 `kirisamevanilla/unity-play-scene`；提交使用 **Conventional Commits**。保留用户已有改动，不把无关资源混入提交。新建分支默认使用 `kirisamevanilla/` 前缀。
- 场景、Sprite 资源、导入设置等持久化内容通过 Unity Editor API 修改并保存；避免手工改 Unity YAML／GUID。现有场景可直接编辑，不要随意执行生成初始场景的工具覆盖布局。
- 后续交流以中文为主；能根据原代码确定的常规实现直接完成并验证，无需重复询问已经确定的约束。

## 3. 最新进展与核心资产

### 当前完成状态与交接边界

- 最新已有功能提交：**`48a0fbc` — `fix(unity): match Nijiiro drumroll bodies and tails`**。其后，本次全局 SceneSwitcher 改造已完成并验证，保留在工作区尚未提交；Test_DefaultScene 为测试入口，SceneSwitcher 为全局预制体，所有切换交由它完成。此前的分支、魂槽、气球和连打功能仍通过回归。
- 本次开始时工作区干净；`Assets/OurTaiko/Generated/Nijiiro SDF.asset` 测试后与本次开始前完全一致。继续保留字体资源，不覆盖、回退或顺带提交无关字体改动。当前快照位于忽略目录 `TestResults/global-switcher-baseline/Nijiiro-SDF.asset`；历史快照 `TestResults/drumroll-baseline/Nijiiro-SDF.asset` 不代表最新内容。Unity 动态字体可能因测试／保存产生额外变化，操作前检查状态，恢复前确认用户没有继续修改。
- 当前验证针对 Unity Editor。早期曾成功构建 macOS Development Player，但 `Builds/OurTaikoPlayerUnity.app` **没有随最近各次修复重新打包**，不能视作当前版本。移动端、真机音频延迟与独立播放器长期手动游玩尚未验收。

### 运行入口与代码结构

下列路径均相对于本项目根目录。

| 核心文件／目录 | 当前职责 |
| --- | --- |
| `Assets/Scenes/Test_DefaultScene.unity` | 测试入口场景；选择歌曲、自动演奏并调用全局控件进入游玩。 |
| `Assets/Scenes/PlayScene.unity` | 已保存并可编辑的游玩 Canvas、轨道、判定圈、鼓面、魂槽、舞者、暂停与结果界面；可直接运行，默认 TRIPLE HELIX。 |
| `Assets/OurTaiko/Runtime/Scenes/SceneSwitcher.cs`、`Assets/OurTaiko/Resources/SceneSwitcher.prefab` | 加载首场景前自动创建的全局 uGUI 控件，跨场景保留。统一接管输入锁定、准备任务、关闭／打开过渡、异步加载、当前／上一场景及切换事件；设置 120 FPS。 |
| `Assets/OurTaiko/Runtime/Scenes/LaunchMenu.cs` | Test_DefaultScene 的测试选曲页面，通过全局 SceneSwitcher 开始游玩。 |
| `Assets/OurTaiko/Runtime/Core/TaikoChart.cs`、`TjaParser.cs` | 纯 C# 谱面模型与 TJA 解析；课程选择、音符 1–9、长音符、BPM／拍号／延迟／复数 SCROLL／GOGO／小节线与三路线分支。 |
| `Assets/OurTaiko/Runtime/Core/PlaySession.cs` | 独立于 Unity 的输入判定、连击、分数、魂槽、长音符次数、自动演奏与分支统计／时间线；通过事件通知表现层。 |
| `Assets/OurTaiko/Runtime/Core/NoteScroll.cs` | 普通位移、对象加载时间与头尾同速的 `RollLength`。 |
| `Assets/OurTaiko/Runtime/Core/SongDefinition.cs` | 谱面 TextAsset、音乐 AudioClip、课程与音画偏移；TJA 以 `.txt` 导入，音频由显式引用绑定。 |
| `Assets/OurTaiko/Runtime/Play/PlayScene.cs` | 输入、DSP 歌曲时钟、`AudioSource.PlayScheduled`、暂停恢复、判定反馈、气球破裂音效、音符／身体／尾部渲染和结果显示。 |
| `Assets/OurTaiko/Runtime/Play/BranchLaneView.cs` | 分支轨道色、右侧字样、升降级和过渡动画。 |
| `Assets/OurTaiko/Runtime/Play/SoulGaugeView.cs` | 50 格魂槽、过关黄色区、新格淡入、满槽彩虹与魂火。 |
| `Assets/OurTaiko/Runtime/Play/BalloonCounterView.cs` | 7 号气球剩余次数、数字弹动、膨胀、破裂与淡出。 |
| `Assets/OurTaiko/Runtime/Play/FpsCounter.cs`、`SpriteFlipbook.cs`、`DrumPad.cs` | 实测帧率、舞者帧动画和可点击打击按钮。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.cs`、`ProjectBuilder.Nijiiro.cs`、`ProjectBuilder.Balloon.cs`、`ProjectBuilder.SceneSwitcher.cs` | 初始生成、Nijiiro 布局／魂槽／连打切片、气球资源配置与全局控件专项迁移；按需使用专项入口，避免全量重建现有场景。 |
| `Assets/OurTaiko/Art`、`Audio`、`Generated` | 打平的皮肤图片／音效、已生成 Sprite 切片与字体；运行时无需原仓库。 |
| `Assets/OurTaiko/Songs` | TRIPLE HELIX（含音乐）、Input Calibration（无音乐）、Branch Training（无音乐分支练习谱）。 |
| `README.md`、`Documentation/PortingNotes.md`、`Documentation/ImportedAssets.json` | 运行说明、详细行为依据与历次验证、素材来源记录。 |

操作：F／J 为咚，D／K 为咔，Space 暂停／恢复，F1 重开，Esc 返回；入口 Tab 切歌、A 切换自动演奏、Enter 开始。游玩页也有鼠标／触控打击按钮。

### 已完成玩法与表现的核心逻辑

**分支游玩。** 支持 `#BRANCHSTART p/r`、`#N/#E/#M`、`#BRANCHEND`、`#SECTION`。命中率以良=1、可=0.5、不可／漏音=0 计算百分比并截断；连打分支只统计 5／6 号，气球和彩球不计入，并保留原版对当前持续连打总数的补充规则。分支判定和 SECTION 重置按歌曲事件时间处理，不把未来判定计入过去的分支。每条路线从共同起点恢复 BPM、时刻、SCROLL 等解析状态；选线时机参考原版对象进入画面的时间。未选路线不显示、不判定、不计分。普通深色、玄人蓝色、达人紫色，右侧贴图随路线变化滑动和淡入淡出；非分支谱隐藏这些标识。暂停冻结动画，重开恢复初始状态。

**魂槽。** 50 格；Easy／Normal+Hard／Oni+Edit 的过关阈值分别为 **60%／70%／80%**，显示和结算共用。过关区使用加高的黄色贴图完整填充；新增格子 450 ms 淡入。满槽彩虹先用 450 ms 淡入，然后每 75 ms 过渡下一帧，共 8 帧／600 ms 循环；魂火 50 ms／帧、400 ms 循环。掉出满槽／过关状态时恢复相应样式，再次满槽重新淡入。参考 `src/objects/game/gauge.cpp` 与 Nijiiro 的 `skin_config.json`、`Graphics/game/gauge/texture.json`、`Graphics/game/animation.json`。

**7 号气球。** 脸的相对对齐量是音符宽度的 **12/128**，通过锚点随音符尺寸与画布缩放，不能改回固定像素补偿。首次有效咚击打后显示 `总次数 - 已击打次数`，使用 Nijiiro 气泡及数字图集，支持跨位数布局；咔不计数。数字 50 ms 拉伸＋116 ms 回弹，身体按进度使用 0、2、3、4、5、6 帧；吹爆后第 7 帧、数字 0，166 ms 淡出；未吹爆到期立即隐藏。移动时显示 `notes/10` 尾部，击打时替换为膨胀素材；9 号彩球不混用这套显示。`Assets/OurTaiko/Audio/balloon_pop.ogg` 原样复制自 Nijiiro，约 0.414 秒、预加载；手动／自动达到要求次数的那次事件只播放一次，到期未爆不播放。参考 `player.cpp::draw_balloon/check_balloon`、`balloon_counter.cpp` 及 Nijiiro 气球配置。

**5／6 号连打（最新修复）。** `PlayScene` 分别持有小／大 `rollBodySprites` 和 `rollTailSprites`；身体只横向拉伸，尾部保持原宽高比，顺序为身体→尾部→头部。源图集左上坐标切片：小身体 `(0,1544,72,192)`、大身体 `(72,1544,72,192)`、小尾 `(0,2120,80,192)`、大尾 `(0,2312,120,192)`。身体长度为连打长度绝对值加音符高度的 **1/128**（192 设计高度时为 1.5），对应原版贴图宽度与 `drumroll_width_offset` 的合成，覆盖接缝。尾部起点位于连打结束位置；负滚动翻转身体和尾部，头部表情保持正向。音符图集使用 **Point** 采样，避免双线性采样混入邻近大连打帧形成杂边。生成资源位于 `Assets/OurTaiko/Generated/`：`RollBodySmall.asset`、`RollBodyBig.asset`、`RollTailSmall.asset`、`RollTailBig.asset`；配置入口 `ProjectBuilder.ApplyDrumrollSprites()`。参考 `player.cpp::draw_drumroll`、`src/libs/texture.cpp` 及 Nijiiro 音符 `texture.json`；头尾同速逻辑未改动。

### 验证结果与继续工作方法

- 最新完整验证：Unity Editor **EditMode 57/57、PlayMode 15/15 全部通过**。报告为 `TestResults/global-switcher-editmode.json`、`TestResults/global-switcher-playmode.json`；`TestResults/` 不纳入版本控制，本机报告不保证随新克隆存在。
- EditMode 程序集：`OurTaiko.Tests`，测试位于 `Assets/OurTaiko/Tests/EditMode/`，覆盖解析、判定、分支阈值／时序、滚动与同速约束。
- PlayMode 程序集：`OurTaiko.PlayModeTests`，`SceneFlowTests.cs` 覆盖场景流程、音乐同步、120 FPS 配置、各分支、魂槽、气球与连打，包含 1080p／720p 渲染。`GlobalSceneSwitcherTests.cs` 另覆盖跨场景预制体、等待准备任务、关闭／加载／打开顺序、timeScale=0、输入阻挡、重复请求、场景事件、手动揭示、泛型结果、失败／取消恢复及销毁取消。
- 已提交截图在 `Documentation/`：`DrumrollSmall.png`、`DrumrollBig.png`、`BalloonAtJudge.png`、`BalloonCounter.png`、`GaugeClear.png`、`GaugeRainbowA.png`、`GaugeRainbowB.png` 及分支截图。
- 优先通过 Unity Test Runner 验证实际场景。控制 Editor 前读取可用的 `unity:unity-cli` 技能；本机 CLI 为 `/Users/kirisamevanilla/.unity/bin/unity`，项目安装了 `com.unity.pipeline` **0.8.0-exp.1**。CLI 成功响应还需检查嵌套命令结果，不能只看进程退出码。
- 以下是已验证的 Editor 命令形式（公共参数在具体命令名前）。先确认正确项目的 Editor 已启动并连接；修改 C# 后刷新并等待编译结束。两组测试依次运行，异步启动后用 `test_status` 确认最终结果，保存报告后再启动下一组：

```sh
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json eval 'UnityEditor.AssetDatabase.Refresh();'
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json run_tests --mode editor --filter OurTaiko.Tests --filter_type assembly --async_tests true
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json test_status
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json run_tests --mode playmode --filter OurTaiko.PlayModeTests --filter_type assembly --async_tests true
```

测试状态原件为 `Temp/pipeline_test_status.json`，每次运行会覆盖。如需新 macOS 播放器，使用 `OurTaiko.Editor.ProjectBuilder.BuildMac()`，另行确认构建成功；不要把旧构建作为最新验证证据。

### 明确尚未实现的范围

当前不是整个原模拟器的等价移植。联网／成绩上传、双人、段位、完整选曲、3D 咚角色、全部皮肤特效、逐帧回放、大音符双手判定窗口，以及完全一致的计分／魂槽数值尚未实现。大音符目前允许单侧击打，计分和魂槽增减仍是简化公式；分母按公共段＋达人路线普通音符数计算。自动连打仍为 **15 次／秒**，未移植原版随 BPM 变化的自动连打节奏。`s` 分数分支、`#LEVELHOLD`、BMSCROLL／HBSCROLL、字母扩展音符明确不支持；不要静默猜测其行为，也不要将这些范围自动当作用户已授权的新开发任务。
