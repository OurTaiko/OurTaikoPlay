# 标准交接摘要

更新日期：2026-10-01。本文记录当前有效结论；`Documentation/PortingNotes.md` 中的早期 Green／1280×720 和简化计分／魂槽记录仅是历史，不代表当前规格。

## 1. 核心项目目标

将相邻 OurTaikoPlayer 的单人游玩页面与玩法移植为纯 C# 的 Unity 2D 项目，通过全局 SceneSwitcher 控件、SongSelect、PlayScene 与 Result 提供采用 Nijiiro 皮肤、行为参照原模拟器的可运行单人流程。SceneSwitcher 不是场景；测试入口为 Test_DefaultScene，所有运行时场景切换从全局控件开始，并交由它完成。

## 2. 当前已知事实/约束条件

- 工作项目：`/Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity`；玩法参考源码：`/Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayer`；全局场景切换架构参照相邻 MajdataPlay 的 `Assets/Scripts/Global/SceneSwitcher.cs`。两个参考项目都只读，不修改。
- 技术栈：Unity **6000.3.25f1**、Universal 2D／URP **17.3.0**、uGUI、TextMeshPro、Input System。运行逻辑和 Editor 工具全部使用 **C#**，不引入 C++、Lua 或原模拟器的原生插件。
- 所有场景设计画布与默认窗口均为 **1920×1080**（SongSelect／Result 的 Viewport 带 RectMask2D）；宽高比变化时保持设计区域比例并居中留边。贴图对齐与局部偏移使用相对锚点或尺寸比例，不能写成固定屏幕像素补丁。
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

- 最新提交：**`b445ac3` — `feat(unity): add Nijiiro song select and result scenes`**，随后 `docs: update handoff for song select and result scenes` 更新本文（之前依次为 Shinuchi 计分／魂槽修复与 `a34ee00` 全局 SceneSwitcher）。交接时工作区干净。
- `Assets/OurTaiko/Generated/Nijiiro SDF.asset` 是动态 SDF 字体，Unity 会在打开项目、运行测试或保存时自动改写它（用户确认属于 Unity 自身行为，并非用户修改）。出现该 diff 时**不要提交**；收尾时用 `git restore "Assets/OurTaiko/Generated/Nijiiro SDF.asset"` 还原为已提交版本（Editor 打开时它可能被再次写入，必要时关闭 Editor 后再还原）。不要用旧的 TestResults 快照覆盖它。
- Unity Editor 可能仍由上一会话打开（项目已安装 Pipeline 包）；先用 `unity status` 确认连接再操作，修改 C# 后刷新并确认 `EditorUtility.scriptCompilationFailed` 为 false（编译错误会让 CLI 无法连接或静默失败，看 `~/Library/Logs/Unity/Editor.log` 的 `error CS`）。耗时较长的 Editor 方法会让 CLI 报 5 秒超时，但会在 Editor 中继续执行，需轮询结果。
- 选曲／结算的下一步候选（均未授权，需用户确认）：演奏选项／音色面板、文件夹与类别、成绩等级演出、曲目板飞入、难度决定标记弹出、皇冠光芒加算混合、支持字母扩展音符以游玩 TRIPLE HELIX Edit。
- 当前验证针对 Unity Editor。早期曾成功构建 macOS Development Player，但 `Builds/OurTaikoPlayerUnity.app` **没有随最近各次修复重新打包**，不能视作当前版本。移动端、真机音频延迟与独立播放器长期手动游玩尚未验收。

### 运行入口与代码结构

下列路径均相对于本项目根目录。

