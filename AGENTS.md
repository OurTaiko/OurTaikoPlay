# 标准交接摘要

更新日期：2026-10-02。本文记录当前有效结论；`Documentation/PortingNotes.md` 中的早期 Green／1280×720 和简化计分／魂槽记录仅是历史，不代表当前规格。

## 1. 核心项目目标

项目产品名称为 **OurTaikoPlay**（2026-10-03 从 OurTaikoPlayerUnity 更名），构建文件使用新名称；移动包名 `org.ourtaiko.play`。

将相邻 OurTaikoPlayer 的单人游玩页面与玩法移植为纯 C# 的 Unity 2D 项目，通过全局 SceneSwitcher 控件、Entry、ServerLogin（在线服务器登录）、SongSelect、SongLoadingScene、SinglePlayScene 与 Result 提供采用 Nijiiro 皮肤、行为参照原模拟器的可运行单人流程。SceneSwitcher 不是场景；入口为 Entry（Build Settings 首个场景、`SceneSwitcher.MenuScene`；2026-10-02 删除 Test_DefaultScene 测试入口及 LaunchMenu，随后移植 Entry），所有运行时场景切换从全局控件开始，并交由它完成。

## 2. 当前已知事实/约束条件

- 工作项目：`/Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayerUnity`；玩法参考源码：`/Users/kirisamevanilla/Repos/OurTaiko/OurTaikoPlayer`；全局场景切换架构参照相邻 MajdataPlay 的 `Assets/Scripts/Global/SceneSwitcher.cs`。两个参考项目都只读，不修改。
- 技术栈：Unity **6000.3.25f1**、Universal 2D／URP **17.3.0**、uGUI、TextMeshPro、Input System。运行逻辑和 Editor 工具全部使用 **C#**，不引入 C++、Lua 或原模拟器的原生插件。**音频例外（用户要求，2026-10-03）**：采用 MajdataPlay 的原文件字节直接解码流程（BASS→Opus→AAC）、BassSimple 直接输出及 Windows WASAPI／ASIO 混音；同一音效重播已有采样，歌曲峰值归一化。ManagedBass 使用未修改的 TeamMajdata Git 子模块，克隆后 `git submodule update --init --recursive`；原生库包括 BASS／BASSmix／FX／Opus／AAC／WASAPI／ASIO，C# 管理层在 `Runtime/Audio/`；保留 AudioSource 作为场景资源引用，运行时统一调用 `AudioPlayback`，不能直接 Play／PlayOneShot 绕过后端。详见 `PortingNotes.md`「跨平台原生音频」。
- 所有场景设计画布与默认窗口均为 **1920×1080**（SongSelect／Result 的 Viewport 带 RectMask2D）；宽高比变化时保持设计区域比例并居中留边。贴图对齐与局部偏移使用相对锚点或尺寸比例，不能写成固定屏幕像素补丁。
- 当前只做 **Nijiiro**。素材主要来自 `Skins/YataiDONNijiiro`；缺少的资源已从 Green 直接复制补齐并打平。不实现皮肤继承、运行时回退或 Green 皮肤切换。图片保持原文件，使用 Sprite 切片；来源见 `Documentation/ImportedAssets.json`，保留 LICENSE／NOTICE 与资源权利归属。
- 默认目标 **120 FPS**：关闭 VSync，`renderFrameInterval = 1`，`targetFrameRate = 120`；可在设置 Display › Target Frame Rate 改为 60 FPS 或 Unlimited，Display › VSync（默认关）开启后 `vSyncCount = 1`，桌面端由刷新率取代目标帧率，移动端忽略 VSync（`DisplaySettings.Apply`，由 `SceneSwitcher` 启动时及设置变化时应用）。FPS 计数器每 **0.5 秒**显示实际平均帧率，暂停时仍更新；目标帧率不保证显示器实际达到 120 Hz。
- 音符位移公式：`(判定时间 - 当前时间，秒) × BPM / 240 × SCROLL × 判定点到右边缘的距离`。当前判定点设计 X=**618**，右边缘 X=**1920**，距离 **1302**；192×192 音符的加载边界半宽为 **96**。音画偏移只改变相位，不改变速度。
- **连打头尾必须同速**：统一采用头部 BPM／SCROLL，长度按头尾时间差乘头部速度计算并保持恒定。中途变速只影响后续音符，不得恢复为头尾各用独立速度。
- 动画时长、裁切、布局和判定行为先查原模拟器源码与 Nijiiro 配置。用户对魂槽“约每 0.5 秒闪黄”的描述是观察猜测；实际原代码是新增格子 **450 ms 淡入**，没有整条周期闪黄，不应另加猜测效果。
- 分支谱面通过轨道颜色及轨道右侧「普通譜面／玄人譜面／達人譜面」贴图识别，不使用独立分支文本框。
- **OurTaikoPlayer 也是模拟器，不是原版游戏。** 它是行为参考，但其中有错误；用户在移植中修正这些错误。下列是**有意的偏离**，不要按 OurTaikoPlayer 源码「还原」，也不要当作待办：
  - **输入互斥（每帧一击）**：游玩时每帧只判定最早的一次咚／咔打击（键盘、触控、鼠标按发生时间合并排序），同帧其余打击直接丢弃，不判定、不播音效、不亮鼓面。OurTaikoPlayer 的 `player.cpp::handle_input` 每帧按固定顺序（左咚、右咚、左咔、右咔）逐个处理全部打击，这是被修正的行为。后果是已知且接受的：连打中同帧双手只计 1 次；同帧先咔后咚时咔占用该帧；同帧双手打大音符不会误吃下一个音符。**判定时间也有意按帧统一**：使用处理该帧时的歌曲时间（含音画偏移），输入事件时间只用于排序，不回推到按键发生时刻；不要改为逐事件时间判定。实现在 `PlayScene.HitFirstDrumPress`，由 `DrumInputMutexTests.cs` 覆盖。
  - **大音符不需要双手同时击打**：大咚／大咔单侧击打即为完整判定，与小音符同样计分。不实现双手判定窗口或双击加分，不要把它列为未移植功能。
  - **演奏オプション**（用户决定）：ドロン只隐藏音符，小节线保留；ランダム按每个咚／咔音符独立概率换色（きまぐれ 30%、でたらめ 50%），不用原 `modifier_random` 的 (对象数/5)×档位 抽取；演奏スキップ灰显不可改（单人没有 2P 鼓）；轨道徽章网格按整数行，不复制原 `slot/3.0` 浮点下移。详见 `Documentation/PortingNotes.md`「演奏オプション」。
  - **计时器不倒数**（用户决定：模拟器不限制玩家时间）：原版 Entry 60 秒、选曲列表 100 秒、难度选择 60 秒倒数，归零替玩家决定（难度选择停在もどる／选项时还会把无效难度传给游戏）；本项目三处都只显示 60／100／60 作占位，不倒数、无 blip 与语音、不自动决定。计时器音效未导入，倒数与 10 秒内红色弹动的代码已删除（`ArcadeTimerView` 只显示固定数字）；如需恢复，参照原版 `Scripts/global/timer.lua` 与 `PortingNotes.md`「Entry 场景」中的记录。
  - **名牌与自动演奏**（用户决定）：原版自动演奏时在名牌位置画 `lane/auto_icon` 取代名牌；本项目名牌始终显示，自动演奏只在演奏オプション徽章区第一位加入选曲的 `song_select/modifier/mod_auto`，没有其他视觉差别。名牌彩虹称号带按 6 帧／50 ms／300 ms 循环；原版从未 `start()` 该动画（停在第 0 帧），属被修正的缺陷。
- **单一字体与黑色描边**（用户决定，2026-10-03）：全部 TMP 文字只用一个 SDF 资产 `Resources/Nijiiro UI SDF.asset`（源 `Art/DDFont.ttf`，64 采样字号／32 padding），只有两种共享材质：浅色文字用 `Resources/Nijiiro UI SDF Outline.mat`（不透明纯黑描边），**本身为黑色／近黑色的文字不加描边**，用字体自带材质（`SkinUi.IsDark`：文字颜色亮度 0.2126R+0.7152G+0.0722B < 0.3，如名牌称号、暂停按钮、设置详情、Entry「１人プレイ」）。不要再加第二个 SDF、按用途的材质或文字实例材质。描边宽度为 TMP 归一化宽度 `SkinUi.OutlineWidth`（0.125，`_FaceDilate` 同值使描边在字面之外），只随各文字的字号与缩放变化，不写死像素。不按背景亮度或 alpha 混合调整（2026-10-03 试过后由用户撤回），不使用类别色、模式色或硬编码描边表。新文字用 `SkinUi.Text(name, parent, size)`，改变文字深浅后调用 `text.UseUiFont()` 重新选材质；迁移入口 `ProjectBuilder.ApplyUnifiedUiFont()`（菜单 OurTaiko/Apply Unified UI Font，按颜色选材质，重复执行不改动）。
- **名牌网格初始化**：不要在 `NameplateView.SetText`／`OnEnable` 中提前 `ForceMeshUpdate`。静态游玩名牌会因 CanvasScaler 尚未稳定导致 SDF 缩放重复计入，出现浅灰字。仅在 `willRenderCanvases` 中首次或 Canvas 比例变化后刷新；验证应读实时 CanvasRenderer 网格与 Game 截图，旧 `TestCapture` 切换渲染模式会掩盖此错误。详见 `PortingNotes.md`「游玩名牌首次渲染灰字修复」。
- 跨平台构建（2026-10-03）：统一入口 `Editor/PlayerBuilds.cs`，菜单 **OurTaiko/Build**，覆盖 macOS Universal（Mono）、Windows x64（Mono）、Android ARM64 APK（IL2CPP）、iOS Xcode 工程（IL2CPP）。移动端包名固定 **`org.ourtaiko.play`**、仅左右横屏；构建产物与报告在被 Git 忽略的 `Builds/`。命令行必须在启动 Editor 时传匹配的 `-buildTarget`，不要在同一次批处理内切平台后立即构建。操作与签名边界见 `Documentation/Building.md`。
- Git 已初始化，当前直接在 `main` 上提交（线性历史，无合并提交）；提交使用 **Conventional Commits**。保留用户已有改动，不把无关资源混入提交。新建分支默认使用 `kirisamevanilla/` 前缀。
- 场景、Sprite 资源、导入设置等持久化内容通过 Unity Editor API 修改并保存；避免手工改 Unity YAML／GUID。现有场景可直接编辑，不要随意执行生成初始场景的工具覆盖布局。
- 后续交流以中文为主；能根据原代码确定的常规实现直接完成并验证，无需重复询问已经确定的约束。

- **A/B 全局延迟（2026-10-04）**：Settings › Play 的 Offset A (Audio)／Offset B (Judgment)，默认 0 ms、每次 1 ms。A 移动整张谱面相对音乐的位置，B 只移动手动判定窗口（含漏判和长音符），不移动显示或自动演奏；正值推迟、负值提前。两种游玩模式共用，练习跳小节只根据视觉时间定位，重置保留 B；偏移采用谱面时间单位，随练习速度缩放。不增加单曲延迟设置。新增数值设置用 `SettingItem.Number(defaultValue, step, unit, get, set)`，咔／触控箭头调整，咚／点数值保存，Esc／遮罩取消。详情见 `PortingNotes.md`「A/B 全局延迟与数值设置」。

## 3. 最新进展与核心资产

### 当前完成状态与交接边界

#### 练习分支：固定路线（2026-10-05）

- 练习菜单对有分支的谱面增加首页「谱面分支」（普通／玄人／達人譜面，咔切换、两端停住、咚进入小节进度），之后为小节进度、播放速度；无分支谱面没有此页。练习不评估任何分支条件，全部分支走所选路线；切换路线就地重建本次练习与小节列表，再次暂停回到分支页并保留路线。
- **解析与游玩分离（用户决定）**：`TjaParser.Parse(text, course)` 对所有模式完全相同，不接收分支参数；`s` 条件记为 `BranchCondition.Score`，缺 `#E` 用 `#N`、缺 `#M` 用 `#E`（`ChartBranch.Routes`／`ResolveRoute`，`#N` 仍必需）。路线选择只在 `PlaySession(chart, judgeOffset, forcedBranch)`／`PracticeAt(..., forcedBranch)`：有值时每个分支取 `ResolveRoute(路线)`、不计算条件；无值（单人游玩）时遇到 `Score` 或缺路线在构造时抛 `NotSupportedException`，不猜测行为。谱面对象不保存分支选择（已删除 `TaikoChart.ForcedBranch`）。`ChartStatistics` 的达人统计按 `ResolveRoute(Master)`。
- 路线只在游戏内练习菜单选择（用户决定）：练习始终从普通譜面开始。View_Web 宿主不再传 branch，点「开始」只关闭遮罩、停在游戏内菜单（无分支谱面为小节进度页），与 Play 进入练习一致；Bridge 用普通譜面固定路线的 PlaySession 验证谱面，`getState` 增加 `stage`。共享文件（TaikoChart、TjaParser、SongDefinition、PlaySession、ChartStatistics、LaneWindow、PracticeProgress、PracticeView、PlayScene.Practice）两边逐字相同，修改时同步。
- 测试：`BranchRouteTests`、`PracticeTests.PracticeForcesTheChosenBranch`、`PracticeFlowTests.BranchPageComesFirstAndFixesTheRoute`／`PracticeOpensScoreBranchesWithOmittedRoutes`。`BranchTests` 中「缺 #M 解析报错」「s 解析报错」两例已移到 session 层。

