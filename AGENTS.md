# 标准交接摘要

更新日期：2026-10-02。本文记录当前有效结论；`Documentation/PortingNotes.md` 中的早期 Green／1280×720 和简化计分／魂槽记录仅是历史，不代表当前规格。

## 1. 核心项目目标

将相邻 OurTaikoPlayer 的单人游玩页面与玩法移植为纯 C# 的 Unity 2D 项目，通过全局 SceneSwitcher 控件、Entry、SongSelect、SongLoadingScene、SinglePlayScene 与 Result 提供采用 Nijiiro 皮肤、行为参照原模拟器的可运行单人流程。SceneSwitcher 不是场景；入口为 Entry（Build Settings 首个场景、`SceneSwitcher.MenuScene`；2026-10-02 删除 Test_DefaultScene 测试入口及 LaunchMenu，随后移植 Entry），所有运行时场景切换从全局控件开始，并交由它完成。

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
- **OurTaikoPlayer 也是模拟器，不是原版游戏。** 它是行为参考，但其中有错误；用户在移植中修正这些错误。下列是**有意的偏离**，不要按 OurTaikoPlayer 源码「还原」，也不要当作待办：
  - **输入互斥（每帧一击）**：游玩时每帧只判定最早的一次咚／咔打击（键盘、触控、鼠标按发生时间合并排序），同帧其余打击直接丢弃，不判定、不播音效、不亮鼓面。OurTaikoPlayer 的 `player.cpp::handle_input` 每帧按固定顺序（左咚、右咚、左咔、右咔）逐个处理全部打击，这是被修正的行为。后果是已知且接受的：连打中同帧双手只计 1 次；同帧先咔后咚时咔占用该帧；同帧双手打大音符不会误吃下一个音符。实现在 `PlayScene.HitFirstDrumPress`，由 `DrumInputMutexTests.cs` 覆盖。
  - **大音符不需要双手同时击打**：大咚／大咔单侧击打即为完整判定，与小音符同样计分。不实现双手判定窗口或双击加分，不要把它列为未移植功能。
  - **演奏オプション**（用户决定）：ドロン只隐藏音符，小节线保留；ランダム按每个咚／咔音符独立概率换色（きまぐれ 30%、でたらめ 50%），不用原 `modifier_random` 的 (对象数/5)×档位 抽取；演奏スキップ灰显不可改（单人没有 2P 鼓）；轨道徽章网格按整数行，不复制原 `slot/3.0` 浮点下移。详见 `Documentation/PortingNotes.md`「演奏オプション」。
  - **计时器不倒数**（用户决定：模拟器不限制玩家时间）：原版 Entry 60 秒、选曲列表 100 秒、难度选择 60 秒倒数，归零替玩家决定（难度选择停在もどる／选项时还会把无效难度传给游戏）；本项目三处都只显示 60／100／60 作占位，不倒数、无 blip 与语音、不自动决定。计时器音效未导入，倒数与 10 秒内红色弹动的代码已删除（`ArcadeTimerView` 只显示固定数字）；如需恢复，参照原版 `Scripts/global/timer.lua` 与 `PortingNotes.md`「Entry 场景」中的记录。
  - **名牌与自动演奏**（用户决定）：原版自动演奏时在名牌位置画 `lane/auto_icon` 取代名牌；本项目名牌始终显示，自动演奏只在演奏オプション徽章区第一位加入选曲的 `song_select/modifier/mod_auto`，没有其他视觉差别。名牌彩虹称号带按 6 帧／50 ms／300 ms 循环；原版从未 `start()` 该动画（停在第 0 帧），属被修正的缺陷。
- Git 已初始化，当前直接在 `main` 上提交（线性历史，无合并提交）；提交使用 **Conventional Commits**。保留用户已有改动，不把无关资源混入提交。新建分支默认使用 `kirisamevanilla/` 前缀。
- 场景、Sprite 资源、导入设置等持久化内容通过 Unity Editor API 修改并保存；避免手工改 Unity YAML／GUID。现有场景可直接编辑，不要随意执行生成初始场景的工具覆盖布局。
- 后续交流以中文为主；能根据原代码确定的常规实现直接完成并验证，无需重复询问已经确定的约束。

## 3. 最新进展与核心资产

### 当前完成状态与交接边界