| 核心文件／目录 | 当前职责 |
| --- | --- |
| `Assets/Scenes/Test_DefaultScene.unity` | 测试入口场景；选择歌曲、自动演奏并调用全局控件进入游玩。 |
| `Assets/Scenes/SongSelect.unity`、`Runtime/Scenes/SongSelectScene.cs` | Nijiiro 纵向曲目板、展开／收起时间轴、试听与 BGM、难度面板、裏切换；扳手按钮暂作自动演奏开关。光标规则 `Core/DifficultyCursor.cs`，谱面信息 `Core/SongInfo.cs`。 |
| `Assets/Scenes/Result.unity`、`Runtime/Scenes/ResultScene.cs`、`ResultBackground.cs` | Nijiiro 结算背景、成绩板、魂槽填充、皇冠、评语、最高分条；时间轴 `Core/ResultSequence.cs`，数据 `Core/PlayResult.cs`，本地最佳成绩 `Core/ScoreStore.cs`。 |
| `Assets/OurTaiko/Animations`、`Runtime/Core/LumenClip.cs` | 原 `Scripts/anim/*.lua` 导出表的原样 `.txt` 副本与纯 C# 线性采样器（只读数据，不运行 Lua）。 |
| `Assets/Scenes/PlayScene.unity` | 已保存并可编辑的游玩 Canvas、轨道、判定圈、鼓面、魂槽、舞者、暂停与结果界面；可直接运行，默认 TRIPLE HELIX。 |
| `Assets/OurTaiko/Runtime/Scenes/SceneSwitcher.cs`、`Assets/OurTaiko/Resources/SceneSwitcher.prefab` | 加载首场景前自动创建的全局 uGUI 控件，跨场景保留。统一接管输入锁定、准备任务、关闭／打开过渡、异步加载、当前／上一场景及切换事件；设置 120 FPS。 |
| `Assets/OurTaiko/Runtime/Scenes/LaunchMenu.cs` | Test_DefaultScene 的测试选曲页面，通过全局 SceneSwitcher 开始游玩。 |
| `Assets/OurTaiko/Runtime/Core/TaikoChart.cs`、`TjaParser.cs` | 纯 C# 谱面模型与 TJA 解析；课程选择、音符 1–9、长音符、BPM／拍号／延迟／复数 SCROLL／GOGO／小节线与三路线分支。 |
| `Assets/OurTaiko/Runtime/Core/PlaySession.cs` | 独立于 Unity 的输入判定、连击、长音符次数、自动演奏与分支统计／时间线；将判定交给计分和魂槽模块，通过事件通知表现层。 |
| `Assets/OurTaiko/Runtime/Core/ChartStatistics.cs`、`ShinuchiScore.cs` | 公共段＋达人路线的固定统计、气球／连打预算、Shinuchi 基准分和累计分；独立于输入、位移与表现。 |
| `Assets/OurTaiko/Runtime/Core/SoulGaugeRules.cs`、`SoulGauge.cs` | 原难度／星级表、魂槽双精度点数、百分比、限幅及过关／满槽状态；与显示和结算共用。 |
| `Assets/OurTaiko/Runtime/Core/NoteScroll.cs` | 普通位移、对象加载时间与头尾同速的 `RollLength`。 |
| `Assets/OurTaiko/Runtime/Core/SongDefinition.cs` | 谱面 TextAsset、音乐 AudioClip、课程与音画偏移；TJA 以 `.txt` 导入，音频由显式引用绑定。 |
| `Assets/OurTaiko/Runtime/Play/PlayScene.cs` | 输入、DSP 歌曲时钟、`AudioSource.PlayScheduled`、暂停恢复、判定反馈、气球破裂音效、音符／身体／尾部渲染和结果显示。 |
| `Assets/OurTaiko/Runtime/Input/InputManager.cs` | 全局输入入口（参照 MajdataPlay `IO/InputManager`）：逻辑键 `InputKey` 与物理键绑定，监听 Input System 事件保留同帧按键先后顺序，由隐藏的 `InputManagerUpdater`（执行顺序 -32000）每帧在所有场景脚本前发布 `PressesThisFrame`／`GetKeyDown`。场景脚本不得直接读 `Keyboard.current`；触控鼓 `DrumPad` 启用时向 InputManager 注册，由同一次每帧更新对触摸与鼠标做命中检测，与键盘按下按时间顺序合并，当帧生效。 |
| `Assets/OurTaiko/Runtime/Play/BranchLaneView.cs` | 分支轨道色、右侧字样、升降级和过渡动画。 |
| `Assets/OurTaiko/Runtime/Play/SoulGaugeView.cs` | 50 格魂槽、过关黄色区、新格淡入、满槽彩虹与魂火。 |
| `Assets/OurTaiko/Runtime/Play/BalloonCounterView.cs` | 7 号气球剩余次数、数字弹动、膨胀、破裂与淡出。 |
| `Assets/OurTaiko/Runtime/Play/FpsCounter.cs`、`SpriteFlipbook.cs`、`DrumPad.cs` | 实测帧率、舞者帧动画和触控鼓。触控鼓**只放在 PlayScene**（原版为全局叠加层），复制原版：Nijiiro `global/overlay/touch_drum.png` 全画面 50% 不透明，位于暂停／结果面板之下；每次按下按原全局动画 66 以底边中心缩至 0.95 再回弹（各 70 ms、二次缓出，真实时间）。判定区照搬 `input.cpp::touch_quadrant_vkey`：上半屏为咔，下半屏中以设计区底边中心、半径为宽度 0.262／0.242 的椭圆内为咚、其余为咔，左右按中线分；落在 uGUI 按钮上的点交给按钮。迁移入口 `ProjectBuilder.ApplyTouchDrum()`。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.SongSelectResult.cs` | 菜单 OurTaiko/Create Song Select And Result Scenes：导入选曲／结算素材、生成切片与 `Generated/Nijiiro SDF Outline.mat`，仅在场景缺失时创建，并对已有场景只做定向升级。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.cs`、`ProjectBuilder.Nijiiro.cs`、`ProjectBuilder.Balloon.cs`、`ProjectBuilder.SceneSwitcher.cs` | 初始生成、Nijiiro 布局／魂槽／连打切片、气球资源配置与全局控件专项迁移；按需使用专项入口，避免全量重建现有场景。 |
| `Assets/OurTaiko/Art`、`Audio`、`Generated` | 打平的皮肤图片／音效、已生成 Sprite 切片与字体；运行时无需原仓库。 |
| `Assets/OurTaiko/Songs` | TRIPLE HELIX（含音乐）、Input Calibration（无音乐）、Branch Training（无音乐分支练习谱）。 |
| `README.md`、`Documentation/PortingNotes.md`、`Documentation/ImportedAssets.json` | 运行说明、详细行为依据与历次验证、素材来源记录。 |