#### 音符可见区间与判定游标（2026-10-05）

- 起因：长谱面每帧遍历全部音符／小节线（显示与判定），练习暂停静止时也照算；iPad Safari Web 播放器约 80 FPS（Web 帧调度与 2 倍像素密度另待实测，可能才是主因）。
- **渲染**：`Core/LaneWindow.cs`。`LaneCull.ForNotes`／`ForBars` 按 x(t)=判定点+(T−t)·速度 的线性关系，为每个音符（含文字）与小节线预算可能接触轨道的时间区间；外扩 3×max(音符,文字宽)+连打长度。负速交换两端，零速／非有限速度视为全时段，气球 t≥T 停在判定点故区间不结束。区间**只做候选筛选**，`RenderNotes` 对候选仍走原精确 `InLane(Reach)` 与 alive 判断；离开区间的对象归还池，`Restack` 只排候选。前进时按起点游标增量更新，时间倒退（跳小节、回滚、重开）整组重建；轨道宽度变化重建区间并清空画面。时间、Session 引用、`Session.Version`、气球计数、预览模式都未变时整帧跳过（暂停静止），舞者同一时刻不重复采样。
- **判定**：`PlaySession` 依赖 `Chart.Notes` 按时间排序（`TjaParser` 排序）：`head` 游标＋`pending`（已到达未结算，按索引升序）。未决定分支的音符保留在 pending（防御性，现有谱面分支总在路线音符前决定），`NextInLane`／连打查找只看 pending 和 head 之后；事件顺序与原全谱扫描一致。未排序谱面退回全扫描。
- **状态只读**：`Resolved`／`Missed`／`LongHits` 对外为只读视图，只有 PlaySession 写入并递增 `Version`；不要恢复为公开数组，否则跳帧会漏重绘。测试用真实 `Hit`／超时制造状态。
- 验证：`Tests/EditMode/ChartCursorTests.cs` 与逐字保留的 `ReferencePlaySession.cs`（`0e4fb7c` 的全扫描实现）逐帧对比事件／状态／分数，并对每帧用精确裁切检查候选覆盖；谱面含 `Tests/EditMode/Charts/Donkama2000.txt`（用户提供）。变异检查：外扩改 0、`NextInLane` 忽略 pending 均被抓到。Donkama Oni 765 音符每帧候选 ≤40；16000 音符判定 0.04 vs 29.4 µs/帧（Editor）。提交 `3f93225`、`c3a30c4`。
- 待办：同步 View_Web 后在 iPad 实测；渲染部分的毫秒收益尚未单独计时。

#### 已知测试问题：iOS 构建平台下 BassMix 失效（2026-10-05）

- `NativeDecoderTests.MixedOutputResamplesPausesAndRoutesWithoutDeviceSpecificDrivers` 在 **Editor 构建平台为 iOS** 时稳定报 `EntryPointNotFoundException: BASS_Mixer_StreamCreate`。原因：`3e5d299` 为 iOS 静态链接设置 Player Settings iOS 定义 `__STATIC_LINKING__`（`AudioBuildSettings.Configure` 自动补），ManagedBass `BassMix.cs` 仅按该符号选 `"__Internal"`、不排除 Editor（核心 `Bass.cs` 用 `UNITY_IOS && !UNITY_EDITOR`）；Editor 用 iOS 定义编译，就去 Editor 进程找 bassmix 符号。`libbassmix.dylib` 有该导出，测试、子模块与原生库自 `3e5d299` 未变。与代码版本无关，切回 macOS 平台预计通过（尚未实测）。
- 影响：iOS 平台下的 Editor 中，BASS 后端凡用混音器之处（`AudioEngine` WASAPI／ASIO 混音、`NativeAudioSample` 重采样）同样失败；真机 iOS 不受影响。修复方向（未做，需用户确认）：不改子模块，仅在 iOS 构建期间临时加符号，或把测试限定非 iOS 平台。
- 其余整组 PlayMode 偶发失败（2026-10-05 在 `0e4fb7c` 与新代码上对照）：`OnlinePreviewFlowTests.FailedPreviewKeepsBgmPlaying`、`SongBestScoreTests.SavedWindowShows…` 整组失败、单独通过（疑似前序测试残留状态）；`OffsetFlowTests`、`PracticeFlowTests` 中毫秒级时序断言时过时不过。均早于本次优化，未修。

#### 跨平台产品名称与图标（2026-10-04）

- `PlayerBranding.Configure()` 统一应用 OurTaikoPlay 产品名及 OurTaikoPlayer 原图 `assets/branding/icon.png`（无修改复制到 `Assets/OurTaiko/Branding/AppIcon.png`）。桌面与 iOS 全尺寸图标均绑定；Android Adaptive 沿用原项目白底／20% 内缩。Unity Build Profiles 与 OurTaiko 构建入口均自动应用。
- `IosProjectBranding` 在音频后处理之后将 iOS 工程、主 App Target、Scheme 改为 OurTaikoPlay；下一次导出前还原 Unity 内部名称以支持增量。UnityFramework／GameAssembly 和引擎文件目录不改。用户应打开 `Builds/iOS/OurTaikoPlay.xcodeproj`，Scheme 选择 OurTaikoPlay。详见 `Documentation/Building.md`。

#### 首页三项可见与选曲返回牌尺寸（2026-10-04）

- Entry 同时显示三块模式牌（演奏／练习／设置），不再按原模拟器只显示选中项及相邻一项。保留原槽位间距、9 帧滑动及打开时序，牌组整体按 `EntryView.modeSafeArea`（设计画布高度的 15%–92.5%）避让顶部与底部装饰；各牌保存的局部调整仍保留。Editor 预览采用同样布局。
- `bar_genre_back` 与 `folder_graphic` 必须以 **Single** 完整贴图导入、上下 border=56，Image 使用 Sliced；只有 `box_chara` 保留 Multiple 左右切片。Unity 自动修剪的 Multiple 子图会丢失九宫格边框，使返回牌比歌曲牌高、圆角变形。`ApplySongSelectFolders()` 可修复已有资产且保留布局。
- 验证及截图见 `Documentation/PortingNotes.md`「首页三项可见与选曲返回牌尺寸」。

#### 单次加分数字（2026-10-04）

- SinglePlayScene／PracticeScene 的 `ScoreCounter` 均已添加可编辑 `ScoreAddition` 模板；每次实际正增量显示橙色数字，复用总分数字图，按 Nijiiro `ScoreCounterAnimation` 35–39 的 446.74 ms 时间轴淡入、左移、上移错开、淡出，没有加号。初始值／不可不触发，练习清零清除旧行；连续加分独立播放、池化复用，不丢弃第六条以后的加分。
- 模板跟随总分定位和画布缩放，以子 Canvas 绘制在 JudgeCounter 上方、全局幕布下方，避免判定统计面板遮住数字。迁移 `ProjectBuilder.ApplyScoreAddition()` 只补两个场景缺少的模板，不改总分和计分逻辑。来源、动画参数与验证见 `Documentation/PortingNotes.md`「总分上方的单次加分数字」。

#### PracticeScene 练习模式（2026-10-04）

- Entry 模式顺序为演奏ゲーム／練習モード／ゲーム設定；普通与练习入口都先经过 ServerLogin，再进入 SongSelect，以连接服务器、下载歌曲和显示历史成绩。`SceneSwitcher.PracticeMode` 由 Entry 决定模式时设置并保留经过登录；SongLoadingScene 用 `SelectedPlayScene` 路由，练习返回选曲后仍选练习，普通入口重新置 false。
- PracticeScene 是通过 Editor API 完整复制 SinglePlayScene 后添加顶部 `PracticeView` 的独立场景；共享 `PlayScene` 的音符显示、输入和动画，练习逻辑在 `PlayScene.Practice.cs`。迁移 `ProjectBuilder.ApplyPracticeMode()`（OurTaiko/Apply Practice Mode）只在缺少时复制场景／添加入口，已有布局保留。
- 初始及结束后停在第一小节。播放中第一次暂停：冻结音频和谱面、清空判定计数与本轮成绩；第一项「小节进度」，左／右咔按实际小节时间前后移动（200 ms 滚动，隐藏小节线也计入）。咚进入「播放速度」，每次咔 ±0.1x，默认 1.0x、范围 0.1x–3.0x；再咚从当前位置开始。速度独立于 HS；BASS FX Tempo 保持音高，Unity 后备 pitch 随变速改变音高。鼠标／触控按钮也可操作。
- 调整层再次按暂停才打开原 SinglePlay 菜单：Resume 回到调整层，Restart 回到第一小节暂停，只有 Back 返回 SongSelect。Esc／Space 不直接退出、快捷 Restart 禁用。结束不显示 Result、不保存或上传成绩、不增加 SongsPlayed。
- **成绩处理在 ResultScene（用户决定）**：PlayScene 只生成普通游玩的 `PlayResult`，连同歌曲和回放经 SceneSwitcher 交接；ResultScene 在初始化演出前一次性领取并保存本地成绩／提交在线上传队列。练习结束直接回到第一小节暂停，不进入 ResultScene，所以不会提交。重载结算与独立预览不重复保存／上传；自动演奏仍不保存。
- 跳转用 `PlaySession.PracticeAt` 跳过历史判定、重置计数，保留游标前分支并让游标后分支重算；进入长音符中途不会补算此前的自动连打。`SongClock.Rate` 统一控制谱面进度和音乐调度／seek，保持普通游玩 Rate=1 和每帧同一判定时刻。
- 验证：EditMode 99/99＋143/143；练习 PlayMode 3/3＋直接启动 1/1，普通暂停 5/5、已完成场景回归 34/34；BASS／Unity 的 0.8x／1.2x 音画同步、1080p／720p 与鼠标操作通过，迁移两次三份场景哈希不变。报告 `TestResults/practice-*.json`。尚未实测移动设备与 Windows。
- 练习登录入口补充验证：`ServerLoginFlowTests` 6/6、`PracticeFlowTests` 4/4；模拟服务器验证 Entry→登录→历史成绩／皇冠→下载→PracticeScene，练习结束无上传且历史成绩保留。重复迁移不改变三份场景内容。
- 参考与素材来源、交互细节见 `Documentation/PortingNotes.md`「PracticeScene 练习模式」。新增测试保留在进行中目录，等待用户确认后再移入 Finished。


#### ScoreRank（2026-10-03）

- Result、选曲歌曲板、难度牌已接入 ScoreRank；七档分数门槛为 50／60／70／80／90／95／100 万。等级从 `SongScores` 的最高分派生；本地／在线成绩来源及自动演奏不保存规则不变。
- **用户明确要求只保留七个可复用图标 Prefab**，位于 `Assets/OurTaiko/Generated/ScoreRank/`，三个位置共用结果页原图并缩放。不要再导入 yellow_box 的等级／难度组合图，不按显示位置或难度生成额外 Prefab。
- 结算等级在皇冠前演出 2 秒，含原时间轴和音效；跳过直接显示最终等级、不重播音效。`ScoreRankView` 绑定保存的 Image；仅 Result 增加 `ScoreRankAnimation` 特效层。迁移 `ProjectBuilder.ApplyScoreRank()` 保留已有布局。详见 `PortingNotes.md`「ScoreRank」。

#### 游玩暂停按钮（2026-10-03）

- SinglePlay 左上角改为本地 Material Symbols `pause_circle` 圆形图标按钮（设计尺寸 48×48，位置 24,0），FPS 面板移至 x82/y2；圆内可点击，四角不响应，原暂停／恢复逻辑保持。迁移 `ProjectBuilder.ApplyCircularPauseButton()`，图标源与许可证位于 `Documentation/ThirdParty/MaterialSymbols/`。

#### SongSelect 最佳成绩与 SQLite（2026-10-03）

- 左侧最佳成绩窗口保存为 `SongSelectView.bestScore`，选曲列表中随选中歌曲显示，进入难度选择继续显示；魔王／里魔王都有记录时每秒轮播，只有一个时固定；两者都无记录才显示困难→普通→简单中最高难度的记录，全部无记录则隐藏。迁移 `ProjectBuilder.ApplySongBestScore()`，直接复用 SinglePlay 的 JudgeCounter 四行布局、面板与计分数字，上方加难度图标和最高分；四行显示该次最高分的良／可／不可／连打数，保留可编辑布局。Editor 默认可见并显示示例成绩，运行时换真实记录；难度图标切自现有 `game/lane/lane_difficulty.png`，不要用 `diff_tower_shadow` 半透明水印。
- 用户指定 `com.gilzoide.sqlite-net` Git 包 1.3.2（允许该包自带的 SQLite 原生库）。`scores.sqlite3` 中 `BestScores` 只存本地谱面，`PendingScoreUploads` 独立存已登录对应服务器的账号待上传请求。在线历史成绩只来自登录拉取和成功上传的服务器响应。旧 JSON 本地成绩与旧队列自动迁移，旧在线历史条目不导入。


#### 最新完成：Sound 设置（2026-10-03）