- 最新提交：`31c6d06` — `test: fix the flaky loading-curtain timing and drop the yellow LOADING text`（2026-10-02，见下文「最新验证」）。其前为 `1c0e2d4`（Entry 测试移入 Finished）、`1bc4c8b`（删除不再引用的计时器红区图片 `bg_red`／`counter_white`／`highlight`）、`75b09fb` — `refactor(overlays): delete the unused timer countdown`（删除 `ArcadeTimer` 倒数代码与白色数字切片）。其前为 `8d90d76` — `feat(entry): make the Entry timer a placeholder that never counts down`（2026-10-02）。其前为 `6a2bb66` — `feat(scenes): show the global arcade overlays on SongSelect and Result`（2026-10-02，见 `Documentation/PortingNotes.md`「SongSelect 与 Result 的全局元素」）。其前为 `df58cb5` — `feat(scenes): port the Nijiiro Entry scene with global arcade overlays`（2026-10-02，见下文「Entry」）。其前为 `abd46ba`（测试分为进行中／已完成程序集）与 `13f2b80` — `refactor(scenes): remove Test_DefaultScene and start from SongSelect`（2026-10-02，删除测试入口，SongSelect 为首个场景，见 `Documentation/PortingNotes.md`「删除 Test_DefaultScene」）。其前为 `a067014` — `feat(ui): add Nijiiro nameplate, PlayerInfoController and score counter`（2026-10-02，名牌、全局 `PlayerInfoController` 与游玩分数计数器，见下文「名牌与分数计数器」），随后的 docs 提交补全本文。其前为 `222f8f2` — `feat(play): fly hit notes to the soul badge with gauge hit effect`（见下文「音符飞向魂槽与 GaugeHitEffect」），随后的 docs 提交补全本文。再前为 `6a78a04` — `feat(scenes): add Nijiiro song loading curtain and SongLoadingScene`（见下文「选曲加载幕布」）。其前为 `feat(play): port note moji lane`（音符文字，见下文「音符文字（moji）」）。其前为音符显隐／层级修复 `dfd5e56`、`5faadfe`、`d6cd16b`、`0e2cbe5`（见下文「音符显隐与层级」）。更早为 **`b445ac3` — `feat(unity): add Nijiiro song select and result scenes`**，随后 `docs: update handoff for song select and result scenes` 更新本文（之前依次为 Shinuchi 计分／魂槽修复与 `a34ee00` 全局 SceneSwitcher）。交接时工作区干净。
- `Assets/OurTaiko/Generated/Nijiiro SDF.asset` 是动态 SDF 字体，Unity 会在打开项目、运行测试或保存时自动改写它（用户确认属于 Unity 自身行为，并非用户修改）。出现该 diff 时**不要提交**；只在 **Editor 关闭后**才用 `git restore "Assets/OurTaiko/Generated/Nijiiro SDF.asset"` 还原；Editor 打开时**绝不还原**：字体开启多图集，运行中新增的字形可能已写到第 2 页图集，文件被还原成只有 1 页后，场景里已有的 TMP 文字仍引用第 2 页，`TMP_MaterialManager.GetFallbackMaterial` 抛 `IndexOutOfRangeException`（2026-10-02 Entry 移植时发生过）。Editor 打开期间只需在提交时排除该文件。不要用旧的 TestResults 快照覆盖它。
- Unity Editor 可能仍由上一会话打开（项目已安装 Pipeline 包）；先用 `unity status` 确认连接再操作，修改 C# 后刷新并确认 `EditorUtility.scriptCompilationFailed` 为 false（编译错误会让 CLI 无法连接或静默失败，看 `~/Library/Logs/Unity/Editor.log` 的 `error CS`）。耗时较长的 Editor 方法会让 CLI 报 5 秒超时，但会在 Editor 中继续执行，需轮询结果。Editor 未运行时用 `unity open <项目路径>` 启动并轮询 `unity status` 到 ready；关闭用 `unity projects close <项目路径>`（不保存，先确认没有未保存场景）。zsh 不会对未加引号的 `$var` 分词，多参数 CLI 调用写成 bash 脚本。
- 若 Editor 报「assets located in immutable packages were unexpectedly altered」：这是 `Library/PackageCache` 中包文件（2026-10-02 为 `com.unity.render-pipelines.core` 的 LookDev 图标 .meta）被改写，与项目文件无关。修复：关闭 Editor，删除该包的 `Library/PackageCache/<包名>@<hash>` 目录，重开后 Package Manager **不会自动**补回（会出现大量 URP／Shader Graph 的 `error CS`），需在 Editor 中执行 `UnityEditor.PackageManager.Client.Resolve()` 重新解析，等待目录恢复并重新编译。
- 选曲／结算的下一步候选（均未授权，需用户确认）：文件夹与类别、成绩等级演出、曲目板飞入、难度决定标记弹出、皇冠光芒加算混合、支持字母扩展音符以游玩 TRIPLE HELIX Edit。
- **SongSelect 与 Result 的画面在运行时由代码构建**：场景文件只保存相机、EventSystem、Canvas 下空的 1920×1080 `Stage`、FPS 面板和持有素材引用的控制组件，`SongSelectScene`／`ResultScene` 在 `Awake` 中 `Build()` 出全部界面（单独打开 Result 时显示样例失败成绩）。因此 Editor 未运行时这两个场景看起来是空的，这是现状而非故障。SongLoadingScene 同样为空，画面全部是 SceneSwitcher 预制体里的幕布。SinglePlayScene 的轨道、魂槽、鼓面与面板则保存在场景中。用户已问过此事；改为编辑期预览或把层级烘焙进场景均未授权。
- 当前验证针对 Unity Editor。早期曾成功构建 macOS Development Player，但 `Builds/OurTaikoPlayerUnity.app` **没有随最近各次修复重新打包**，不能视作当前版本。移动端、真机音频延迟与独立播放器长期手动游玩尚未验收。

### 运行入口与代码结构

下列路径均相对于本项目根目录。