操作：F／J 为咚，D／K 为咔（游玩时同一帧只判定最早的一次打击，其余同帧打击丢弃——太鼓输入互斥），Space 暂停／恢复，F1 重开，Esc 返回；入口 Tab 切歌、A 切换自动演奏、Enter 开始、S 进入选曲。选曲 D／K 移动、F／J 决定、A 自动演奏、Esc 回入口；结算 F／J 跳过／返回。游玩页也可用鼠标／触控敲击原版样式的触控鼓，选曲板、难度卡和结算画面也可点击。

**选曲／结算。** 流程：入口 → SongSelect → PlayScene → Result → 发起游玩的场景（`SceneSwitcher.ReturnScene`）。`PlayScene.Finish` 保存成绩（自动演奏不保存）后调用 `SceneSwitcher.ShowResult`，场景内结果面板只用于谱面加载失败。时间与布局均取自原 `song_select.cpp`／`navigator.cpp`／`player.cpp`／`result.cpp` 及 Nijiiro Lua，详见 `Documentation/PortingNotes.md`“选曲与结算场景”。TMP Mobile SDF 描边需 `OUTLINE_ON`，新场景文字使用 Outline 材质。PlayScene 现于遮罩关闭期间预载歌曲音频，避免首次 PlayScheduled 卡顿约 1 秒。