- 用户最新决定：GlobalSettingScene 的 Sound 显示 **Master／BGM／Track／Drum／Effects／Voice 音量组＋Output Backend**（及 Return）；音量范围 0–200%、5% 一档，确认后即时保存并生效，鼓音／语音确认时试听。设备、采样率与缓冲等高级参数仅保留在 `settings.json` 的 `audio` 中，不放回菜单。后端按平台显示 Automatic／BASS／Unity，Windows 另有 WASAPI／ASIO，WebGL 仅 Unity。
- 确认后端后保存，退出设置时通过 SceneSwitcher 淡黑后热切换，进入 Entry 前完成。未改设备参数不重建输出；切换失败恢复之前配置并留在设置显示原因。原生加载与释放共用生命周期锁，旧异步任务按 generation 失效，未领取样本统一释放。切换后端保留配置文件中的其他音频参数。
- Sound 共 8 行（含 Return），每页 4 行，支持分页／滑动／滚轮；选项弹窗仍最多显示 3 项并支持左右切换，供 Windows 的 5 种后端使用。场景控件通过 `ProjectBuilder.ApplySoundSettings()` 保存。实现 `SoundSettings.cs`、`AudioBus` 音量分组；详情与验证报告见 `PortingNotes.md`「Sound 设置」。
- **语言设置（2026-10-03）**：General › Language 保存到 `settings.json` 的 `general.language`（en／ja／zh／zh_tw／ko，默认 en）。当前只影响歌名和副标题，不翻译菜单、登录提示、类别或皮肤。名称按所选语言 → 日文（TITLEJA／TITLEJP）回退；两者都缺失才保留基础 TITLE／SUBTITLE。`SongDefinition.ReadDisplayInfo()` 供选曲、加载幕布与游玩使用，结算沿用本局显示标题；原始谱面、成绩键与上传标识不改。General 在类型列表首位，类型行距 142，使五行含 Return 均在 footer 上方；语言弹窗沿用三项可视选择与左右翻页。
- **统一时钟（2026-10-03）**：`Core/GameTimeline.cs` 集中提供每帧稳定的 `FrameTime`／`AudioFrameTime` 与音频调度用 `AudioNow`；原生后端用 Stopwatch／Frequency，Unity 后端歌曲时间保留 DSP 时钟。`GameLoop` 在输入及场景更新前采样；UI、幕布、暂停菜单统一用 FrameTime，暂停演奏不会停止 UI。`Core/SongClock.cs` 管理歌曲倒计时、暂停恢复、播放调度和 BASS 启动后 2 秒同步校正，`PlayScene` 只使用发布的 SongTime。FPS 测量用实时 Realtime；网络重试的 UTC 时间仍归网络模块。
- **HitFace／HitRing 原生时钟修复**：BASS 的 Stopwatch 时钟在同一帧内仍递增；原 Update 先取时间，再在 OnJudged 取较晚时间，ShowTime 用旧时间导致 elapsed<0，刚生成的效果立即被取消。当前由 `SongClock` 每帧发布一次歌曲时间，判定、分支回调与动画共用，直到下次更新；暂停时冻结。已删除 PlayScene 原来的临时快照和重复计时字段。真实自动演奏帧回归 `HitFeedbackClockTests` 修复前失败；旧的暂停后手动采样测试未覆盖此路径。

#### 最新完成：在线服务器与 ServerLogin（2026-10-03）

- 移植 OurTaikoPlayer `fanmade.cpp`：Entry 演奏ゲーム → **ServerLogin**（MajdataPlay Login 式，每台启用服务器 ログイン／ゲスト／スキップ／もどる）→ SongSelect。库：`System.Net.Http.HttpClient` ＋ Newtonsoft JSON（同 MajdataPlay）。代码 `Runtime/Online/`，全局 `OnlineManager`（同 SettingManager 模式）。
- 配置 `persistentDataPath/servers.json`；**内置** `https://fanmade.ourtaiko.org` 与 `https://ese-backend.llx.life`（用户要求），缺失时自动补回，停用用 `"enabled": false`。
- **分类文件夹（用户决定）**：本地歌曲之后，每台服务器的每个分类是一个文件夹（Nijiiro `bar_genre` 关闭／`folder_graphic`＋`box_chara` 打开），默认全部关闭；决定后就地展开（原 `load_current_directory` 无子文件夹分支）：文件夹板变为「もどる」、歌曲接在后面、每 10 首再插一个もどる；同时只能打开一个（打开另一个先收起当前）；もどる 或 Esc 收起并聚焦回文件夹。列表末尾另有一个根「もどる」（曲目轮循环，所以位于第一首歌上方），选它与 Esc 一样回到 Entry（用户要求；原版根目录没有 もどる）。从该文件夹的歌曲游玩回来时重新打开并聚焦该曲；每次经过 ServerLogin 全部重置为关闭。歌曲板颜色取所在文件夹。SongSelect 曲目轮按需绑定视图（保存的板固定属于本地歌曲，其余用池化 `SongBoard.prefab`／`FolderBoard.prefab`），1500 首文件夹展开时约 120 FPS。迁移 `ProjectBuilder.ApplySongSelectFolders()`（菜单 OurTaiko/Apply Song Select Folders）。
- 下载在 SongLoadingScene 幕布下进行（SHA-256 校验、缓存、`play.tja`），幕布新增 `Status` 文字；成绩经持久化幂等队列上传并附 `replay_data`。详见 `PortingNotes.md`「在线服务器与 ServerLogin」。
- 验证：EditMode `FanmadeClientTests` 14/14，PlayMode `ServerLoginFlowTests` 4/4（含文件夹开合、一次一个、每 10 首もどる、回来重开）；回归 进行中 EditMode 44/44、PlayMode 16/16，已完成 EditMode 143/143、PlayMode 34/34（报告 `TestResults/folders-*.json`）。真实服务器只做过游客只读拉取（Fanmade 26 首、ESE 2958 首解析成功），未用真实账号登录或上传。

#### 此前完成：Result 界面持久化（2026-10-03）

- Result 画面保存在 `Result.unity` 的 `Stage` 下（`ResultView` 组件），层级与原运行时构建顺序相同（Background → 标题／曲数 → Success → 成绩板与数字 → HighScore → 皇冠／评语 → 魂槽 → Nameplate → FadeIn → CoinOverlay → TouchArea），测试依赖的 `SoulSheen`／`FadeIn` 等名称不变。`ResultScene.Awake` 改为 `Bind()`：只填入本局内容（标题、曲数、难度贴图、数字、魂槽贴图、评语贴图与文字、最高分差值）并驱动动画；不再创建对象。`ResultScene` 只保留随成绩变化的贴图（难度、数字、魂槽三种难度贴图、彩虹、魂火、皇冠、光芒、评语）；背景、成绩板、标签、皇冠淡影等静态图与 `nameplatePrefab` 已删除，名牌为场景中的预制体实例。
- 动画以保存位置为基准：云与通关富士山保存在第 0 帧位置（`ResultBackground.Layers`／`Default`），运行时加时间轴位移；星群相对皇冠保存位置；最高分组相对自身保存位置；魂槽宽度只改 `sizeDelta.x`。魂槽クリア标记（Transition／Top／Bottom）与「クリア」字样按 `ResultView.gaugeArt`（默认 2＝むずかしい／おに）放置，其他难度在运行时按 `ClearCell`／`ClearCaptionX` 的差值平移。
- Inspector：选中 Result 控制器可用「结算画面／淡入遮罩」预览（示例おに过关 45 格；只改贴图、文字、显示与宽度）。迁移 `ProjectBuilder.ApplyResultLayout()`（菜单 OurTaiko/Apply Result Layout，`ProjectBuilder.ResultLayout.cs`，也由 `CreateSongSelectAndResult()` 调用），已有布局直接返回，重复执行文件哈希不变。`ApplyNameplate()` 不再处理 Result；`ApplyJudgeCounter()` 改为更新保存的 `JudgeLabels` 图片。
- 验证：PlayMode `SongSelectResultTests` 2/2、`NameplateFlowTests` 3/3、`GlobalOverlayFlowTests` 2/2、`ScoreGaugeFlowTests` 1/1；截图 `ResultFullCombo`／`ResultFailed`／`ResultGaugeFilling`／`NameplateResult`（むずかしい，验证クリア平移）与改动前一致。

#### 此前完成：Entry 界面持久化（2026-10-03）

- Entry 画面保存在 `Entry.unity` 的 `Stage` 下（`EntryView` 组件），`EntryScene.Awake` 只绑定引用、赋回调，运行时只改透明度、帧、缩放与模式板滑动偏移；不再在运行时创建任何对象。`EntryScene` 上的建场景用 Sprite 字段与 `nameplatePrefab` 已删除（名牌为场景中的预制体实例＋CanvasGroup）。
- 层级：Background（街景、4 闪光、2 灯笼光、街灯）→ TouchArea → ModeBoards（每块板 Cursor／Closed／Open／Info／Title／TitleOpen／TitleClosed 标记／Flash／Hit）→ Credit → ControlGuide（ClipSampler）→ Nameplate → Timer（60 占位）→ StatusChips → CoinOverlay。
- 模式板保存在首次布局：第一块打开居中，其余关闭在 `mode_list` 槽位（ゲーム設定 在 +50,+305）；运行时以「保存位置 − 初始槽位」为基准加滑动偏移，所以在 Inspector 中拖动板会整体保留。标题在 `TitleClosed` 与 `TitleOpen` 两个空标记之间随打开程度插值；点击区大小为 `EntryView.BoardView.closedHitSize`／`openHitSize`；板的场景名、标题与说明文字均在场景中编辑（`EntryScene.Modes` 由保存的标题与 `scene` 生成）。
- 共享类新增绑定构造：`ControlGuideView(Image, art)`、`ArcadeTimerView(art, params Image[][] rows)`、`CoinOverlayView(art, freePlay, qrChip, bubble, player, message)`；运行时 `StatusChips` 已删除（改为 Editor 生成）。
- Inspector：选中 Entry 控制器可用「投币画面／模式选择」预览（只切换显示与透明度，会把场景标为已修改）。迁移入口仍为 `ProjectBuilder.CreateEntryScene()`：仅在没有 `EntryView` 时生成层级（`ProjectBuilder.EntryLayout.cs`），已有布局只重绑时间轴、音效与 overlay 美术；连续执行文件哈希不变。
- 验证：EditMode `ControlGuideClipTests` 2/2；PlayMode `EntryFlowTests` 1/1、`EntryTouchFlowTests` 1/1、`GlobalSettingFlowTests` 3/3、`GlobalOverlayFlowTests` 2/2；截图 `TestResults/EntryCredit.png`、`EntryModeSelect.png` 与改动前一致。

#### 最新完成：GlobalSettingScene 与 SettingManager（2026-10-02）

- **入口**：Entry 模式列表改为街机 `box.lua` 的多板列表——演奏ゲーム（选中、居中打开）与其下方关闭的ゲーム設定（`mode_select/box` 9／10 帧、蓝色 (0,132,212) 标题边、skin_config `entry_settings_comment_1/2`）。左咔上移、右咔下移，两端夹住（原 `BoxManager::move_left/right`）；列表 9 帧线性滑动（`mode_list.txt` 的 kanban 槽位），新选中板滑动结束后才 select_on，旧板 select_off；首次出现时关闭的板从 3 格外飞入。选ゲーム設定后切到 `SceneSwitcher.SettingScene`（GlobalSettingScene，Build Settings 第 2 位）。**Entry 触控**（用户要求，原为全屏点击一律当咚）：投币画面点任意处加入；模式列表时点其他板移动到该板、点打开的板决定（同 SongSelect），点空白无反应，纵向滑动（`SwipeRelay`，约 200 px 一格，上滑＝右咔）移动；每块板的透明点击区 `EntryModeBoard.Hit` 随打开程度在可见板面 964×157（关）与 1050×436（开）之间插值，全屏 `TouchArea` 放在板列表之下。测试 `EntryTouchFlowTests`（已完成，用 EventSystem 实际射线检查点击归属）。`EntryModeList`／`EntryModeBoard`（`EntryViews.cs`），`EntryFlow.MoveMode`／`SelectedMode`。
- **设置存储**：`Core/GameSettings.cs`（`settings.json`，`JsonUtility.FromJsonOverwrite`，缺字段取默认）＋全局 `Scenes/SettingManager.cs`（同 `PlayerInfoController`：首场景前自动创建、跨场景保留、每次修改立即写盘、`Changed` 事件、测试用 `UseUnsaved`，`TestData.Use` 已接入）。当前设置：Play › Enable Drumpad for Single Player Mode（`play.singlePlayerDrumPad`，默认 true）；Display › Target Frame Rate（`display.targetFrameRate`，120／60／-1＝Unlimited，默认 120，未知值按 120；移动端 Unlimited 实际设 1000，因为 -1 在移动端是平台默认 30／60）；Display › VSync（`display.vSync`，默认 false）。选项弹窗加宽到 1290、选项间距 380，并把按钮组连同右侧箭头的位置一起居中，三个选项时箭头不压按钮。
- **菜单逻辑**：`Core/SettingsMenu.cs`（纯 C#）：焦点 Types→Items→Choice；咔在当前列表移动（循环），咚确认；类型列表与项目列表末尾各有 Return（类型的 Return 返回 Entry，项目的 Return 回到类型）；选项列表咚即应用并回到项目；Esc 逐级后退。触控：点其他行移动、点已聚焦行确认（同 SongSelect 曲目板），点选项按钮直接应用；两列表另支持纵向滑动（`SwipeRelay`，SongSelect 本身只有点击、没有滑动）。选项标签沿用原 settings_template 的 Enabled／Disabled。
- **场景**：`Assets/Scenes/GlobalSettingScene.unity` 保存可编辑层级（`GlobalSettingScene` 控制器＋`GlobalSettingView`）：左侧类型行、右侧项目行（标签自动缩小以免压到当前值）、选项弹窗（用户决定：名称、说明、选项按钮，只在焦点进入选项时出现，居中于半透明黑色 `PopupShade` 之上；浏览类型与项目时隐藏；点遮罩不改值关闭、同 Esc）、`blue_arrow` 指向焦点（在弹窗之上）、底部 footer、右下 FPS。美术来自 PyTaikoGreen `Graphics/settings`（Nijiiro 无自有设置美术，继承 Green），1.5 倍绘制，九宫格；BGM `Audio/settings/bgm.ogg`。行 0 为布局基准，行距在 `GlobalSettingView` 可调，多出的行运行时复制。迁移 `ProjectBuilder.CreateGlobalSettingScene()`（菜单 OurTaiko/Create Global Setting Scene，仅缺失时创建，已有布局只重绑美术／音效，并给 SinglePlayScene 的 `PlayScene.drumPad` 赋值）；Entry 重绑用 `CreateEntryScene()`。两者重复执行文件哈希不变。
- **游玩**：`PlayScene.Start` 按设置 `drumPad.gameObject.SetActive(...)`——关闭时触控鼓既不绘制也不向 InputManager 注册；暂停恢复只重新启用自己禁用的鼓，不会把它打开。
- 测试（已完成，2026-10-02 用户确认后移入 Finished）：EditMode `SettingsMenuTests` 5/5，PlayMode `GlobalSettingFlowTests` 3/3（Entry 列表滑动／打开时序、进入设置、咚咔与触控全流程、设置写入 SettingManager、触控鼓开／关）；`EntryFlowTests` 的咔断言改为左咔在顶端不动。全部程序集：进行中 EditMode 35/35、PlayMode 15/15，已完成 EditMode 136/136、PlayMode 30/30，报告 `TestResults/settings-*.json`，截图 `TestResults/EntrySettingsBoard.png`、`SettingsTypes.png`、`SettingsChoice.png`、`SinglePlayNoDrumPad.png`。
- **添加设置**：在 `SettingsMenu.Catalog()` 中加 `SettingItem`（布尔用 `SettingItem.Toggle`，选项标签 Enabled／Disabled），并在 `GameSettings` 对应分区加带默认值的字段；新类型加 `SettingType` 与新的 `[Serializable]` 分区。视图会自动复制行。**布局**（2026-10-03 Sound 设置更新）：右侧列表每页 4 行，随焦点滚动，支持滚轮、滑动和翻页按钮；选择弹窗最多显示相邻 3 项，并支持左右按钮切换。详情已改为选项弹窗，不再占用右侧。离开设置回到 Entry 时从投币画面重新开始（与原版一致）。
- 注意：Unity 6 默认把新复制的 PNG 自动切成 Multiple 修剪 sprite；新增美术必须显式设为 Single（`ImportSettingArt`、`ImportEntryArt` 已处理）。