| 核心文件／目录 | 当前职责 |
| --- | --- |
| `Assets/Scenes/Entry.unity`、`Runtime/Scenes/EntryScene.cs`、`EntryViews.cs`、`Runtime/Core/EntryFlow.cs` | Nijiiro Entry（街机投币模式）：街景背景、「１人プレイ／２人プレイ 太鼓をたたいてスタート！」两行、1P 加入后名牌与操作指引淡入、演奏ゲーム 模式板（`mode_board` 时间轴）、决定后进入 SongSelect。画面在 `Awake` 中由代码构建。迁移入口 `ProjectBuilder.CreateEntryScene()`（菜单 OurTaiko/Create Entry Scene）。 |
| `Runtime/Scenes/GlobalOverlays.cs` | 全局街机界面元素：计时器（`ArcadeTimerView`，占位，只显示固定数字）、左上操作指引（`global/indicator` 决定循环）、フリープレイ／QR 芯片／2P 邀请云（`coin_overlay`）、段位道場／1プレイ4曲／IC Card 状态芯片（`entry_overlay`）。Entry 全部显示；SongSelect 显示计时器占位（列表 100、难度选择 60，不倒数）、QR 芯片与 2P 邀请云（已玩曲数 < 2）；Result 只显示フリープレイ（在 FadeIn 之上）。迁移入口 `ProjectBuilder.ApplyGlobalOverlays()`。 |
| `Assets/Scenes/SongSelect.unity`、`Runtime/Scenes/SongSelectScene.cs` | Nijiiro 纵向曲目板、展开／收起时间轴、试听与 BGM、难度面板、裏切换；扳手按钮打开演奏オプション。光标规则 `Core/DifficultyCursor.cs`，谱面信息 `Core/SongInfo.cs`。 |
| `Runtime/Core/PlayOptions.cs`、`OptionMenu.cs`、`Runtime/Scenes/OptionPanel.cs` | 演奏オプション：设置与 `options.json` 持久化、速度档位、`ChartModifiers`（あべこべ／ランダム／はやさ／ドロン）；7 行面板逻辑与滑入滑出；Nijiiro 面板绘制和触控区。游玩侧 `Play/HitSoundLibrary.cs`（`Generated/HitSounds.asset`，21 套音色）与 `Play/ModifierBadgeView.cs`（轨道徽章）。迁移入口 `ProjectBuilder.ApplyPlayOptions()`。 |
| `Assets/Scenes/Result.unity`、`Runtime/Scenes/ResultScene.cs`、`ResultBackground.cs` | Nijiiro 结算背景、成绩板、魂槽填充、皇冠、评语、最高分条；时间轴 `Core/ResultSequence.cs`，数据 `Core/PlayResult.cs`，本地最佳成绩 `Core/ScoreStore.cs`。 |
| `Assets/OurTaiko/Animations`、`Runtime/Core/LumenClip.cs` | 原 `Scripts/anim/*.lua` 导出表的原样 `.txt` 副本与纯 C# 线性采样器（只读数据，不运行 Lua）。 |
| `Assets/Scenes/SongLoadingScene.unity`、`Runtime/Scenes/SongLoadingScene.cs`、`SongTransition.cs` | 选曲加载：`SceneSwitcher.Play()` 以彩虹幕布（SceneSwitcher 预制体内的 `SongTransition`，`TransitionStyle.Curtain`）关闭并进入此场景；场景在停住的幕布下解析 TJA（含演奏オプション）、载入歌曲音频，至少 2 秒后切到 SinglePlayScene 并在其上打开幕布。迁移入口 `ProjectBuilder.ApplySongLoadingCurtain()`。 |
| `Assets/Scenes/SinglePlayScene.unity` | 单人游玩场景（`SceneSwitcher.GameScene`；2026-10-01 由 PlayScene 改名，GUID 不变，控制组件类仍为 `PlayScene`）。已保存并可编辑的游玩 Canvas、轨道、判定圈、鼓面、魂槽、舞者、暂停与结果界面；可直接运行，默认 TRIPLE HELIX。 |
| `Assets/OurTaiko/Runtime/Scenes/SceneSwitcher.cs`、`Assets/OurTaiko/Resources/SceneSwitcher.prefab` | 加载首场景前自动创建的全局 uGUI 控件，跨场景保留。统一接管输入锁定、准备任务、关闭／打开过渡、异步加载、当前／上一场景及切换事件；设置 120 FPS。两种过渡样式 `TransitionStyle.Fade`（`Transition` 深色遮罩，MajdataPlay OutQuint）与 `Curtain`（`SongTransition` 彩虹幕布）；`Play()` 用幕布进入 SongLoadingScene，其余切换默认淡入淡出。另持有一次性的预解析谱面（`SetPreparedChart`／`TakePreparedChart`）。 |
| `Assets/OurTaiko/Runtime/Core/TaikoChart.cs`、`TjaParser.cs` | 纯 C# 谱面模型与 TJA 解析；课程选择、音符 1–9、长音符、BPM／拍号／延迟／复数 SCROLL／GOGO／小节线与三路线分支。 |
| `Assets/OurTaiko/Runtime/Core/PlaySession.cs` | 独立于 Unity 的输入判定、连击、长音符次数、自动演奏与分支统计／时间线；将判定交给计分和魂槽模块，通过事件通知表现层。 |
| `Assets/OurTaiko/Runtime/Core/ChartStatistics.cs`、`ShinuchiScore.cs` | 公共段＋达人路线的固定统计、气球／连打预算、Shinuchi 基准分和累计分；独立于输入、位移与表现。 |
| `Assets/OurTaiko/Runtime/Core/SoulGaugeRules.cs`、`SoulGauge.cs` | 原难度／星级表、魂槽双精度点数、百分比、限幅及过关／满槽状态；与显示和结算共用。 |
| `Assets/OurTaiko/Runtime/Core/NoteMoji.cs` | 音符文字帧分配（原 `modifier_moji`），按 `TaikoChart.NoteLists` 逐条处理。 |
| `Assets/OurTaiko/Runtime/Core/NoteScroll.cs` | 普通位移、对象加载时间与头尾同速的 `RollLength`。 |
| `Assets/OurTaiko/Runtime/Core/SongDefinition.cs` | 谱面 TextAsset、音乐 AudioClip、课程与音画偏移；TJA 以 `.txt` 导入，音频由显式引用绑定。 |
| `Assets/OurTaiko/Runtime/Play/PlayScene.cs` | 输入、DSP 歌曲时钟、`AudioSource.PlayScheduled`、暂停恢复、判定反馈、气球破裂音效、音符／身体／尾部渲染和结果显示。 |
| `Assets/OurTaiko/Runtime/Input/InputManager.cs` | 全局输入入口（参照 MajdataPlay `IO/InputManager`）：逻辑键 `InputKey` 与物理键绑定，监听 Input System 事件保留同帧按键先后顺序，由隐藏的 `InputManagerUpdater`（执行顺序 -32000）每帧在所有场景脚本前发布 `PressesThisFrame`／`GetKeyDown`。场景脚本不得直接读 `Keyboard.current`；触控鼓 `DrumPad` 启用时向 InputManager 注册，由同一次每帧更新对触摸与鼠标做命中检测，与键盘按下按时间顺序合并，当帧生效。 |
| `Assets/OurTaiko/Runtime/Play/BranchLaneView.cs` | 分支轨道色、右侧字样、升降级和过渡动画。 |
| `Assets/OurTaiko/Runtime/Play/SoulGaugeView.cs` | 50 格魂槽、过关黄色区、新格淡入、满槽彩虹与魂火。 |
| `Assets/OurTaiko/Runtime/Core/NoteArcPath.cs`、`GaugeHitEffectTiming.cs`、`Runtime/Play/NoteArcView.cs`、`GaugeHitEffectView.cs` | 命中音符飞向魂徽章：Nijiiro `note_arc_pivot` 圆弧、30 帧；到达后在徽章播放 GaugeHitEffect（光圈换帧、0.8→1.5 放大、黄→橙→红、383 ms 淡出，音符同步淡出）。`NoteArcs`、`GaugeHitEffect` 层依次在 `SoulGauge` 之后。迁移入口 `ProjectBuilder.ApplyNoteArcs()`。 |
| `Runtime/Core/PlayerInfo.cs`、`NameplateLayout.cs`、`Runtime/Scenes/PlayerInfoController.cs`、`NameplateView.cs`、`Generated/Nameplate.prefab` | 玩家名牌：数据与规则（coin／称号／段位、名字框、彩虹帧）、全局持有者（读 `player.json`，`Changed` 事件更新名牌）、Nijiiro 名牌预制体。SinglePlayScene 保存实例；SongSelect／Result 由 `nameplatePrefab` 运行时实例化。迁移入口 `ProjectBuilder.ApplyNameplate()`。 |
| `Runtime/Play/ScoreCounterView.cs` | 游玩分数计数器（`lane_score_cover`＋`score_number` 数字、TextStretch 弹动），布局在 `Core/NameplateLayout.cs` 的 `ScoreCounterLayout`，弹动公式 `TextStretch` 与气球数字共用。 |
| `Assets/OurTaiko/Runtime/Play/BalloonCounterView.cs` | 7 号气球剩余次数、数字弹动、膨胀、破裂与淡出。 |
| `Assets/OurTaiko/Runtime/Play/FpsCounter.cs`、`SpriteFlipbook.cs`、`DrumPad.cs` | 实测帧率、舞者帧动画和触控鼓。触控鼓**只放在 SinglePlayScene**（原版为全局叠加层），复制原版：Nijiiro `global/overlay/touch_drum.png` 全画面 50% 不透明，位于暂停／结果面板之下；每次按下按原全局动画 66 以底边中心缩至 0.95 再回弹（各 70 ms、二次缓出，真实时间）。判定区照搬 `input.cpp::touch_quadrant_vkey`：上半屏为咔，下半屏中以设计区底边中心、半径为宽度 0.262／0.242 的椭圆内为咚、其余为咔，左右按中线分；落在 uGUI 按钮上的点交给按钮。迁移入口 `ProjectBuilder.ApplyTouchDrum()`。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.SongSelectResult.cs` | 菜单 OurTaiko/Create Song Select And Result Scenes：导入选曲／结算素材、生成切片与 `Generated/Nijiiro SDF Outline.mat`，仅在场景缺失时创建，并对已有场景只做定向升级。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.cs`、`ProjectBuilder.Nijiiro.cs`、`ProjectBuilder.Balloon.cs`、`ProjectBuilder.SceneSwitcher.cs`、`ProjectBuilder.SongLoading.cs` | 初始生成、Nijiiro 布局／魂槽／连打切片、气球资源配置、全局控件专项迁移，以及选曲加载幕布（重建预制体内的 `SongTransition` 子物体，SongLoadingScene 仅缺失时创建）；按需使用专项入口，避免全量重建现有场景。 |
| `Assets/OurTaiko/Art`、`Audio`、`Generated` | 打平的皮肤图片／音效、已生成 Sprite 切片与字体；运行时无需原仓库。 |
| `Assets/OurTaiko/Songs` | TRIPLE HELIX（含音乐）、Input Calibration（无音乐）、Branch Training（无音乐分支练习谱）。 |
| `README.md`、`Documentation/PortingNotes.md`、`Documentation/ImportedAssets.json` | 运行说明、详细行为依据与历次验证、素材来源记录。 |