### 已完成玩法与表现的核心逻辑

**分支游玩。** 支持 `#BRANCHSTART p/r`、`#N/#E/#M`、`#BRANCHEND`、`#SECTION`。命中率以良=1、可=0.5、不可／漏音=0 计算百分比并截断；连打分支只统计 5／6 号，气球和彩球不计入，并保留原版对当前持续连打总数的补充规则。分支判定和 SECTION 重置按歌曲事件时间处理，不把未来判定计入过去的分支。每条路线从共同起点恢复 BPM、时刻、SCROLL 等解析状态；选线时机参考原版对象进入画面的时间。未选路线不显示、不判定、不计分。普通深色、玄人蓝色、达人紫色，右侧贴图随路线变化滑动和淡入淡出；非分支谱隐藏这些标识。暂停冻结动画，重开恢复初始状态。

**Shinuchi 计分。** 基准分从 100 万分扣除气球预算（每个最多 100 次，每次 100 分）和连打预算（源码 float 常量约 16.92008 次／秒，每次 100 分），除以公共段＋达人路线的普通音符数，向上取整到 10 分；没有普通音符时基准分为 1000000。良得基准分，可按原整数除法减半并取 10 分整数倍，不可／漏音不加分。大音符、GOGO、连击不加倍率，5／6／7／9 号每次有效击打均为 100，吹爆不另加 5000。最终分数不限制为 100 万。连打预算直接使用对应尾部减去头部时间，即 `(EndTime - Time)×1000` 毫秒；已按用户要求修复原源码使用下一个对象、可能被小节线截短的缺陷。不可恢复为 nextobj 时长。BALLOON 缺失次数默认 1。参考 `tja.cpp::calculate_base_score` 与 `player.cpp`。

**魂槽。** 数值上限 10000，双精度累积，按原 `gauge.h` 难度／星级表增减；普通音符分母固定为公共段＋达人路线。良为 `1000000/(max(1,普通音符数)×soul_percent)`，可／不可按表乘倍率，每次判定限幅并以 `1e-6` 点容差归整过关／满槽边界；百分比向下取整。长音符不改变魂槽或重启淡入。LEVEL 缺失／为 0 时使用 Oni ★10 数值及 80% 过关档，原表未使用行保持零值。Normal／Hard 部分表项在原源码中标为推测，本项目保留来源值。50 格；Easy／Normal+Hard／Oni+Edit 的过关阈值分别为 **60%／70%／80%**，显示和结算共用。过关区使用加高的黄色贴图完整填充；新增格子 450 ms 淡入。满槽彩虹先用 450 ms 淡入，然后每 75 ms 过渡下一帧，共 8 帧／600 ms 循环；魂火 50 ms／帧、400 ms 循环。掉出满槽／过关状态时恢复相应样式，再次满槽重新淡入。参考 `src/objects/game/gauge.cpp` 与 Nijiiro 的 `skin_config.json`、`Graphics/game/gauge/texture.json`、`Graphics/game/animation.json`。

**7 号气球。** 脸的相对对齐量是音符宽度的 **12/128**，通过锚点随音符尺寸与画布缩放，不能改回固定像素补偿。首次有效咚击打后显示 `总次数 - 已击打次数`，使用 Nijiiro 气泡及数字图集，支持跨位数布局；咔不计数。数字 50 ms 拉伸＋116 ms 回弹，身体按进度使用 0、2、3、4、5、6 帧；吹爆后第 7 帧、数字 0，166 ms 淡出；未吹爆到期立即隐藏。移动时显示 `notes/10` 尾部，击打时替换为膨胀素材；9 号彩球不混用这套显示。`Assets/OurTaiko/Audio/balloon_pop.ogg` 原样复制自 Nijiiro，约 0.414 秒、预加载；手动／自动达到要求次数的那次事件只播放一次，到期未爆不播放。参考 `player.cpp::draw_balloon/check_balloon`、`balloon_counter.cpp` 及 Nijiiro 气球配置。