#### 此前完成：判定计数器（2026-10-02）

`JudgeCounter`（`Play/JudgeCounterView.cs`，Viewport 中 `ComboAnnounce` 之后，左上 1P 名牌区上方 (29,50) 352×212）：按用户参考截图（街机 Nijiiro 的精简判定计数）制作，**不是**原 `judge_counter.cpp` 的百分比版（Nijiiro 无该皮肤，PyTaikoGreen 版在轨道下方且带百分比），不要还原。半透明橙色面板与白色半透明行条由 `ProjectBuilder.GenerateJudgeCounterArt()` 生成（alpha 写在 PNG 中，`Art/game/judge_counter/panel.png`、`bar.png`）；良／可／不可／連打数 标签切自结算的 `result/score/max_combo_ja`（该图因此改为 Multiple，Result 的 `judgeLabels` 改用整图切片 `ResultJudgeLabels`）；数字用分数计数器的 `score_number`（用户要求同字体），右对齐、高 38。FPS 读数移到 y 2 以免压住 連打数 行（用户选择左上位置时已知）。层级保存在场景可编辑；迁移 `ProjectBuilder.ApplyJudgeCounter()`（可重复执行，文件哈希不变）。测试 `JudgeCounterFlowTests`（进行中 PlayMode）。标签水平居中于行条。调试文字 `JudgmentCounters`（GOOD/OK/BAD/ROLL）与 `PlayScene.counters` 已删除（用户决定，由本计数器取代）；调试文字 `RollCounter`（连打时「DRUMROLL n」、9 号彩球「BALLOON n」）与 `PlayScene.rollCounter` 同样删除，彩球目前没有剩余次数显示（原版 kusudama 演出未移植）。

#### 此前完成：游玩连击数（2026-10-02）

`NoteLane/Combo`（`Play/ComboView.cs`）改用 Nijiiro `game/combo` 贴图，居中于鼓面：连击 < 10 隐藏（**用户决定**，即 Nijiiro `combo_min`；OurTaikoPlayer 代码写死 3，不要还原），10–49 白、50–99 银、≥100 金（`combo_100_ja`＋`ComboGlimmer.anim` 闪光）。层级保存在场景中可编辑；迁移 `ProjectBuilder.ApplyCombo()`。测试 `ComboFlowTests`（Finished PlayMode）与 `ComboGlimmerClipTests`（Finished EditMode），2026-10-02 用户确认完成后移入 Finished。详见 `PortingNotes.md`「游玩连击数」。每 100 连击的卷轴提示（`Play/ComboAnnounceView.cs`，Viewport 中 `GaugeHitEffect` 之后，`ComboAnnounce.anim` 淡入淡出）与 1P 连击语音 100–5000（`Audio/combo`）已移植，迁移 `ProjectBuilder.ApplyComboAnnounce()`，见 `PortingNotes.md`「连击提示与语音」。

#### 此前完成：SongSelect 界面持久化（2026-10-02）

**范围**：SongSelect 与演奏设置菜单保存为场景层级／Prefab，运行时代码只更新内容、输入和动画。Entry 与 Result 当时仍在运行时构建（Entry 已于 2026-10-03 持久化，见上）。暂停菜单 `ad16cb9` 与删除 SampleScene `b48f531` 在此之前提交。

- `SongSelectScene.cs` 不再在运行时构建 UI，改为绑定 `public SongSelectView view`；当前 3 首歌使用场景中保存的实例，新增歌曲时才 Instantiate `Generated/SongBoard.prefab`。视图脚本在 Runtime/Scenes：`SongSelectView`、`SongBoardView`、`OptionPanelView`、`SongSelectOverlayView`、独立的 `PointerRelay`（点击回调仍在 Awake 绑定）。
- `OptionPanel` 构造为 `new OptionPanel(OptionPanelView view, OptionPanelArt art)`，只更新已有对象；`GlobalOverlays` 新增绑定保存对象的构造函数（Entry 与 Result 现在也使用绑定构造）。
- Editor：`ProjectBuilder.SongSelectLayout.cs`、`ProjectBuilder.OptionPanelView.cs`、`ProjectBuilder.SongSelectOverlays.cs`、`SongSelectSceneEditor.cs`。迁移菜单 **OurTaiko/Apply Song Select Layout**（`ProjectBuilder.ApplySongSelectLayout()`）只处理尚无 view 的场景，已有布局直接返回。文字共用 `SkinUi` 的两种材质（`Generated/SongSelectMaterials` 已删除）。
- 动画以保存的 RectTransform 为基线施加偏移，保留 Inspector 调整。曲目轮整体参数位于 `SongSelectView`；`SongBoardView.authoredWheelPosition` 与 `PlateView.authoredContentX` 保存生成时的参考位置，使整体参数、换歌及 TJA 增删难度都能与单板／单牌的微调共存。
- Inspector 的「选曲列表／难度选择／演奏选项」预览只切换显示：不播放音频、不创建全局控制器、不读写玩家设置（因此编辑态名牌没有玩家名，运行时才填入）。预览会把场景标记为已修改；如不想保存预览状态，重新打开场景即可。进入 Play 会恢复正常列表状态。
- 验证（最终代码与最终资产）：EditMode `SongSelectSavedAssetTests` 2/2（`TestResults/song-select-saved-asset-final-editmode.json`）；名称含 SongSelect 的 PlayMode 测试 8/8，92 秒，覆盖 Entry→选曲→难度→游玩→暂停→结算→返回、全局元素、名牌与 `SongSelectSavedLayoutTests`（`TestResults/song-select-flow-final-playmode.json`）。更早的 `song-select-saved-layout-playmode.json` 是已修复的箭头浮点比较失败，不是最终结果。
- 现场验证：`ApplySongSelectLayout()` 连续执行两次，场景与两个 Prefab 的文件哈希不变、对象数不变。编辑态三种预览分别离屏渲染为 `TestResults/SongSelectEditPreviewList.png`、`SongSelectEditPreviewCourse.png`、`SongSelectEditPreviewOptions.png`；`PlayOptions.prefab`（84 个对象）与 `SongBoard.prefab`（37 个对象）用 `LoadPrefabContents` 加载，无 Missing Script。编辑态额外出现的 `TMP SubMeshUI` 对象是 `DontSave` 的多图集子网格，不会写入场景。独立 Player 未重新构建。
- 工具提醒：编辑态 `capture_game_view` 拿到的是旧帧或全黑，且 `save_path` 会写到 `Assets/` 下（用完需用 `AssetDatabase.DeleteAsset` 删除）。编辑态截图应把 Overlay Canvas 临时切到相机模式，离屏渲染后再还原。`Generated/Nijiiro SDF.asset` 与 `Resources/Nijiiro UI SDF.asset` 是 Unity 自动修改的，不要提交；Editor 打开期间也不要还原。

#### 更早已完成状态（历史记录）

- 本次新增：游玩暂停菜单，详见下文；此前最近代码提交 `45feb88`（游玩名牌 SDF 缩放修复）。更早交接记录：`31c6d06` — `test: fix the flaky loading-curtain timing and drop the yellow LOADING text`（2026-10-02，见下文「最新验证」）。其前为 `1c0e2d4`（Entry 测试移入 Finished）、`1bc4c8b`（删除不再引用的计时器红区图片 `bg_red`／`counter_white`／`highlight`）、`75b09fb` — `refactor(overlays): delete the unused timer countdown`（删除 `ArcadeTimer` 倒数代码与白色数字切片）。其前为 `8d90d76` — `feat(entry): make the Entry timer a placeholder that never counts down`（2026-10-02）。其前为 `6a2bb66` — `feat(scenes): show the global arcade overlays on SongSelect and Result`（2026-10-02，见 `Documentation/PortingNotes.md`「SongSelect 与 Result 的全局元素」）。其前为 `df58cb5` — `feat(scenes): port the Nijiiro Entry scene with global arcade overlays`（2026-10-02，见下文「Entry」）。其前为 `abd46ba`（测试分为进行中／已完成程序集）与 `13f2b80` — `refactor(scenes): remove Test_DefaultScene and start from SongSelect`（2026-10-02，删除测试入口，SongSelect 为首个场景，见 `Documentation/PortingNotes.md`「删除 Test_DefaultScene」）。其前为 `a067014` — `feat(ui): add Nijiiro nameplate, PlayerInfoController and score counter`（2026-10-02，名牌、全局 `PlayerInfoController` 与游玩分数计数器，见下文「名牌与分数计数器」），随后的 docs 提交补全本文。其前为 `222f8f2` — `feat(play): fly hit notes to the soul badge with gauge hit effect`（见下文「音符飞向魂槽与 GaugeHitEffect」），随后的 docs 提交补全本文。再前为 `6a78a04` — `feat(scenes): add Nijiiro song loading curtain and SongLoadingScene`（见下文「选曲加载幕布」）。其前为 `feat(play): port note moji lane`（音符文字，见下文「音符文字（moji）」）。其前为音符显隐／层级修复 `dfd5e56`、`5faadfe`、`d6cd16b`、`0e2cbe5`（见下文「音符显隐与层级」）。更早为 **`b445ac3` — `feat(unity): add Nijiiro song select and result scenes`**，随后 `docs: update handoff for song select and result scenes` 更新本文（之前依次为 Shinuchi 计分／魂槽修复与 `a34ee00` 全局 SceneSwitcher）。交接时工作区干净。
- **UI 字体源**为 `Art/DDFont.ttf`（2026-10-02 用户提供，取代 Nijiiro `Taiko.ttf`），见 `PortingNotes.md`「源字体替换」与「单一字体与黑色描边」。
- `Assets/OurTaiko/Resources/Nijiiro UI SDF.asset` 是动态 SDF 字体（2026-10-03 前此条针对已删除的 `Generated/Nijiiro SDF.asset`）。**提交前**在 Editor 中执行 `ProjectBuilder.RegenerateUiFontAtlas()`（菜单 OurTaiko/Regenerate UI Font Atlas）：清空图集（不留种子字符与额外图集页，字形全部运行时按需生成），去掉 Editor／测试运行时加入的字形，结果只取决于 `DDFont.ttf` 且每次相同，可直接提交；Editor 重绘打开的场景时会再次加字形，所以执行后立即提交（用户要求，2026-10-03）。以下为不通过该入口时的旧规则：Unity 会在打开项目、运行测试或保存时自动改写它（用户确认属于 Unity 自身行为，并非用户修改）。出现该 diff 时**不要提交**；只在 **Editor 关闭后**才用 `git restore "Assets/OurTaiko/Resources/Nijiiro UI SDF.asset"` 还原；Editor 打开时**绝不还原**：字体开启多图集，运行中新增的字形可能已写到第 2 页图集，文件被还原成只有 1 页后，场景里已有的 TMP 文字仍引用第 2 页，`TMP_MaterialManager.GetFallbackMaterial` 抛 `IndexOutOfRangeException`（2026-10-02 Entry 移植时发生过）。Editor 打开期间只需在提交时排除该文件。不要用旧的 TestResults 快照覆盖它。
- Unity Editor 可能仍由上一会话打开（项目已安装 Pipeline 包）；先用 `unity status` 确认连接再操作，修改 C# 后刷新并确认 `EditorUtility.scriptCompilationFailed` 为 false（编译错误会让 CLI 无法连接或静默失败，看 `~/Library/Logs/Unity/Editor.log` 的 `error CS`）。耗时较长的 Editor 方法会让 CLI 报 5 秒超时，但会在 Editor 中继续执行，需轮询结果。Editor 未运行时用 `unity open <项目路径>` 启动并轮询 `unity status` 到 ready；关闭用 `unity projects close <项目路径>`（不保存，先确认没有未保存场景）。zsh 不会对未加引号的 `$var` 分词，多参数 CLI 调用写成 bash 脚本。
- 若 Editor 报「assets located in immutable packages were unexpectedly altered」：这是 `Library/PackageCache` 中包文件（2026-10-02 为 `com.unity.render-pipelines.core` 的 LookDev 图标 .meta）被改写，与项目文件无关。修复：关闭 Editor，删除该包的 `Library/PackageCache/<包名>@<hash>` 目录，重开后 Package Manager **不会自动**补回（会出现大量 URP／Shader Graph 的 `error CS`），需在 Editor 中执行 `UnityEditor.PackageManager.Client.Resolve()` 重新解析，等待目录恢复并重新编译。
- 选曲／结算的下一步候选（均未授权，需用户确认）：文件夹与类别、成绩等级演出、曲目板飞入、难度决定标记弹出、皇冠光芒加算混合、支持字母扩展音符以游玩 TRIPLE HELIX Edit。
- **SongSelect 已保存为可编辑层级**：`SinglePlayScene` 与 `SongSelect` 的界面均持久化；选曲场景保存曲目板、4 张难度卡、演奏选项、名牌与全局覆盖层，`SongSelectScene.Awake` 只绑定引用，内容与动画在运行时更新。`Generated/SongBoard.prefab` 为新增歌曲模板，`Generated/PlayOptions.prefab` 可独立打开编辑；选择场景控制器时可用 Inspector 的列表／难度／选项预览。动画以保存的 RectTransform 为基线，曲目轮布局参数位于 `SongSelectView`。迁移入口 `ProjectBuilder.ApplySongSelectLayout()`，已有布局不会被重建。Entry 与 Result 也已持久化（见「Entry 界面持久化」「Result 界面持久化」），SongLoadingScene 的画面仍来自 SceneSwitcher 幕布。
- 当前验证针对 Unity Editor。早期曾成功构建 macOS Development Player，但 `Builds/OurTaikoPlayerUnity.app` **没有随最近各次修复重新打包**，不能视作当前版本。移动端、真机音频延迟与独立播放器长期手动游玩尚未验收。