操作：F／J 为咚，D／K 为咔（游玩时同一帧只判定最早的一次打击，其余同帧打击丢弃——太鼓输入互斥），Space 暂停／恢复，F1 重开，Esc 返回选曲。Entry 中 F／J（或点击）加入／决定，D／K 只有咔声。选曲 D／K 移动、F／J 决定、A 自动演奏、Esc 返回 Entry（演奏オプション中 D／K 改值、F／J 下一行、Esc 关闭）；结算 F／J 跳过／返回。游玩页也可用鼠标／触控敲击原版样式的触控鼓，选曲板、难度卡和结算画面也可点击。

**选曲加载幕布（SongSelect → SinglePlayScene 过渡）。** 照搬 `transition.lua`／`anim/loading_song.lua`：关闭 532 ms（帧 5→55，在旧场景上）→ SongLoadingScene 停在帧 55（标题、副标题、皮肤「ゲームのヒント」原图、咚咔、星、光晕）→ 打开 532 ms（帧 60→109，在游玩场景上），打开结束后才开始倒计时。用户决定：幕布美术放在全局 SceneSwitcher；停留至少 2 秒；提示区用皮肤文字贴图（截图中的街机插画卡不在皮肤内）；所有 `Play()` 入口使用幕布，重开与其他切换仍为淡入淡出。演奏スキップON 徽章因该功能未实现而不显示。预解析谱面经 `SceneSwitcher.TakePreparedChart` 只交给 PlayScene 一次。详见 `Documentation/PortingNotes.md`「选曲加载幕布与 SongLoadingScene」。

- 切换机制：`Play()` 记录 SelectedSong／Course／AutoPlay 与 ReturnScene（排除 SongLoadingScene），`ShowSongOnCurtain` 写入 TJA 的 TITLE／SUBTITLE，再 `SwitchScene(SongLoadingScene, TransitionStyle.Curtain, autoFadeOut: false)`；关闭结束后加载，幕布保持关闭（`IsCurtainClosed`、`IsCovered` 为真，输入阻挡）。`SongLoadingScene` 等待 `minimumSeconds`（场景内可调，默认 2）与 `IsSwitching` 结束后调用普通 `SwitchScene(GameScene)`：**遮罩在打开前保持自身样式**——幕布已关闭时跳过关闭，加载后由幕布打开；打开时看 `SongTransition.IsVisible` 选择样式。失败／取消同样用当前样式打开旧场景。缺少幕布或 SongLoadingScene 不在 Build Settings 时，`Play()` 退回直接淡入 SinglePlayScene。
- 加载内容：`PlayScene.PrepareChart`（解析＋`ChartModifiers.Apply`，对应 `Player::reset_chart`）在主线程执行，失败只记日志，由 PlayScene 再解析并显示原错误面板；`song.music.LoadAudioData()` 等到不再 Loading。PlayScene 仍会对 `music.clip`／don／ka 调用 `LoadAudioData`，作为重开与直接运行的兜底。音频由 `SceneSwitcher.SelectedSong` 持有，不会随场景卸载。
- 表现细节：`SongTransition` 由 `LumenClip` 采样 `Animations/loading_song.txt`；关闭／打开均为真实时间。标题文字在幕布出现（`Appear`）时才写入并 `Squeeze(1920)`；主副标题均使用 `Nijiiro UI SDF` 与 5 px 黑色外描边，材质也保存在 SceneSwitcher 预制体中，旧归一化描边字段已删除。名牌预制体的名字与称号同样保存新字体引用；定向迁移入口 `ProjectBuilder.ApplyOutlinedUiFont()`，只更新这两个预制体的字体／材质。底部光晕用 `Generated/UI Additive.mat`（`Mobile/Particles/Additive`）；幕布根物体有透明 raycast Image 阻挡点击，`Viewport1920x1080` 带 RectMask2D，非 16:9 时幕布不会画进留边。
- 原版未移植部分：每首歌目录下的 `Loading.png` 自定义加载图（`add_loading_graphic`）、段位加载画面（`set_dan`）、Fanmade 远程下载进度页与取消、以及演奏スキップON 徽章（只在跳过功能启用时显示）。均未授权。
- 测试注意：经 `Play()` 进入游玩现在要经过幕布与至少 2 秒停留，原有 `WaitForScene(GameScene)`（条件为非切换中且活动场景为 SinglePlayScene）在 SongLoadingScene 停留期间不会误判完成。截图时幕布在 SceneSwitcher 的 Overlay Canvas 上，`SceneFlowTests.Capture` 只渲染场景 Canvas 拍不到；`SongLoadingCurtainTests.Capture` 会把所有根 Overlay Canvas 临时切到相机模式一起渲染。