**5／6 号连打。** `PlayScene` 分别持有小／大 `rollBodySprites` 和 `rollTailSprites`；身体只横向拉伸，尾部保持原宽高比，顺序为身体→尾部→头部。源图集左上坐标切片：小身体 `(0,1544,72,192)`、大身体 `(72,1544,72,192)`、小尾 `(0,2120,80,192)`、大尾 `(0,2312,120,192)`。身体长度为连打长度绝对值加音符高度的 **1/128**（192 设计高度时为 1.5），对应原版贴图宽度与 `drumroll_width_offset` 的合成，覆盖接缝。尾部起点位于连打结束位置；负滚动翻转身体和尾部，头部表情保持正向。音符图集使用 **Point** 采样，避免双线性采样混入邻近大连打帧形成杂边。生成资源位于 `Assets/OurTaiko/Generated/`：`RollBodySmall.asset`、`RollBodyBig.asset`、`RollTailSmall.asset`、`RollTailBig.asset`；配置入口 `ProjectBuilder.ApplyDrumrollSprites()`。参考 `player.cpp::draw_drumroll`、`src/libs/texture.cpp` 及 Nijiiro 音符 `texture.json`；头尾同速逻辑未改动。

### 验证结果与继续工作方法

- 最新完整验证（2026-10-01）：Unity Editor **EditMode 108/108、PlayMode 18/18 全部通过**。报告为 `TestResults/songselect-editmode.json`、`TestResults/songselect-playmode.json`；PlayMode 通过 `TestScoreStore` 使用临时成绩文件。`TestResults/` 不纳入版本控制，本机报告不保证随新克隆存在。
- EditMode 程序集：`OurTaiko.Tests`，测试位于 `Assets/OurTaiko/Tests/EditMode/`，覆盖解析、判定、分支阈值／时序、滚动与同速约束，以及 Shinuchi 预算／取整、独立逗号／空小节、魂槽难度星级／增减／过关边界。
- PlayMode 程序集：`OurTaiko.PlayModeTests`，`SceneFlowTests.cs` 覆盖场景流程、音乐同步、120 FPS 配置、各分支、魂槽、气球与连打，包含 1080p／720p 渲染。`GlobalSceneSwitcherTests.cs` 另覆盖跨场景预制体、等待准备任务、关闭／加载／打开顺序、timeScale=0、输入阻挡、重复请求、场景事件、手动揭示、泛型结果、失败／取消恢复及销毁取消。
- `ScoreGaugeFlowTests.cs` 覆盖实际游玩场景的 Shinuchi HUD、魂槽过关／满槽／失去过关、气球击打不重启淡入、结算状态及重开清零。
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

当前不是整个原模拟器的等价移植。选曲中的文件夹／类别、搜索与排序、演奏选项与音色面板、2P、曲目板飞入、难度决定标记弹出，结算中的成绩等级（粋／雅／極）演出、3D 咚与名牌、皇冠光芒加算混合尚未移植；TRIPLE HELIX 的 Edit 谱面含字母扩展音符，无法游玩。联网／成绩上传、双人、段位、3D 咚角色、全部皮肤特效、逐帧回放、大音符双手判定窗口尚未实现。大音符目前允许单侧击打；计分固定使用 Shinuchi，单人魂槽数值已按原源码还原，GEN3 计分及段位魂槽不在当前范围。自动连打仍为 **15 次／秒**，未移植原版随 BPM 变化的自动连打节奏。`s` 分数分支、`#LEVELHOLD`、BMSCROLL／HBSCROLL、字母扩展音符明确不支持；不要静默猜测其行为，也不要将这些范围自动当作用户已授权的新开发任务。