### 动画剪辑（Generated/Clips，2026-10-02）

固定时间轴、与游戏状态无关的表现存为 `.anim`，由 `Runtime/Scenes/ClipSampler.cs` 播放：Animator＋手动求值的 Playables 图（`AnimationClip.SampleAnimation` 不会应用 sprite 关键帧），各视图仍持有自己的时钟（歌曲时钟、真实时间、重新开始）并每帧调用 `Sample`／`SampleLoop`／`Play`（切换变体）。剪辑由 Editor 构建（`ProjectBuilder.Clips.cs` 的 `SaveClip` 原地重写，GUID 不变），各项迁移菜单可重复执行。测试 `AnimationClipTests`（进行中）直接采样资产。
剪辑帧不再是 `Generated` 中的独立 sprite 资产：操作指引、判定外圈（`outer_*`）、魂槽彩虹（`game/gauge/rainbow_*`）、名牌彩虹带（`frame_top_rainbow`）与魂徽章光圈（`game/gauge/hit_effect`）的帧都在各自 PNG 的导入设置中切片（Multiple 模式，`ProjectBuilder.SliceSheet`，按名称保留 spriteID，名称沿用旧资产名）。2026-10-02 由 `ProjectBuilder.ApplyFrameSheets()` 迁移并删除 49 个旧切片资产；随后 `ProjectBuilder.MoveSlicesIntoSheets()` 把其余 210 个 `Slice()` 生成的 sprite 资产（音符图集、文字、各类数字、段位、星级、结算彩虹等，共 21 张图）同样移入 PNG 导入设置并删除，`Generated` 中不再有 sprite 资产；`Slice()` 现在直接在图的导入设置中切片，各导入步骤不会把 Multiple 模式的图改回 Single。原 PNG 必须保留（剪辑只引用 sprite，不含图像数据）。同日删除无人引用的 `game/gauge/hit_effect_circle(_big).png`（Nijiiro 透明占位图）与 `game/hit_effect/outer_effect_good/ok.png`（按「从 Green 补齐」规则复制、原版代码从未加载），并从 `ImportedAssets.json` 移除。
- `ControlGuide.anim`：操作指引决定循环，见「Entry」。
- `Dancer.anim`：游玩舞者 `0_loop` 19 帧、8 fps 循环、歌曲时钟（取代已删除的 `SpriteFlipbook`）；`PlayScene.dancers` 为 `ClipSampler[]`。迁移 `ProjectBuilder.ApplyDancerClip()`。
- `DrumFlash.anim`：鼓面闪光（`m_Enabled` 亮 0.12 s），四个鼓面各自从自己的击打计时（原为最后一次击打后统一熄灭；原版每次击打也是独立的 `DrumHitEffect`），真实时间。迁移 `ProjectBuilder.ApplyDrumFlashClip()`。
- `JudgmentFade.anim`：判定文字（良／可／不可）0.25 s 线性淡出（只写 `m_Color.a`，图片 RGB 为白），真实时间，每次判定（含连打击打）重新开始。迁移 `ProjectBuilder.ApplyJudgmentFadeClip()`。
- `GogoPulse.anim`：GOGO 轨道着色 `CanvasGroup.m_Alpha` = 0.18 + 0.05 sin(12t)，一个周期（2π/12 s）循环，24 段 Hermite 键带精确斜率（误差 < 1e-6），歌曲时钟（负时间按周期取模）；非 GOGO 时由代码置 0。迁移 `ProjectBuilder.ApplyGogoPulseClip()`。
- `HitFace.anim`：判定点笑脸，透明度（动画 28）与 350 ms 时关闭的 `m_Enabled`；变体贴图仍由 `HitFaceView` 选择。`HitFace.prefab` 带 Animator＋ClipSampler；`ApplyHitEffects()` 会补上。
- `HitRingGood／Ok／GoodBig／OkBig.anim`：判定点外圈，`outer_*` 四帧（动画 30）、透明度（动画 27）与 200 ms 时关闭的 `m_Enabled`；`HitRingView` 按判定与大小切换剪辑（`ClipSampler.clip`）。
- `GaugeHitEffect.anim`：魂徽章 GaugeHitEffect（动画 2／32／33）：Burst 三帧、尺寸 0.8→1.5、按尺寸阶梯着色黄→橙→红、Burst 与 Note 的 300 ms 后 83 ms 淡出及 383 ms 时关闭；位置仍由代码（`GaugeHitEffectLayout`）。剪辑挂在 `GaugeHitEffect` 层，取代 `GaugeHitEffectTiming` 及其测试；迁移 `ProjectBuilder.ApplyNoteArcs()`。
- `SoulFire.anim`／`SoulOverlay.anim`：满槽魂火 8 帧×50 ms 循环与 `tamashii_overlay` 在第 0、1、4、5 帧显示，分别挂在 `SoulFire`、`SoulOverlay` 上（两者之间隔着 `Soul`，不重组层级），共用歌曲时钟；未满时由代码隐藏。迁移 `ProjectBuilder.ApplySoulFireClips()`。
- `SoulRainbowEasy／Normal／Hard.anim`：满槽彩虹，cell k 上叠 cell k+1 在 75 ms 内淡入，整体在 450 ms 内淡入。剪辑 1.2 s＝0.6 s 开场（淡入与交叉淡入的乘积每 5 ms 烘焙一键）＋0.6 s 循环，`SoulGaugeView` 在开场后只重复后半段。RainbowA／B 归入全尺寸的 `Rainbow` 组（位置不变），`SoulGaugeView.rainbowSampler` 按难度样式（`Style.rainbow`）切换剪辑。迁移 `ProjectBuilder.ApplySoulRainbowClips()`。
- `CellFade.anim`：魂槽新增格子 450 ms 线性淡入（只写透明度）；哪一格、贴图与位置仍由 `SoulGaugeView` 决定。迁移 `ProjectBuilder.ApplyCellFadeClip()`。
- `NameplateRainbow.anim`：名牌彩虹称号带（全局动画 12），`Band` 6 帧×50 ms、300 ms 循环，`BandUnder` 从第 1 帧起显示前一帧；真实时间，只在 `rainbow` 称号时采样。`Nameplate.prefab` 根物体带 Animator＋ClipSampler，`NameplateLayout.RainbowFrame` 已删除。迁移 `ProjectBuilder.ApplyNameplateRainbowClip()`。
- `BranchChange.anim`：分支切换（Nijiiro ID 41–45）：旧标签 100 ms 缓出推移 30 px、再与新标签 133 ms 缓出滑动 105 px 并交叉淡入淡出，背景淡到 0.5，等级徽章 116＋116 ms 放大到 1.2 再回、1276–1392 ms 淡出。剪辑写入 `BranchLaneView` 的 `previousOffset`／`currentOffset`（相对保存位置的偏移，代码按升降方向取正负），因此 Inspector 中调整的标签位置仍有效；缓出段用两键 Hermite 精确表示二次曲线。迁移 `ProjectBuilder.ApplyBranchChangeClip()`。
- `DrumSqueeze.anim`：触控鼓按下（全局动画 66）70 ms 缓出缩至 0.95、70 ms 缓出回 1，挂在 `TouchDrum/Drum` 上，真实时间、每次按下重新开始；禁用时 `DrumPad` 直接复原比例。迁移 `ProjectBuilder.ApplyDrumSqueezeClip()`。
- `TextStretch.anim`：分数计数器与气球计数数字的 TextStretch（id 4／6）：每整毫秒 +0.2 px 升到 50 ms 的 12，再每 16.57 ms 减 2（末两步按原式到 -2、-4），166 ms 后为 0。剪辑写入同物体的 `AnimatedFloat.value`，`ScoreCounterView`／`BalloonCounterView` 读取后排列数字；`TextStretch` 静态类已删除。迁移 `ProjectBuilder.ApplyTextStretchClip()`。
- `BalloonPop.anim`：气球吹爆后的数字伸长加整个计数器 166 ms 淡出（`CanvasGroup.m_Alpha`）；`BalloonCounterView` 未爆时用 `stretchClip`（TextStretch）、吹爆后切换到 `popClip`，166 ms 后仍由代码移除。迁移 `ProjectBuilder.ApplyBalloonPopClip()`。
- 不转换：Lumen 时间轴（`Animations/*.txt`，原版导出原样副本，Entry／选曲／加载幕布／结算）、场景切换与暂停菜单淡入淡出（从当前不透明度插值，可中断）、由游戏状态驱动的表现（音符滚动与飞行、气球膨胀、魂槽填充）。

### 游玩暂停菜单（2026-10-02）

- `Play/PauseMenuView.cs` 与 `PlayScene` 管理三项菜单、真实时间各 0.5 秒淡入／淡出；外部 Restart／Back 已移入菜单。菜单位于主 Canvas 最后层，关闭结束才恢复歌曲、判定和 DrumPad；Restart／Back 同样先淡出，Back 固定到 SongSelect。
- 复用 Nijiiro `dan_select/confirm_box` 的青海波樱花背景和金橙按钮，保留原图，Sprite 九宫格布局。迁移入口 `ProjectBuilder.ApplyPauseMenu()`；首次生成同样配置菜单。`PausePanel` 现在直接位于主 Canvas，触控鼓设计区应取 `pauseButton.transform.parent`，不能再取 `pausePanel.transform.parent`。
- 暂停打开和恢复当帧隔离残留输入，恢复淡出中失焦会保持暂停。测试在进行中 `PauseMenuFlowTests`，用户确认完成前不移入 Finished。
- 验证：暂停菜单专项 5/5，进行中 PlayMode 程序集 10/10，Finished PlayMode 回归 28/28；真实 Game 画面和 16:9／4:3 布局通过，截图 `Documentation/PauseMenu.png`。未重新构建独立播放器。

### 运行入口与代码结构

下列路径均相对于本项目根目录。