**Entry。** 照搬 `scenes/entry.cpp`、`objects/entry/*` 与 Nijiiro `Scripts/entry/entry.lua`、`box.lua`、`player.lua`，Nijiiro 开启 `entry_credit_arcade`（街机投币模式）。用户决定：只有「演奏ゲーム」一块模式板（特訓モード／きせかえ／ゲーム設定 留待对应场景移植；段位道場 板在原版 `dan_available` 恒为 false，从不显示）；3D 咚与加入时的云不绘制，但模式选择仍按原版云动画结束时刻（加入后 550+350+333 ms）出现；两行信用行都显示，只有 1P 可以加入（任意咚面）；操作指引、フリープレイ＋QR＋2P 邀请云、状态芯片移植，60 秒计时器只作占位不倒数（ALL.Net 图标未做）。时间轴 `entry_bg`／`credit_row`／`credit_fade`／`credit_side`／`mode_board`／`cursor_glow` 由 `LumenClip` 采样。操作指引原图 4576×6900（325 格），只切片决定循环 210–324 格，导入上限 8192、CompressedHQ（未压缩约 126 MB）。`entry/global/player_entry_*` 为全透明 8×8 占位图，不绘制。TMP 描边受 SDF padding 限制，粗边（7 px、模式板标题双层边）为近似。详见 `Documentation/PortingNotes.md`「Entry 场景」。

**选曲／结算。** 流程：Entry（入口）→ SongSelect →（幕布＋SongLoadingScene）→ SinglePlayScene → Result → 发起游玩的场景（`SceneSwitcher.ReturnScene`）。`PlayScene.Finish` 保存成绩（自动演奏不保存）后调用 `SceneSwitcher.ShowResult`，场景内结果面板只用于谱面加载失败。时间与布局均取自原 `song_select.cpp`／`navigator.cpp`／`player.cpp`／`result.cpp` 及 Nijiiro Lua，详见 `Documentation/PortingNotes.md`“选曲与结算场景”。TMP Mobile SDF 描边需 `OUTLINE_ON`，新场景文字使用 Outline 材质。歌曲音频现在主要在 SongLoadingScene 中载入；PlayScene 仍于遮罩关闭期间预载（重开／直接运行），避免首次 PlayScheduled 卡顿约 1 秒。

### 已完成玩法与表现的核心逻辑

**分支游玩。** 支持 `#BRANCHSTART p/r`、`#N/#E/#M`、`#BRANCHEND`、`#SECTION`。命中率以良=1、可=0.5、不可／漏音=0 计算百分比并截断；连打分支只统计 5／6 号，气球和彩球不计入，并保留原版对当前持续连打总数的补充规则。分支判定和 SECTION 重置按歌曲事件时间处理，不把未来判定计入过去的分支。每条路线从共同起点恢复 BPM、时刻、SCROLL 等解析状态；选线时机参考原版对象进入画面的时间。未选路线不显示、不判定、不计分。普通深色、玄人蓝色、达人紫色，右侧贴图随路线变化滑动和淡入淡出；非分支谱隐藏这些标识。暂停冻结动画，重开恢复初始状态。

**Shinuchi 计分。** 基准分从 100 万分扣除气球预算（每个最多 100 次，每次 100 分）和连打预算（源码 float 常量约 16.92008 次／秒，每次 100 分），除以公共段＋达人路线的普通音符数，向上取整到 10 分；没有普通音符时基准分为 1000000。良得基准分，可按原整数除法减半并取 10 分整数倍，不可／漏音不加分。大音符、GOGO、连击不加倍率，5／6／7／9 号每次有效击打均为 100，吹爆不另加 5000。最终分数不限制为 100 万。连打预算直接使用对应尾部减去头部时间，即 `(EndTime - Time)×1000` 毫秒；已按用户要求修复原源码使用下一个对象、可能被小节线截短的缺陷。不可恢复为 nextobj 时长。BALLOON 缺失次数默认 1。参考 `tja.cpp::calculate_base_score` 与 `player.cpp`。

**魂槽。** 数值上限 10000，双精度累积，按原 `gauge.h` 难度／星级表增减；普通音符分母固定为公共段＋达人路线。良为 `1000000/(max(1,普通音符数)×soul_percent)`，可／不可按表乘倍率，每次判定限幅并以 `1e-6` 点容差归整过关／满槽边界；百分比向下取整。长音符不改变魂槽或重启淡入。LEVEL 缺失／为 0 时使用 Oni ★10 数值及 80% 过关档，原表未使用行保持零值。Normal／Hard 部分表项在原源码中标为推测，本项目保留来源值。50 格；Easy／Normal+Hard／Oni+Edit 的过关阈值分别为 **60%／70%／80%**，显示和结算共用。过关区使用加高的黄色贴图完整填充；新增格子 450 ms 淡入。满槽彩虹先用 450 ms 淡入，然后每 75 ms 过渡下一帧，共 8 帧／600 ms 循环；魂火 50 ms／帧、400 ms 循环。掉出满槽／过关状态时恢复相应样式，再次满槽重新淡入。参考 `src/objects/game/gauge.cpp` 与 Nijiiro 的 `skin_config.json`、`Graphics/game/gauge/texture.json`、`Graphics/game/animation.json`。

**7 号气球。** 脸的相对对齐量是音符宽度的 **12/128**，通过锚点随音符尺寸与画布缩放，不能改回固定像素补偿。首次有效咚击打后显示 `总次数 - 已击打次数`，使用 Nijiiro 气泡及数字图集，支持跨位数布局；咔不计数。数字 50 ms 拉伸＋116 ms 回弹，身体按进度使用 0、2、3、4、5、6 帧；吹爆后第 7 帧、数字 0，166 ms 淡出；未吹爆到期立即隐藏。移动时显示 `notes/10` 尾部，击打时替换为膨胀素材；9 号彩球不混用这套显示。`Assets/OurTaiko/Audio/balloon_pop.ogg` 原样复制自 Nijiiro，约 0.414 秒、预加载；手动／自动达到要求次数的那次事件只播放一次，到期未爆不播放。参考 `player.cpp::draw_balloon/check_balloon`、`balloon_counter.cpp` 及 Nijiiro 气球配置。

**音符飞向魂槽与 GaugeHitEffect。** 照搬 `note_arc.cpp`／`gauge_hit_effect.cpp`，数值取 Nijiiro 皮肤。良／可的 1–4 号音符、5／6 号连打每次击打（按所敲的鼓飞小咚／小咔，自动演奏为咚）、吹爆的 7 号气球各飞一个；不可与 9 号彩球不飞。路径为 Nijiiro `note_arc_pivot` 圆弧（圆心 (1269.22,415.14)、半径约 719.14、-154.89°→-38.24° 等角速度、`note_arc_duration` 30 帧×16.67 ms），**不是** OurTaikoPlayer 当前代码的贝塞尔（22 帧、curve height 608）——此取舍已告知用户，用户未要求改回；读取圆弧键的原实现在 OurTaikoPlayer `7a08ced`，合并时被丢弃但皮肤键保留。起点判定圈中心（轨道局部 618,110），终点魂徽章中心 (1834,-30)；顶点飞出设计区顶边，由 `NoteArcs` 的 RectMask2D 裁掉。到达时由 `NoteArcView` 交给 `GaugeHitEffectView`（同时只有一个，新到达清除旧的，按到达时刻 `开始+30 帧` 计时）：`gauge/hit_effect` 三帧 232×232（33.33／66.66 ms 换帧），116.67 ms 后 266 ms 内从 0.8 线性放大到 1.5，按尺寸着色黄 (253,249,0)→橙 (255,161,0)→红 (230,41,55)，300 ms 后 83 ms 淡出；音符画在光环之上同步淡出。Nijiiro 的 `hit_effect_circle*` 是全透明 8×8 占位图、旋转固定为 0，因此不绘制，不要当作遗漏。层级：`SoulGauge` → `NoteArcs` → `GaugeHitEffect`（原版先画魂槽再画 `draw_overlays`）；坐标经 `NoteLane` 变换换算，使用歌曲时钟，暂停冻结。迁移入口 `ProjectBuilder.ApplyNoteArcs()`（菜单 OurTaiko/Apply Nijiiro Note Arcs），帧切片 `Generated/GaugeHitEffect0–2.asset`。未移植：气球吹爆彩虹拖尾（`balloon/rainbow`／`note_arc_balloon_*`）。详见 `Documentation/PortingNotes.md`「音符飞向魂槽与 GaugeHitEffect」。

**名牌与分数计数器。** 照搬 Nijiiro `Scripts/global/nameplate.lua` 与 `score_counter.cpp`，素材为 Nijiiro `global/nameplate`（未导入 2P／AI 牌）。数据由全局 `PlayerInfoController`（首场景前自动创建、跨场景保留，用户决定）在初始化时读取 `Application.persistentDataPath/player.json`（缺失时写入默认 Don-chan／Donder Debut!），`Changed` 事件让所有 `NameplateView` 即时更新；无游戏内编辑界面。「Donder Debut!」或空为无称号，dan 只接受 0–24；无称号无段位为 coin 牌（30 号名字，无称号带／段位），否则画称号带（titleBackground 0–4，越界归 0）、段位（gold 金色）、黑色称号与 24 号名字；文字只横向压扁到框宽（名字 190、称号 215）。位置：游玩 `NoteLane` 局部 (-44,161)（鼓面之后、BalloonCounter 之前），选曲 (14,908)（Wheel 与 CoursePanel 之间），结算 (2,922)（SoulSheen 之后、FadeIn 之前）。名字黑边使用独立的 `Resources/Nijiiro UI SDF.asset`（64 采样字号／32 padding）恢复 3 px 外描边；`SkinUi.OutlineOutsidePixels` 也供选曲标题与 2P 提示使用，修复旧图集产生的灰色字形矩形。详见 `PortingNotes.md`「UI 字体外描边修复」。分数计数器替换了原 TMP 占位分数：灰条在轨道局部 (0,12)，数字右对齐 x 255、间距 30、不补零、顶边 5.5，变化时 TextStretch 向上伸长，作为 `NoteLane` 最后一个子物体。SinglePlayScene 的调试文字 PlayerName 与 PlayState（READY 倒计时／AUTO PLAY／1 PLAYER）已删除（用户决定）。未移植：「+分数」飞出动画（`ScoreCounterAnimation`）。详见 `Documentation/PortingNotes.md`「名牌、PlayerInfoController 与分数计数器」。

**音符文字（moji）。** 音符下方的「ドン／ド／コ／カッ／カ／ドン(大)／カッ(大)／連打ー／連打(大)ー／ふうせん／ーっ!!／くすだま」取自 `notes/moji` 12 帧（256×48，Point 采样，`Generated/Moji0–11.asset`）。分配在 `Core/NoteMoji.cs`，照搬 `tja.cpp::modifier_moji/find_streams`：解析器按原版 NoteList 保留源顺序的 `TaikoChart.NoteLists`（公共段一条，每个分支每条路线各一条，含小节线与长音符尾），依次按 8／12／16／24／32 分查连续段（±15 ms，长音符头断开，小节线和尾参与），段内除末个外咚→ド、咔→カ，恰 3 个咚时中间为コ；`ChartModifiers.Apply` 末尾重算，文字随あべこべ／ランダム换色。原版一小节分多行书写时会插入额外隐藏小节线并截断连续段，本项目每小节只有一条小节线，不复制该现象。渲染在 `PlayScene.RenderMoji`：独立的 `NoteLane/MojiClip/Moji` 层紧跟 `LaneClip`（全部文字压在全部音符之上，层内早的音符在上），文字中心比音符中心低 123（skin `moji.y`=209 对 `notes.y`=14）；显隐与音符相同（命中消失、漏音继续流动、ドロン一并隐藏），按自身宽度裁切；气球计数显示期间按 `skip_note` 隐藏；连打为 `moji_drumroll_mid`（宽 8＋长度）→头字→尾字ーっ!!，高度不随 Y 滚动。迁移入口 `ProjectBuilder.ApplyMoji()`。

**音符显隐与层级。** 照搬原版 `draw_note_buffer`：只有击打（良／可／不可）会立即移除音符；超时漏音（`PlaySession.Missed`，由 `AdvanceNotes` 超时置位）与到尾判定的 5／6 号连打继续按原速流过判定点，直到完全离开轨道。气球／彩球仍按吹爆或到期隐藏。裁切不用固定像素：`PlayScene.InLane` 以 `LaneClip` 下音符层的实时 rect 判断，`Reach` 按当前贴图尺寸计算水平范围（普通音符半宽；气球含 12/128 脸偏移与 `notes/10` 尾；连打含长度与尾部贴图，正负滚动均可），小节线按自身半宽。层级按原 `draw_notes` 逆序绘制：`CreateNotes` 对每个音符 `SetAsFirstSibling`，早的音符压在晚的音符上（每个连打内部仍为身体→尾部→头部）。因此音符层子物体顺序与谱面顺序相反，测试和代码须用 `PlayScene.NoteRoot(index)` 取音符，不能用 `noteLayer.GetChild(i)`。由 `ChartTests.OnlyTimedOutNotesAreMarkedMissed` 与 `SceneFlowTests.FinishedRollsAndMissedNotesKeepScrollingPastJudge` 覆盖。