| 核心文件／目录 | 当前职责 |
| --- | --- |
| `Assets/Scenes/Entry.unity`、`Runtime/Scenes/EntryScene.cs`、`EntryViews.cs`、`Runtime/Core/EntryFlow.cs` | Nijiiro Entry（街机投币模式）：街景背景、「１人プレイ／２人プレイ 太鼓をたたいてスタート！」两行、1P 加入后名牌与操作指引淡入、演奏ゲーム 模式板（`mode_board` 时间轴）、决定后进入 SongSelect。画面保存在场景中（`EntryView.cs`），`Awake` 只绑定；Inspector 有投币／模式选择预览（`Editor/EntrySceneEditor.cs`）。迁移入口 `ProjectBuilder.CreateEntryScene()`（菜单 OurTaiko/Create Entry Scene，仅缺少布局时生成，`ProjectBuilder.EntryLayout.cs`）。 |
| `Runtime/Scenes/GlobalOverlays.cs` | 全局街机界面元素：计时器（`ArcadeTimerView`，占位，只显示固定数字）、左上操作指引（`global/indicator` 决定循环）、フリープレイ／QR 芯片／2P 邀请云（`coin_overlay`）、段位道場／1プレイ4曲／IC Card 状态芯片（`entry_overlay`）。Entry 全部显示；SongSelect 显示计时器占位（列表 100、难度选择 60，不倒数）、QR 芯片与 2P 邀请云（已玩曲数 < 2）；Result 只显示フリープレイ（在 FadeIn 之上）。迁移入口 `ProjectBuilder.ApplyGlobalOverlays()`。 |
| `Assets/Scenes/GlobalSettingScene.unity`、`Runtime/Scenes/GlobalSettingScene.cs`、`GlobalSettingView.cs`、`SettingManager.cs`、`SwipeRelay.cs`、`Runtime/Core/GameSettings.cs`、`SettingsMenu.cs` | 全局设置：Entry 的ゲーム設定板进入；类型（Play）／项目／选项三级焦点，咚咔、Esc 与触控（点击＋纵向滑动）操作，Return 项返回；`SettingManager` 读写 `persistentDataPath/settings.json`。当前设置：Play › Enable Drumpad for Single Player Mode（默认开，控制 SinglePlayScene 触控鼓启用与显示）。PyTaikoGreen 设置美术。迁移 `ProjectBuilder.CreateGlobalSettingScene()`。 |
| `Assets/Scenes/SongSelect.unity`、`Runtime/Scenes/SongSelectScene.cs` | Nijiiro 纵向曲目板、展开／收起时间轴、试听与 BGM、难度面板、裏切换；扳手按钮打开演奏オプション。光标规则 `Core/DifficultyCursor.cs`，谱面信息 `Core/SongInfo.cs`。 |
| `Runtime/Core/PlayOptions.cs`、`OptionMenu.cs`、`Runtime/Scenes/OptionPanel.cs` | 演奏オプション：设置与 `options.json` 持久化、速度档位、`ChartModifiers`（あべこべ／ランダム／はやさ／ドロン）；7 行面板逻辑与滑入滑出；Nijiiro 面板绘制和触控区。游玩侧 `Play/HitSoundLibrary.cs`（`Generated/HitSounds.asset`，21 套音色）与 `Play/ModifierBadgeView.cs`（轨道徽章）。迁移入口 `ProjectBuilder.ApplyPlayOptions()`。 |
| `Assets/Scenes/Result.unity`、`Runtime/Scenes/ResultScene.cs`、`ResultView.cs`、`ResultBackground.cs` | Nijiiro 结算背景、成绩板、魂槽填充、皇冠、评语、最高分条；时间轴 `Core/ResultSequence.cs`，数据 `Core/PlayResult.cs`，本地最佳成绩 `Core/ScoreStore.cs`。画面保存在场景中（`ResultView.cs`），`Awake` 只绑定并填入本局内容；Inspector 预览 `Editor/ResultSceneEditor.cs`，迁移 `ProjectBuilder.ApplyResultLayout()`。 |
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
| `Assets/OurTaiko/Runtime/Play/PlayScene.cs` | 输入与玩法驱动；由 SongClock 提供歌曲时间、AudioPlayback 调度音乐；暂停恢复、判定反馈、气球破裂音效、音符／身体／尾部渲染和结果显示。 |
| `Assets/OurTaiko/Runtime/Input/InputManager.cs` | 全局输入入口（参照 MajdataPlay `IO/InputManager`）：逻辑键 `InputKey` 与物理键绑定，监听 Input System 事件保留同帧按键先后顺序，由隐藏的 `GameLoop`（执行顺序 -32000，先更新时间快照，再发布输入）每帧在所有场景脚本前发布 `PressesThisFrame`／`GetKeyDown`。场景脚本不得直接读 `Keyboard.current`；触控鼓 `DrumPad` 启用时向 InputManager 注册，由同一次每帧更新对触摸与鼠标做命中检测，与键盘按下按时间顺序合并，当帧生效。 |
| `Assets/OurTaiko/Runtime/Play/BranchLaneView.cs` | 分支轨道色、右侧字样、升降级和过渡动画。 |
| `Assets/OurTaiko/Runtime/Play/SoulGaugeView.cs` | 50 格魂槽、过关黄色区、新格淡入、满槽彩虹与魂火。 |
| `Assets/OurTaiko/Runtime/Core/NoteArcPath.cs`、`GaugeHitEffectLayout.cs`、`Generated/Clips/GaugeHitEffect.anim`、`Runtime/Play/NoteArcView.cs`、`GaugeHitEffectView.cs` | 命中音符飞向魂徽章：Nijiiro `note_arc_pivot` 圆弧、30 帧；到达后在徽章播放 GaugeHitEffect（光圈换帧、0.8→1.5 放大、黄→橙→红、383 ms 淡出，音符同步淡出）。`NoteArcs`、`GaugeHitEffect` 层依次在 `SoulGauge` 之后。迁移入口 `ProjectBuilder.ApplyNoteArcs()`。 |
| `Runtime/Core/PlayerInfo.cs`、`NameplateLayout.cs`、`Runtime/Scenes/PlayerInfoController.cs`、`NameplateView.cs`、`Generated/Nameplate.prefab` | 玩家名牌：数据与规则（coin／称号／段位、名字框、彩虹帧）、全局持有者（读 `player.json`，`Changed` 事件更新名牌）、Nijiiro 名牌预制体。SinglePlayScene、SongSelect、Entry 与 Result 均保存实例。迁移入口 `ProjectBuilder.ApplyNameplate()`。 |
| `Runtime/Play/HitFaceView.cs`、`HitRingView.cs`、`Generated/Clips/HitFace.anim`、`HitRing*.anim`、`Generated/HitFace.prefab`、`HitRing.prefab` | 判定点笑脸与外圈（原 `Judgment::draw_effect`／`draw_outer_effect`）：只在良／可时显示，不可、超时漏音与连打击打不显示；良／可与大音符分别用 `hit_effect_good/ok(_big)` 与 `outer_good/ok(_big)`（4 帧 336×336，`HitRing_*` 切片，`UI Additive` 加算混合）。笑脸透明度按 Nijiiro 动画 28（66.7 ms 0.5→1、保持 216.6 ms、66.7 ms 回 0.5 后移除，共 350 ms，存于 `HitFace.anim`，预制体自带 ClipSampler）；外圈帧按动画 30（54.5／72.7／90.9 ms 换帧，之后停在第 3 帧），透明度按动画 27（166.7 ms 后 33.3 ms 淡出，共 200 ms），四个变体各存为 `HitRingGood/Ok/GoodBig/OkBig.anim`。用歌曲时钟、暂停冻结；同时只有一个（新判定替换旧的，原版最多叠 7 个）。层级照搬 `Player::draw`：笑脸在 `LaneClip`（音符）之前，外圈在音符／文字与 PlayerCover 之后、`Drum` 之前。SinglePlayScene 中均为预制体实例（取代旧 `HitFlash`）。迁移入口 `ProjectBuilder.ApplyHitEffects()`（菜单 OurTaiko/Apply Hit Effects，可重复执行）；测试 `HitFaceTests`、`HitFaceFlowTests`（已完成，2026-10-02 用户确认后移入 Finished）。判定文字动画未改。 |
| `Runtime/Play/ScoreCounterView.cs` | 游玩分数计数器（`lane_score_cover`＋`score_number` 数字、TextStretch 弹动），布局在 `Core/NameplateLayout.cs` 的 `ScoreCounterLayout`，弹动由 `Generated/Clips/TextStretch.anim` 给出，与气球数字共用。 |
| `Runtime/Play/ComboView.cs`、`ComboAnnounceView.cs`、`Generated/Clips/ComboGlimmer.anim`、`ComboAnnounce.anim`、`Art/game/combo`、`Audio/combo` | 游玩连击数（原 `combo.cpp`）：`NoteLane/Combo` 居中于鼓面，连击 < 10 隐藏（用户决定），10–49 白、50–99 银、≥100 金（`combo_100_ja`＋闪光，歌曲时钟），变化时 TextStretch 弹动。每 100 连击提示（原 `combo_announce.cpp`＋Nijiiro lua 排版）：Viewport 中 `GaugeHitEffect` 之后的 `ComboAnnounce` 卷轴，100 ms 淡入、保持到 1666.67 ms、100 ms 淡出，并播放一次 1P 语音（100–5000）。两者层级都保存在 SinglePlayScene 中可编辑，代码只换数字与排版。迁移入口 `ProjectBuilder.ApplyCombo()`／`ApplyComboAnnounce()`（仅在对象缺失或生成代码改动时需要重跑）；测试 `ComboFlowTests`、`ComboGlimmerClipTests`（已完成）。 |
| `Runtime/Play/JudgeCounterView.cs`、`Art/game/judge_counter`、`Editor/ProjectBuilder.JudgeCounter.cs` | 判定计数器（用户设计，参照街机截图）：左上半透明橙色面板，良／可／不可／連打数 行条（标签切自 `result/score/max_combo_ja`），计数用 `score_number` 右对齐；判定时由 `PlayScene.UpdateHud` 更新。面板与行条贴图由生成器绘制。迁移 `ProjectBuilder.ApplyJudgeCounter()`；测试 `JudgeCounterFlowTests`（进行中）。 |
| `Assets/OurTaiko/Runtime/Play/BalloonCounterView.cs` | 7 号气球剩余次数、数字弹动、膨胀、破裂与淡出。 |
| `Assets/OurTaiko/Runtime/Play/FpsCounter.cs`、`DrumPad.cs` | 实测帧率和触控鼓（舞者帧动画见「动画剪辑」）。触控鼓**只放在 SinglePlayScene**（原版为全局叠加层），复制原版：Nijiiro `global/overlay/touch_drum.png` 全画面 50% 不透明，位于暂停／结果面板之下；每次按下按原全局动画 66 以底边中心缩至 0.95 再回弹（各 70 ms、二次缓出，真实时间）。判定区照搬 `input.cpp::touch_quadrant_vkey`：上半屏为咔，下半屏中以设计区底边中心、半径为宽度 0.262／0.242 的椭圆内为咚、其余为咔，左右按中线分；落在 uGUI 按钮上的点交给按钮。迁移入口 `ProjectBuilder.ApplyTouchDrum()`。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.SongSelectResult.cs` | 菜单 OurTaiko/Create Song Select And Result Scenes：导入选曲／结算素材、生成切片，仅在场景缺失时创建，并对已有场景只做定向升级。 |
| `Assets/OurTaiko/Editor/ProjectBuilder.cs`、`ProjectBuilder.Nijiiro.cs`、`ProjectBuilder.Balloon.cs`、`ProjectBuilder.SceneSwitcher.cs`、`ProjectBuilder.SongLoading.cs` | 初始生成、Nijiiro 布局／魂槽／连打切片、气球资源配置、全局控件专项迁移，以及选曲加载幕布（重建预制体内的 `SongTransition` 子物体，SongLoadingScene 仅缺失时创建）；按需使用专项入口，避免全量重建现有场景。 |
| `Assets/OurTaiko/Art`、`Audio`、`Generated` | 打平的皮肤图片／音效、已生成 Sprite 切片与字体；运行时无需原仓库。 |
| `Assets/OurTaiko/Songs` | TRIPLE HELIX（含音乐）、Input Calibration（无音乐）、Branch Training（无音乐分支练习谱）。 |
| `README.md`、`Documentation/PortingNotes.md`、`Documentation/ImportedAssets.json` | 运行说明、详细行为依据与历次验证、素材来源记录。 |

操作：F／J 为咚，D／K 为咔（游玩时同一帧只判定最早的一次打击，其余同帧打击丢弃——太鼓输入互斥），Space／Esc 打开暂停菜单／恢复，F1 重开；暂停菜单依次为 Resume、Restart、Back to Song Select，↑／↓（或 D／K、←／→）选择，Enter／F／J 确认，也可点击／触摸。菜单淡入和淡出各 0.5 秒，全程禁用 DrumPad，淡出后才继续演奏。Entry 中 F／J（或点击）加入／决定，D／K 只有咔声。选曲 D／K 移动、F／J 决定（自动演奏只在演奏オプション的オート行切换，A 键快捷与各场景 KeyHelp 按键提示已删除）、Esc 返回 Entry（演奏オプション中 D／K 改值、F／J 下一行、Esc 关闭）；结算 F／J 跳过／返回。游玩页也可用鼠标／触控敲击原版样式的触控鼓，选曲板、难度卡和结算画面也可点击。

**选曲加载幕布（SongSelect → SinglePlayScene 过渡）。** 照搬 `transition.lua`／`anim/loading_song.lua`：关闭 532 ms（帧 5→55，在旧场景上）→ SongLoadingScene 停在帧 55（标题、副标题、皮肤「ゲームのヒント」原图、咚咔、星、光晕）→ 打开 532 ms（帧 60→109，在游玩场景上），打开结束后才开始倒计时。用户决定：幕布美术放在全局 SceneSwitcher；停留至少 2 秒；提示区用皮肤文字贴图（截图中的街机插画卡不在皮肤内）；所有 `Play()` 入口使用幕布，重开与其他切换仍为淡入淡出。演奏スキップON 徽章因该功能未实现而不显示。预解析谱面经 `SceneSwitcher.TakePreparedChart` 只交给 PlayScene 一次。详见 `Documentation/PortingNotes.md`「选曲加载幕布与 SongLoadingScene」。

- 切换机制：`Play()` 记录 SelectedSong／Course／AutoPlay 与 ReturnScene（排除 SongLoadingScene），`ShowSongOnCurtain` 写入 TJA 的 TITLE／SUBTITLE，再 `SwitchScene(SongLoadingScene, TransitionStyle.Curtain, autoFadeOut: false)`；关闭结束后加载，幕布保持关闭（`IsCurtainClosed`、`IsCovered` 为真，输入阻挡）。`SongLoadingScene` 等待 `minimumSeconds`（场景内可调，默认 2）与 `IsSwitching` 结束后调用普通 `SwitchScene(GameScene)`：**遮罩在打开前保持自身样式**——幕布已关闭时跳过关闭，加载后由幕布打开；打开时看 `SongTransition.IsVisible` 选择样式。失败／取消同样用当前样式打开旧场景。缺少幕布或 SongLoadingScene 不在 Build Settings 时，`Play()` 退回直接淡入 SinglePlayScene。
- 加载内容：`PlayScene.PrepareChart`（解析＋`ChartModifiers.Apply`，对应 `Player::reset_chart`）在主线程执行，失败只记日志，由 PlayScene 再解析并显示原错误面板；`song.music.LoadAudioData()` 等到不再 Loading。PlayScene 仍会对 `music.clip`／don／ka 调用 `LoadAudioData`，作为重开与直接运行的兜底。音频由 `SceneSwitcher.SelectedSong` 持有，不会随场景卸载。
- 表现细节：`SongTransition` 由 `LumenClip` 采样 `Animations/loading_song.txt`；关闭／打开均为真实时间。标题文字在幕布出现（`Appear`）时才写入并 `Squeeze(1920)`；主副标题使用统一黑色描边（见上方「单一字体与黑色描边」）。底部光晕用 `Generated/UI Additive.mat`（`Mobile/Particles/Additive`）；幕布根物体有透明 raycast Image 阻挡点击，`Viewport1920x1080` 带 RectMask2D，非 16:9 时幕布不会画进留边。
- 原版未移植部分：每首歌目录下的 `Loading.png` 自定义加载图（`add_loading_graphic`）、段位加载画面（`set_dan`）、Fanmade 远程下载进度页与取消、以及演奏スキップON 徽章（只在跳过功能启用时显示）。均未授权。
- 测试注意：经 `Play()` 进入游玩现在要经过幕布与至少 2 秒停留，原有 `WaitForScene(GameScene)`（条件为非切换中且活动场景为 SinglePlayScene）在 SongLoadingScene 停留期间不会误判完成。截图时幕布在 SceneSwitcher 的 Overlay Canvas 上，`SceneFlowTests.Capture` 只渲染场景 Canvas 拍不到；`SongLoadingCurtainTests.Capture` 会把所有根 Overlay Canvas 临时切到相机模式一起渲染。

**Entry。** 照搬 `scenes/entry.cpp`、`objects/entry/*` 与 Nijiiro `Scripts/entry/entry.lua`、`box.lua`、`player.lua`，Nijiiro 开启 `entry_credit_arcade`（街机投币模式）。用户决定：模式板为「演奏ゲーム」与「ゲーム設定」（2026-10-02 加入，进入 GlobalSettingScene；特訓モード／きせかえ 留待对应场景移植；段位道場 板在原版 `dan_available` 恒为 false，从不显示）；3D 咚与加入时的云不绘制，但模式选择仍按原版云动画结束时刻（加入后 550+350+333 ms）出现；两行信用行都显示，只有 1P 可以加入（任意咚面）；操作指引、フリープレイ＋QR＋2P 邀请云、状态芯片移植，60 秒计时器只作占位不倒数（ALL.Net 图标未做）。时间轴 `entry_bg`／`credit_row`／`credit_fade`／`credit_side`／`mode_board`／`cursor_glow` 由 `LumenClip` 采样。操作指引原图 4576×6900（325 格），只切片决定循环 210–324 格，导入上限 8192、CompressedHQ（未压缩约 126 MB）。这 115 帧在原图导入设置中切片（Multiple 模式，名称 `ControlGuide210`–`324`，重复执行保留 spriteID），由单个 `Generated/Clips/ControlGuide.anim`（`Image.m_Sprite`，30 fps 循环，长 115/30 s）引用；`ControlGuideView` 通过 `ClipSampler`（Animator＋手动求值的 Playables 图，`AnimationClip.SampleAnimation` 不会应用 sprite 关键帧）按 Entry 的时钟取帧中点采样，加入时仍从头开始。迁移入口 `ProjectBuilder.ApplyControlGuideClip()`（菜单 OurTaiko/Apply Control Guide Clip），测试 `ControlGuideClipTests`（进行中）。`entry/global/player_entry_*` 为全透明 8×8 占位图，不绘制。文字描边按统一规则（模式板标题不再有色边与双层边）。详见 `Documentation/PortingNotes.md`「Entry 场景」。

**选曲／结算。** 流程：Entry（入口）→ SongSelect →（幕布＋SongLoadingScene）→ SinglePlayScene → Result → 发起游玩的场景（`SceneSwitcher.ReturnScene`）。`PlayScene.Finish` 只生成结果并通过 `SceneSwitcher.ShowResult` 交接歌曲及回放；`ResultScene.Awake` 在创建结算演出前一次性领取本局，保存本地成绩或发起在线上传（自动演奏不保存，重载结算页不重复处理），场景内结果面板只用于谱面加载失败。时间与布局均取自原 `song_select.cpp`／`navigator.cpp`／`player.cpp`／`result.cpp` 及 Nijiiro Lua，详见 `Documentation/PortingNotes.md`“选曲与结算场景”。TMP Mobile SDF 描边需 `OUTLINE_ON`，由共用描边材质提供。歌曲音频现在主要在 SongLoadingScene 中载入；PlayScene 仍于遮罩关闭期间预载（重开／直接运行），避免首次 PlayScheduled 卡顿约 1 秒。

### 已完成玩法与表现的核心逻辑

**分支游玩。** 支持 `#BRANCHSTART p/r`、`#N/#E/#M`、`#BRANCHEND`、`#SECTION`。命中率以良=1、可=0.5、不可／漏音=0 计算百分比并截断；连打分支只统计 5／6 号，气球和彩球不计入，并保留原版对当前持续连打总数的补充规则。分支判定和 SECTION 重置按歌曲事件时间处理，不把未来判定计入过去的分支。每条路线从共同起点恢复 BPM、时刻、SCROLL 等解析状态；选线时机参考原版对象进入画面的时间。未选路线不显示、不判定、不计分。普通深色、玄人蓝色、达人紫色，右侧贴图随路线变化滑动和淡入淡出；非分支谱隐藏这些标识。暂停冻结动画，重开恢复初始状态。

**Shinuchi 计分。** 基准分从 100 万分扣除气球预算（每个最多 100 次，每次 100 分）和连打预算（源码 float 常量约 16.92008 次／秒，每次 100 分），除以公共段＋达人路线的普通音符数，向上取整到 10 分；没有普通音符时基准分为 1000000。良得基准分，可按原整数除法减半并取 10 分整数倍，不可／漏音不加分。大音符、GOGO、连击不加倍率，5／6／7／9 号每次有效击打均为 100，吹爆不另加 5000。最终分数不限制为 100 万。连打预算直接使用对应尾部减去头部时间，即 `(EndTime - Time)×1000` 毫秒；已按用户要求修复原源码使用下一个对象、可能被小节线截短的缺陷。不可恢复为 nextobj 时长。BALLOON 缺失次数默认 1。参考 `tja.cpp::calculate_base_score` 与 `player.cpp`。

**魂槽。** 数值上限 10000，双精度累积，按原 `gauge.h` 难度／星级表增减；普通音符分母固定为公共段＋达人路线。良为 `1000000/(max(1,普通音符数)×soul_percent)`，可／不可按表乘倍率，每次判定限幅并以 `1e-6` 点容差归整过关／满槽边界；百分比向下取整。长音符不改变魂槽或重启淡入。LEVEL 缺失／为 0 时使用 Oni ★10 数值及 80% 过关档，原表未使用行保持零值。Normal／Hard 部分表项在原源码中标为推测，本项目保留来源值。50 格；Easy／Normal+Hard／Oni+Edit 的过关阈值分别为 **60%／70%／80%**，显示和结算共用。过关区使用加高的黄色贴图完整填充；新增格子 450 ms 淡入。满槽彩虹先用 450 ms 淡入，然后每 75 ms 过渡下一帧，共 8 帧／600 ms 循环；魂火 50 ms／帧、400 ms 循环。掉出满槽／过关状态时恢复相应样式，再次满槽重新淡入。参考 `src/objects/game/gauge.cpp` 与 Nijiiro 的 `skin_config.json`、`Graphics/game/gauge/texture.json`、`Graphics/game/animation.json`。

**7 号气球。** 脸的相对对齐量是音符宽度的 **12/128**，通过锚点随音符尺寸与画布缩放，不能改回固定像素补偿。首次有效咚击打后显示 `总次数 - 已击打次数`，使用 Nijiiro 气泡及数字图集，支持跨位数布局；咔不计数。数字 50 ms 拉伸＋116 ms 回弹，身体按进度使用 0、2、3、4、5、6 帧；吹爆后第 7 帧、数字 0，166 ms 淡出；未吹爆到期立即隐藏。移动时显示 `notes/10` 尾部，击打时替换为膨胀素材；9 号彩球不混用这套显示。`Assets/OurTaiko/Audio/balloon_pop.ogg` 原样复制自 Nijiiro，约 0.414 秒、预加载；手动／自动达到要求次数的那次事件只播放一次，到期未爆不播放。参考 `player.cpp::draw_balloon/check_balloon`、`balloon_counter.cpp` 及 Nijiiro 气球配置。

**音符飞向魂槽与 GaugeHitEffect。** 照搬 `note_arc.cpp`／`gauge_hit_effect.cpp`，数值取 Nijiiro 皮肤。良／可的 1–4 号音符、5／6 号连打每次击打（按所敲的鼓飞小咚／小咔，自动演奏为咚）、吹爆的 7 号气球各飞一个；不可与 9 号彩球不飞。路径为 Nijiiro `note_arc_pivot` 圆弧（圆心 (1269.22,415.14)、半径约 719.14、-154.89°→-38.24° 等角速度、`note_arc_duration` 30 帧×16.67 ms），**不是** OurTaikoPlayer 当前代码的贝塞尔（22 帧、curve height 608）——此取舍已告知用户，用户未要求改回；读取圆弧键的原实现在 OurTaikoPlayer `7a08ced`，合并时被丢弃但皮肤键保留。起点判定圈中心（轨道局部 618,110），终点魂徽章中心 (1834,-30)；顶点飞出设计区顶边，由 `NoteArcs` 的 RectMask2D 裁掉。到达时由 `NoteArcView` 交给 `GaugeHitEffectView`（同时只有一个，新到达清除旧的，按到达时刻 `开始+30 帧` 计时）：`gauge/hit_effect` 三帧 232×232（33.33／66.66 ms 换帧），116.67 ms 后 266 ms 内从 0.8 线性放大到 1.5，按尺寸着色黄 (253,249,0)→橙 (255,161,0)→红 (230,41,55)，300 ms 后 83 ms 淡出；音符画在光环之上同步淡出。Nijiiro 的 `hit_effect_circle*` 是全透明 8×8 占位图、旋转固定为 0，因此不绘制，不要当作遗漏（2026-10-02 起这两张图不再导入项目）。层级：`SoulGauge` → `NoteArcs` → `GaugeHitEffect`（原版先画魂槽再画 `draw_overlays`）；坐标经 `NoteLane` 变换换算，使用歌曲时钟，暂停冻结。迁移入口 `ProjectBuilder.ApplyNoteArcs()`（菜单 OurTaiko/Apply Nijiiro Note Arcs），帧在 `game/gauge/hit_effect.png` 的导入设置中切片（`GaugeHitEffect0–2`）。未移植：气球吹爆彩虹拖尾（`balloon/rainbow`／`note_arc_balloon_*`）。详见 `Documentation/PortingNotes.md`「音符飞向魂槽与 GaugeHitEffect」。

**名牌与分数计数器。** 照搬 Nijiiro `Scripts/global/nameplate.lua` 与 `score_counter.cpp`，素材为 Nijiiro `global/nameplate`（未导入 2P／AI 牌）。数据由全局 `PlayerInfoController`（首场景前自动创建、跨场景保留，用户决定）在初始化时读取 `Application.persistentDataPath/player.json`（缺失时写入默认 Don-chan／Donder Debut!），`Changed` 事件让所有 `NameplateView` 即时更新；无游戏内编辑界面。「Donder Debut!」或空为无称号，dan 只接受 0–24；无称号无段位为 coin 牌（30 号名字，无称号带／段位），否则画称号带（titleBackground 0–4，越界归 0）、段位（gold 金色）、黑色称号与 24 号名字；文字只横向压扁到框宽（名字 190、称号 215）。位置：游玩 `NoteLane` 局部 (-44,161)（鼓面之后、BalloonCounter 之前），选曲 (14,908)（Wheel 与 CoursePanel 之间），结算 (2,922)（SoulSheen 之后、FadeIn 之前）。名字有黑色描边，黑色称号无描边。分数计数器替换了原 TMP 占位分数：灰条在轨道局部 (0,12)，数字右对齐 x 255、间距 30、不补零、顶边 5.5，变化时 TextStretch 向上伸长，作为 `NoteLane` 最后一个子物体。SinglePlayScene 的调试文字 PlayerName 与 PlayState（READY 倒计时／AUTO PLAY／1 PLAYER）已删除（用户决定）。未移植：「+分数」飞出动画（`ScoreCounterAnimation`）。详见 `Documentation/PortingNotes.md`「名牌、PlayerInfoController 与分数计数器」。

**音符文字（moji）。** 音符下方的「ドン／ド／コ／カッ／カ／ドン(大)／カッ(大)／連打ー／連打(大)ー／ふうせん／ーっ!!／くすだま」取自 `notes/moji` 12 帧（256×48，Point 采样，`notes/moji.png` 导入设置中的 `Moji0–11`）。分配在 `Core/NoteMoji.cs`，照搬 `tja.cpp::modifier_moji/find_streams`：解析器按原版 NoteList 保留源顺序的 `TaikoChart.NoteLists`（公共段一条，每个分支每条路线各一条，含小节线与长音符尾），依次按 8／12／16／24／32 分查连续段（±15 ms，长音符头断开，小节线和尾参与），段内除末个外咚→ド、咔→カ，恰 3 个咚时中间为コ；`ChartModifiers.Apply` 末尾重算，文字随あべこべ／ランダム换色。原版一小节分多行书写时会插入额外隐藏小节线并截断连续段，本项目每小节只有一条小节线，不复制该现象。渲染在 `PlayScene.RenderMoji`：独立的 `NoteLane/MojiClip/Moji` 层紧跟 `LaneClip`（全部文字压在全部音符之上，层内早的音符在上），文字中心比音符中心低 123（skin `moji.y`=209 对 `notes.y`=14）；显隐与音符相同（命中消失、漏音继续流动、ドロン一并隐藏），按自身宽度裁切；气球计数显示期间按 `skip_note` 隐藏；连打为 `moji_drumroll_mid`（宽 8＋长度）→头字→尾字ーっ!!，高度不随 Y 滚动。迁移入口 `ProjectBuilder.ApplyMoji()`。

**音符显隐与层级。** 照搬原版 `draw_note_buffer`：只有击打（良／可／不可）会立即移除音符；超时漏音（`PlaySession.Missed`，由 `AdvanceNotes` 超时置位）与到尾判定的 5／6 号连打继续按原速流过判定点，直到完全离开轨道。气球／彩球仍按吹爆或到期隐藏。裁切不用固定像素：`PlayScene.InLane` 以 `LaneClip` 下音符层的实时 rect 判断，`Reach` 按当前贴图尺寸计算水平范围（普通音符半宽；气球含 12/128 脸偏移与 `notes/10` 尾；连打含长度与尾部贴图，正负滚动均可），小节线按自身半宽。**对象池**（2026-10-02）：音符、音符文字与小节线不再逐个预建，只有在轨道可见范围内的对象才从按形状分的池（普通／气球／连打；文字普通／连打；小节线）取出，离开即失活归还；`CreateNotes` 只预热少量对象，池不足时再创建。层级按原 `draw_notes` 逆序绘制：有新对象进入时 `Restack` 让早的音符压在晚的音符上（每个连打内部仍为身体→尾部→头部）。测试和代码须用 `PlayScene.NoteRoot(index)`／`MojiRoot`／`BarRoot` 取对象，不在画面上时返回 null；不能用 `noteLayer.GetChild(i)`，也不要缓存某个音符的对象后跨帧判断 `activeSelf`（归还后可能被别的音符复用）。由 `ChartTests.OnlyTimedOutNotesAreMarkedMissed` 与 `SceneFlowTests.FinishedRollsAndMissedNotesKeepScrollingPastJudge` 覆盖。

**5／6 号连打。** `PlayScene` 分别持有小／大 `rollBodySprites` 和 `rollTailSprites`；身体只横向拉伸，尾部保持原宽高比，顺序为身体→尾部→头部。源图集左上坐标切片：小身体 `(0,1544,72,192)`、大身体 `(72,1544,72,192)`、小尾 `(0,2120,80,192)`、大尾 `(0,2312,120,192)`。身体长度为连打长度绝对值加音符高度的 **1/128**（192 设计高度时为 1.5），对应原版贴图宽度与 `drumroll_width_offset` 的合成，覆盖接缝。尾部起点位于连打结束位置；负滚动翻转身体和尾部，头部表情保持正向。音符图集使用 **Point** 采样，避免双线性采样混入邻近大连打帧形成杂边。切片在 `game/notes/notes_atlas.png` 的导入设置中：`RollBodySmall`、`RollBodyBig`、`RollTailSmall`、`RollTailBig`；配置入口 `ProjectBuilder.ApplyDrumrollSprites()`。参考 `player.cpp::draw_drumroll`、`src/libs/texture.cpp` 及 Nijiiro 音符 `texture.json`；头尾同速逻辑未改动。

### 验证结果与继续工作方法

- 最新验证（2026-10-02，全部程序集）：重开 Editor 后编译通过，进行中 EditMode 8/8、PlayMode 4/4，已完成 EditMode 132/132、PlayMode 28/28 全部通过，报告 `TestResults/final-*.json`。`SongLoadingCurtainTests` 曾偶发失败：停留时间从切换完全结束（晚于场景 Start 一帧加 50 ms）开始计时、咚／咔位置用精确浮点比较；已改为从加载场景成为活动场景时计时、位置用 0.01 容差。`GlobalSceneSwitcherTests` 不再在遮罩上写黄色「LOADING」测试文字（游戏本身从不显示该文字）。
- 此前验证（2026-10-02，Entry）：进行中 EditMode 11/11、PlayMode 3/3，已完成 PlayMode 27/27 通过（菜单场景改为 Entry 影响所有从菜单开始的 PlayMode 测试，因此回归了 Finished 程序集）；报告 `TestResults/entry-playmode.json`、`TestResults/entry-finished-playmode.json`，截图 `TestResults/EntryCredit.png`、`EntryModeSelect.png`。
- 此前验证（2026-10-02，名牌与分数计数器）：Unity Editor **EditMode 139/139、PlayMode 29/29 全部通过**，报告 `TestResults/nameplate-editmode.json`、`TestResults/nameplate-playmode.json`；新增 `NameplateTests`、`NameplateFlowTests`，`ScoreGaugeFlowTests` 改为检查 `scoreCounter.Text`（不补零）。截图 `TestResults/NameplatePlay.png`、`NameplatePlayCoin.png`、`NameplateSongSelect.png`、`NameplateResult.png`。PlayMode 的 `TestScoreStore` 让 `PlayerInfoController` 使用不落盘的默认数据。
- 此前验证（2026-10-01，音符飞向魂槽与 GaugeHitEffect）：Unity Editor **EditMode 131/131、PlayMode 27/27 全部通过**，报告 `TestResults/arc-editmode.json`、`TestResults/arc-playmode.json`；新增 `NoteArcPathTests`、`GaugeHitEffectTimingTests`、`NoteArcFlowTests`，截图 `TestResults/NoteArc.png`。路径按 Nijiiro 皮肤的圆弧键而非 OurTaikoPlayer 当前的贝塞尔，理由见 `Documentation/PortingNotes.md`「音符飞向魂槽与 GaugeHitEffect」；Nijiiro 的 `hit_effect_circle*` 为透明占位图，不绘制；气球吹爆彩虹拖尾未移植。
- 此前验证（2026-10-01，选曲加载幕布）：Unity Editor **EditMode 127/127、PlayMode 26/26 全部通过**，报告 `TestResults/curtain-editmode.json`、`TestResults/curtain-playmode.json`；新增 `SongLoadingCurtainTests`，截图 `TestResults/CurtainClosing.png`、`SongLoading.png`、`CurtainOpening.png`。
- 此前验证（2026-10-01，音符文字）：Unity Editor **EditMode 127/127、PlayMode 24/24 全部通过**，报告 `TestResults/moji-editmode.json`、`TestResults/moji-playmode.json`；`NoteMojiTests` 覆盖帧分配／连续段／小节线／分支独立／换色重算，`NoteMojiFlowTests` 覆盖帧、层级、位置、命中／漏音／ドロン显隐与连打横条，截图 `TestResults/NoteMoji.png`。当时调试计数文字 `GOOD/OK/BAD/ROLL` 与文字带重叠；该文字已于 2026-10-02 随判定计数器删除。
- 此前验证（2026-10-01，演奏オプション）：Unity Editor **EditMode 115/115、PlayMode 22/22 全部通过**。报告为 `TestResults/options-editmode.json`、`TestResults/options-playmode.json`；PlayMode 通过 `TestScoreStore` 使用临时成绩文件和不落盘的 `PlayOptions`。`PlayOptionsTests`／`PlayOptionsFlowTests` 覆盖速度档位、面板行走、滑动曲线、谱面修改、随机概率、持久化与游玩场景实际效果；截图 `TestResults/SongSelectOptions.png`、`PlayOptionsBadges.png`。`TestResults/` 不纳入版本控制，本机报告不保证随新克隆存在。
- 测试分为进行中与已完成两组（2026-10-02 用户要求）。**进行中**：`OurTaiko.Tests`（`Tests/EditMode/`）与 `OurTaiko.PlayModeTests`（`Tests/PlayMode/`），目前为名牌（`NameplateTests`、`NameplateFlowTests`）与选曲／结算全局元素（`GlobalOverlayFlowTests`）；Entry 已完成（2026-10-02 用户确认），`EntryTests`、`EntryFlowTests` 移入 Finished；连击数与连击提示已完成（2026-10-02 用户确认），`ComboFlowTests` 与从 `AnimationClipTests` 拆出的 `ComboGlimmerClipTests` 移入 Finished；全局设置与 Entry 触控已完成（2026-10-02 用户确认），`SettingsMenuTests`、`GlobalSettingFlowTests`、`EntryTouchFlowTests` 移入 Finished。**已完成**功能的测试移到 `Tests/Finished/`：`OurTaiko.FinishedTests`（EditMode）与 `OurTaiko.FinishedPlayModeTests`（PlayMode），类名与命名空间 `OurTaiko.Tests` 不变。共享辅助在 `Tests/Shared/`（`OurTaiko.TestSupport`，仅 `UNITY_INCLUDE_TESTS` 时编译）：`TestCapture.Capture` 截图、`TestData.Use/Restore` 临时成绩／选项／玩家信息，每个 PlayMode 程序集各有一个调用它的 `TestScoreStore` SetUpFixture。功能确认完成后把其测试移入 Finished。**只运行与改动相关的测试类**，不要每次跑全部程序集；需要回归时再跑 Finished 程序集。
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

- **Entry 未移植部分**：3D 咚与加入云（原版 `player.lua` 的 drum_back／drum_front）、2P 加入、其他模式板（きせかえ）与きせかえ菜单（`costume_menu`）、ALL.Net 图标（`allnet_indicator`）。选曲与结算的全局元素已放置（ALL.Net 图标仍未做）。
- **设置场景待办**：右侧项目列表无滚动，超过 5 行会压到 footer（见「最新完成：GlobalSettingScene」）；设置场景未放全局覆盖层（计时器、フリープレイ、操作指引）；无多语言（设置标签英文）。
- **名牌待办**：原版段位选择（`dan_select.cpp`）与段位结算（`dan_result.cpp`／`dan_result_draw.lua` 的 `nameplate_pos`）场景也显示名牌。将来移植这些场景时须同样复用 `Generated/Nameplate.prefab` 与 `PlayerInfoController`；2P／AI 名牌（`2p.png`／`ai.png`）与名牌编辑界面同样未做。
- **可选：判定点效果叠加**（用户 2026-10-02 记为选项，暂不做，需用户确认后再动）：原版 `player.cpp` 每次判定向 `draw_judge_list` 加入一个独立 `Judgment`（笑脸、外圈与判定文字各自计时），每帧按旧→新全部更新绘制，笑脸动画结束（约 350 ms）后移除；密集连段时多个效果在不同动画阶段重叠。上限只在加入良时检查（`size() < 7`），可与不可不检查；不可条目只有文字、无笑脸／外圈，但同样占名额。本项目目前笑脸、外圈与判定文字各只有一个，新判定替换旧的。移植做法：用现有 `HitFace`／`HitRing` 预制体建小型实例池，每次判定取一个独立计时，旧的先画、新的在上，按原版规则限 7 个；判定文字也需同样改为叠加。

当前不是整个原模拟器的等价移植。游玩中气球吹爆的彩虹拖尾、选曲中的文件夹／类别、搜索与排序、独立音色面板、演奏スキップ功能（及加载幕布上的演奏スキップON 徽章）、歌曲自定义 `Loading.png` 加载图、段位加载画面、2P、曲目板飞入、难度决定标记弹出，结算中的成绩等级（粋／雅／極）演出、3D 咚、皇冠光芒加算混合、游玩中的「+分数」飞出动画尚未移植；TRIPLE HELIX 的 Edit 谱面含字母扩展音符，无法游玩。联网／成绩上传、双人、段位、3D 咚角色、全部皮肤特效、逐帧回放尚未实现（大音符单侧击打即可，属有意设计，见上方约束）；计分固定使用 Shinuchi，单人魂槽数值已按原源码还原，GEN3 计分及段位魂槽不在当前范围。自动连打仍为 **15 次／秒**，未移植原版随 BPM 变化的自动连打节奏。`s` 分数分支（单人游玩拒绝；练习固定路线可显示，见「练习分支」）、`#LEVELHOLD`、BMSCROLL／HBSCROLL、字母扩展音符明确不支持；不要静默猜测其行为，也不要将这些范围自动当作用户已授权的新开发任务。