**5／6 号连打。** `PlayScene` 分别持有小／大 `rollBodySprites` 和 `rollTailSprites`；身体只横向拉伸，尾部保持原宽高比，顺序为身体→尾部→头部。源图集左上坐标切片：小身体 `(0,1544,72,192)`、大身体 `(72,1544,72,192)`、小尾 `(0,2120,80,192)`、大尾 `(0,2312,120,192)`。身体长度为连打长度绝对值加音符高度的 **1/128**（192 设计高度时为 1.5），对应原版贴图宽度与 `drumroll_width_offset` 的合成，覆盖接缝。尾部起点位于连打结束位置；负滚动翻转身体和尾部，头部表情保持正向。音符图集使用 **Point** 采样，避免双线性采样混入邻近大连打帧形成杂边。生成资源位于 `Assets/OurTaiko/Generated/`：`RollBodySmall.asset`、`RollBodyBig.asset`、`RollTailSmall.asset`、`RollTailBig.asset`；配置入口 `ProjectBuilder.ApplyDrumrollSprites()`。参考 `player.cpp::draw_drumroll`、`src/libs/texture.cpp` 及 Nijiiro 音符 `texture.json`；头尾同速逻辑未改动。

### 验证结果与继续工作方法

- 最新验证（2026-10-02，全部程序集）：重开 Editor 后编译通过，进行中 EditMode 8/8、PlayMode 4/4，已完成 EditMode 132/132、PlayMode 28/28 全部通过，报告 `TestResults/final-*.json`。`SongLoadingCurtainTests` 曾偶发失败：停留时间从切换完全结束（晚于场景 Start 一帧加 50 ms）开始计时、咚／咔位置用精确浮点比较；已改为从加载场景成为活动场景时计时、位置用 0.01 容差。`GlobalSceneSwitcherTests` 不再在遮罩上写黄色「LOADING」测试文字（游戏本身从不显示该文字）。
- 此前验证（2026-10-02，Entry）：进行中 EditMode 11/11、PlayMode 3/3，已完成 PlayMode 27/27 通过（菜单场景改为 Entry 影响所有从菜单开始的 PlayMode 测试，因此回归了 Finished 程序集）；报告 `TestResults/entry-playmode.json`、`TestResults/entry-finished-playmode.json`，截图 `TestResults/EntryCredit.png`、`EntryModeSelect.png`。
- 此前验证（2026-10-02，名牌与分数计数器）：Unity Editor **EditMode 139/139、PlayMode 29/29 全部通过**，报告 `TestResults/nameplate-editmode.json`、`TestResults/nameplate-playmode.json`；新增 `NameplateTests`、`NameplateFlowTests`，`ScoreGaugeFlowTests` 改为检查 `scoreCounter.Text`（不补零）。截图 `TestResults/NameplatePlay.png`、`NameplatePlayCoin.png`、`NameplateSongSelect.png`、`NameplateResult.png`。PlayMode 的 `TestScoreStore` 让 `PlayerInfoController` 使用不落盘的默认数据。
- 此前验证（2026-10-01，音符飞向魂槽与 GaugeHitEffect）：Unity Editor **EditMode 131/131、PlayMode 27/27 全部通过**，报告 `TestResults/arc-editmode.json`、`TestResults/arc-playmode.json`；新增 `NoteArcPathTests`、`GaugeHitEffectTimingTests`、`NoteArcFlowTests`，截图 `TestResults/NoteArc.png`。路径按 Nijiiro 皮肤的圆弧键而非 OurTaikoPlayer 当前的贝塞尔，理由见 `Documentation/PortingNotes.md`「音符飞向魂槽与 GaugeHitEffect」；Nijiiro 的 `hit_effect_circle*` 为透明占位图，不绘制；气球吹爆彩虹拖尾未移植。
- 此前验证（2026-10-01，选曲加载幕布）：Unity Editor **EditMode 127/127、PlayMode 26/26 全部通过**，报告 `TestResults/curtain-editmode.json`、`TestResults/curtain-playmode.json`；新增 `SongLoadingCurtainTests`，截图 `TestResults/CurtainClosing.png`、`SongLoading.png`、`CurtainOpening.png`。
- 此前验证（2026-10-01，音符文字）：Unity Editor **EditMode 127/127、PlayMode 24/24 全部通过**，报告 `TestResults/moji-editmode.json`、`TestResults/moji-playmode.json`；`NoteMojiTests` 覆盖帧分配／连续段／小节线／分支独立／换色重算，`NoteMojiFlowTests` 覆盖帧、层级、位置、命中／漏音／ドロン显隐与连打横条，截图 `TestResults/NoteMoji.png`。已知：调试计数文字 `GOOD/OK/BAD/ROLL` 与文字带重叠，未处理。
- 此前验证（2026-10-01，演奏オプション）：Unity Editor **EditMode 115/115、PlayMode 22/22 全部通过**。报告为 `TestResults/options-editmode.json`、`TestResults/options-playmode.json`；PlayMode 通过 `TestScoreStore` 使用临时成绩文件和不落盘的 `PlayOptions`。`PlayOptionsTests`／`PlayOptionsFlowTests` 覆盖速度档位、面板行走、滑动曲线、谱面修改、随机概率、持久化与游玩场景实际效果；截图 `TestResults/SongSelectOptions.png`、`PlayOptionsBadges.png`。`TestResults/` 不纳入版本控制，本机报告不保证随新克隆存在。
- 测试分为进行中与已完成两组（2026-10-02 用户要求）。**进行中**：`OurTaiko.Tests`（`Tests/EditMode/`）与 `OurTaiko.PlayModeTests`（`Tests/PlayMode/`），目前为名牌（`NameplateTests`、`NameplateFlowTests`）与选曲／结算全局元素（`GlobalOverlayFlowTests`）；Entry 已完成（2026-10-02 用户确认），`EntryTests`、`EntryFlowTests` 移入 Finished。**已完成**功能的测试移到 `Tests/Finished/`：`OurTaiko.FinishedTests`（EditMode）与 `OurTaiko.FinishedPlayModeTests`（PlayMode），类名与命名空间 `OurTaiko.Tests` 不变。共享辅助在 `Tests/Shared/`（`OurTaiko.TestSupport`，仅 `UNITY_INCLUDE_TESTS` 时编译）：`TestCapture.Capture` 截图、`TestData.Use/Restore` 临时成绩／选项／玩家信息，每个 PlayMode 程序集各有一个调用它的 `TestScoreStore` SetUpFixture。功能确认完成后把其测试移入 Finished。**只运行与改动相关的测试类**，不要每次跑全部程序集；需要回归时再跑 Finished 程序集。
- 已完成的 EditMode 测试（原 `Tests/EditMode/`）覆盖解析、判定、分支阈值／时序、滚动与同速约束，以及 Shinuchi 预算／取整、独立逗号／空小节、魂槽难度星级／增减／过关边界。
- 已完成的 PlayMode 测试中，`SceneFlowTests.cs` 覆盖场景流程、音乐同步、120 FPS 配置、各分支、魂槽、气球与连打，包含 1080p／720p 渲染。`GlobalSceneSwitcherTests.cs` 另覆盖跨场景预制体、等待准备任务、关闭／加载／打开顺序、timeScale=0、输入阻挡、重复请求、场景事件、手动揭示、泛型结果、失败／取消恢复及销毁取消。
- `SongLoadingCurtainTests.cs` 覆盖幕布帧映射（关闭 5→55、标题 266 ms 延迟淡入；打开 60→109、133 ms 淡出、标题带压扁）、在旧场景上关闭、SongLoadingScene 停在帧 55 的各图层状态、至少 2 秒停留、音频已载入、PlayScene 取走预解析谱面、在游玩场景上打开并解除输入阻挡、重开仍为淡入淡出。
- `ScoreGaugeFlowTests.cs` 覆盖实际游玩场景的 Shinuchi HUD、魂槽过关／满槽／失去过关、气球击打不重启淡入、结算状态及重开清零。
- `DrumInputMutexTests.cs` 用虚拟 Input System 键盘（测试期间临时设 `IgnoreFocus`／`AllDeviceInputAlwaysGoesToGameView`，结束后还原）覆盖输入互斥：同帧多次按下按发生顺序进入 `PressesThisFrame`；连打中同帧按下 D／F／J／K 只计 1 次连打且只亮最先按下的鼓面，分帧按下各自计数。已用变异检查确认去掉 `HitFirstDrumPress` 的 `return` 时测试失败。
- 已提交截图在 `Documentation/`：`DrumrollSmall.png`、`DrumrollBig.png`、`BalloonAtJudge.png`、`BalloonCounter.png`、`GaugeClear.png`、`GaugeRainbowA.png`、`GaugeRainbowB.png` 及分支截图。
- 优先通过 Unity Test Runner 验证实际场景。控制 Editor 前读取可用的 `unity:unity-cli` 技能；本机 CLI 为 `/Users/kirisamevanilla/.unity/bin/unity`，项目安装了 `com.unity.pipeline` **0.8.0-exp.1**。CLI 成功响应还需检查嵌套命令结果，不能只看进程退出码。
- 以下是已验证的 Editor 命令形式（公共参数在具体命令名前）。先确认正确项目的 Editor 已启动并连接；修改 C# 后刷新并等待编译结束。两组测试依次运行，异步启动后用 `test_status` 确认最终结果，保存报告后再启动下一组：

```sh
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json eval 'UnityEditor.AssetDatabase.Refresh();'
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json run_tests --mode editor --filter OurTaiko.Tests --filter_type assembly --async_tests true
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json test_status
/Users/kirisamevanilla/.unity/bin/unity command --caller plugin --skill unity-cli --project-path /Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity --format json run_tests --mode playmode --filter OurTaiko.PlayModeTests --filter_type assembly --async_tests true
```

已完成程序集把上面的 `--filter` 换成 `OurTaiko.FinishedTests`／`OurTaiko.FinishedPlayModeTests`。只跑单个测试类时用 `--filter OurTaiko.Tests.<类名> --filter_type testName`（两组的命名空间都是 `OurTaiko.Tests`）（`filter_type` 只接受 testName／assembly／category，`class` 会报错且结果为 0 个测试）。测试状态原件为 `Temp/pipeline_test_status.json`，每次运行会覆盖。如需新 macOS 播放器，使用 `OurTaiko.Editor.ProjectBuilder.BuildMac()`，另行确认构建成功；不要把旧构建作为最新验证证据。

### 明确尚未实现的范围

- **Entry 未移植部分**：3D 咚与加入云（原版 `player.lua` 的 drum_back／drum_front）、2P 加入、其他模式板（特訓モード／きせかえ／ゲーム設定）与きせかえ菜单（`costume_menu`）、ALL.Net 图标（`allnet_indicator`）。选曲与结算的全局元素已放置（ALL.Net 图标仍未做）。
- **名牌待办**：原版段位选择（`dan_select.cpp`）与段位结算（`dan_result.cpp`／`dan_result_draw.lua` 的 `nameplate_pos`）场景也显示名牌。将来移植这些场景时须同样复用 `Generated/Nameplate.prefab` 与 `PlayerInfoController`；2P／AI 名牌（`2p.png`／`ai.png`）与名牌编辑界面同样未做。

当前不是整个原模拟器的等价移植。游玩中气球吹爆的彩虹拖尾、选曲中的文件夹／类别、搜索与排序、独立音色面板、演奏スキップ功能（及加载幕布上的演奏スキップON 徽章）、歌曲自定义 `Loading.png` 加载图、段位加载画面、2P、曲目板飞入、难度决定标记弹出，结算中的成绩等级（粋／雅／極）演出、3D 咚、皇冠光芒加算混合、游玩中的「+分数」飞出动画尚未移植；TRIPLE HELIX 的 Edit 谱面含字母扩展音符，无法游玩。联网／成绩上传、双人、段位、3D 咚角色、全部皮肤特效、逐帧回放尚未实现（大音符单侧击打即可，属有意设计，见上方约束）；计分固定使用 Shinuchi，单人魂槽数值已按原源码还原，GEN3 计分及段位魂槽不在当前范围。自动连打仍为 **15 次／秒**，未移植原版随 BPM 变化的自动连打节奏。`s` 分数分支、`#LEVELHOLD`、BMSCROLL／HBSCROLL、字母扩展音符明确不支持；不要静默猜测其行为，也不要将这些范围自动当作用户已授权的新开发任务。
