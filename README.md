# OurTaikoPlayerUnity

从相邻 `OurTaikoPlayer` 提取单人游玩页面的 Unity 2D 工程。使用 **Unity 6000.3.25f1 / Universal 2D / uGUI + TextMeshPro**。所有新增运行逻辑和 Editor 工具均为 **C#**，不包含 C++、Lua 或原项目的原生插件。

## 运行

1. 用 Unity Hub 打开本目录。
2. 打开 `Assets/Scenes/Entry.unity`（Build Settings 的首个场景），点击 Unity 的 Play。
3. 在 Entry 敲咚加入，再敲咚选择「演奏ゲーム」，在 Nijiiro 选曲中选择 TRIPLE HELIX（含音乐）、Input Calibration（原项目的无音乐校准谱）或 Branch Training（分支练习谱）及难度。

也可直接打开 `Assets/Scenes/SinglePlayScene.unity` 运行，默认载入 TRIPLE HELIX。

Entry 场景 `Entry.unity` 是游戏入口（Nijiiro 街机投币画面：1P 加入、演奏ゲーム 模式板；计时器只作占位，不倒数）；决定后进入选曲场景 `SongSelect.unity`，游玩结束后进入结算场景 `Result.unity`，再回到选曲。场景之间的切换全部交给全局 SceneSwitcher。开始游玩时显示 Nijiiro 彩虹幕布：幕布关闭后进入 `SongLoadingScene.unity` 解析谱面并载入歌曲（至少停留 2 秒），再在游玩场景上打开。

| 选曲按键 | 功能 |
| --- | --- |
| D / K（或 ← / →） | 移动曲目 / 难度光标；在おに上连按右 10 次切换裏 |
| F / J（或 Enter） | 决定（もどる 返回曲目列表，扳手按钮打开演奏オプション） |
| A | 切换自动演奏 |
| Esc | 返回 Entry；演奏オプション打开时关闭面板 |

**演奏オプション。** 难度面板中的扳手按钮打开 Nijiiro 选项面板，共 7 行：オート、はやさ（0.1–2.0 每档 0.1，之后 3.0、4.0，首尾循环）、ドロン（隐藏音符，小节线保留，仍正常判定）、あべこべ（咚咔互换）、ランダム（きまぐれ：每个咚／咔音符 30% 概率换色；でたらめ：50%）、演奏スキップ（单人没有 2P 鼓，灰显不可改）、音色（21 套 Nijiiro 打击音色和無音，切换时试听）。D / K 改当前行的值，F / J 进入下一行，最后一行后面板滑出并把设置保存到 `Application.persistentDataPath/options.json`。触控时点行名选中该行（再点一次等于决定），点数值框左／右半边改值，点面板外关闭。游玩时轨道左侧按 3 列网格显示 オート（自动演奏时排第一）／速度／ドロン／あべこべ／ランダム徽章。

**名牌。** 选曲、游玩和结算画面显示 Nijiiro 1P 名牌，数据来自 `Application.persistentDataPath/player.json`（首次运行自动写入默认值）：`name`、`title`（「Donder Debut!」或空为无称号）、`titleBackground`（0–4）、`dan`（0=初級 … 24=達人，-1 为无段位）、`gold`（金色段位）、`rainbow`（彩虹称号带）。无称号且无段位时显示只有名字的白牌。全局 `PlayerInfoController` 在启动时读取该文件，并让屏幕上的名牌随数据变化更新；目前没有游戏内编辑界面。游玩画面左上灰条显示分数（Nijiiro 数字图，变化时弹动）。

结算画面按 F / J（或点击）跳过演出；演出结束约 8.3 秒后可再按一次返回，约 34 秒后自动返回。

| 按键 | 功能 |
| --- | --- |
| F / J | 左 / 右咚 |
| D / K | 左 / 右咔 |
| Space / Esc | 打开暂停菜单 / 继续 |
| F1 | 重开 |

暂停菜单依次为 **Resume**、**Restart**、**Back to Song Select**。使用 ↑ / ↓（或 D / K、← / →）选择，Enter 或 F / J 确认，也可点击或触摸选项。打开和关闭各淡入／淡出 **0.5 秒**；菜单位于游玩界面最上方，全程禁用鼓面，关闭结束才恢复歌曲与判定。游玩界面只保留 Pause 按钮，重开和返回选曲按钮在菜单内。

页面下方有可点击的咚 / 咔按钮。两个场景采用 Nijiiro 原生 **1920×1080** 画布与固定设计区域，宽高比变化时居中留边，保持判定位置和轨道比例。

默认目标为 **120 FPS**。任何场景启动（包括直接打开游玩场景）时都会关闭 VSync 并启用每帧渲染，避免画质档的垂直同步覆盖目标帧率。左上角 FPS 计数器每 0.5 秒显示实际帧率平均值，暂停时仍持续更新。它显示实测值，不是目标 120 的固定文字。

如果 Unity 内仍约为 60 FPS，检查 Game 视图的 VSync 选项、系统显示器刷新率及节能设置，并与独立播放器比较；Editor 自身的负载也可能降低帧率。120 FPS 是渲染目标，不会把 60 Hz 显示器变成 120 Hz。

## Shinuchi 计分与魂槽

计分固定使用虹版 **Shinuchi（真打）**，参照 OurTaikoPlayer 的 `tja.cpp::calculate_base_score` 和 `player.cpp`。从 100 万分中扣除气球预计得分（每个最多预算 100 次，每次 100 分）和连打预计得分（源码常量约 16.92008 次／秒，每次 100 分），除以普通音符数，再向上取整到 10 分。良得基准分，可得一半并向下取整到 10 分，不可／漏音不加分。大音符、GOGO、连击不乘倍率；5／6／7／9 号每次有效击打均为 100 分，气球／彩球吹爆不另加 5000 分。取整后总分可以超过 100 万。

计分预算和魂槽分母固定统计公共段＋达人路线，即使实际选择普通／玄人也不重算。连打预算直接使用对应尾部减去头部时间，修复原源码用下一个对象、可能被小节线截短的缺陷；隐藏小节线和源文本分行不再影响预算。画面仍按头尾时间差与头部速度移动。

魂槽使用 0–10000 的双精度进度，按原 `gauge.h` 的难度／星级表计算良、可、不可／漏音的增减；每次判定后限幅，过关和满槽边界按原版容差归整。长音符不增减魂槽。Easy／Normal+Hard／Oni+Edit 过关线为 60%／70%／80%；缺失或为 0 的 LEVEL 按原玩家逻辑采用 Oni ★10 的数值及 80% 过关档。表中未使用的星级行保持原版零值，Normal／Hard 的部分行在原源码中标记为推测值，本项目保留这些来源值。

## 分支游玩

支持 `#BRANCHSTART p,玄人阈值,达人阈值`（命中率）和 `r`（连打数），以及 `#N`、`#E`、`#M`、`#BRANCHEND`、`#SECTION`。按相邻 OurTaikoPlayer 的规则，在分支首个对象进入画面时确定路线；未选路线不会显示、判定或计分。分支谱面沿用原皮肤的轨道配色：普通为深色、玄人为蓝色、达人为紫色；轨道右侧显示「普通譜面／玄人譜面／達人譜面」图片字样。路线变化时按原版滑动、淡入淡出，并显示升降级提示。没有分支的谱面不显示这些标识。

Branch Training 是新增的无音乐练习谱：前三个咚的命中率低于 50% 进入普通，50% 至不足 80% 进入玄人，80% 及以上进入达人；后半段连打不足 5 下进入普通、5–14 下进入玄人、15 下及以上进入达人。可以打开 AUTO PLAY 验证两次达人分支，或手动游玩尝试不同路线。

当前参考模拟器只实现 `p` 和 `r` 条件。本移植对 `s` 分数条件、`#LEVELHOLD`、BMSCROLL/HBSCROLL 明确报不支持，避免按错误规则游玩。

## 场景与代码

- `Resources/SceneSwitcher.prefab`、`Runtime/Scenes/SceneSwitcher.cs`：全局切换控件，在首场景加载前自动创建，通过 `DontDestroyOnLoad` 保留。所有运行时切换统一调用 `SceneSwitcher.Instance.SwitchScene(...)` 或可等待的 `SwitchSceneAsync(...)`；`Play(song, autoPlay)`、`Restart()`、`ReturnToMenu()` 也转交同一流程。
- `SinglePlayScene.unity`：可在 Hierarchy 中编辑的 Canvas、音符轨道、判定圈、鼓面、魂槽、歌曲信息、舞者、暂停及结果面板。`PlayScene.cs` 连接输入、DSP 时钟、音乐和画面。
- `Runtime/Core/TjaParser.cs`：纯 C# TJA 读取，支持课程选择、音符 1–9、连打/气球、BPMCHANGE、MEASURE、DELAY、SCROLL（含复数）、GOGO、BARLINE，以及三路线分支与 SECTION。每条路线从分支起点恢复时刻、BPM、SCROLL、拍号等状态。
- `Runtime/Core/PlaySession.cs`：独立于 Unity 的判定、连击、自动演奏和分支选择，向计分与魂槽模块分发判定，再通过事件更新表现层。大音符单侧击打即可（有意设计，不需要双手同时击打）。
- `Runtime/Core/ChartStatistics.cs`、`ShinuchiScore.cs`：固定谱面统计与纯 C# 真打计分规则，独立于输入、音符位移、皮肤及动画。
- `Runtime/Core/SoulGaugeRules.cs`、`SoulGauge.cs`：难度／星级查表、魂槽数值、百分比和过关状态；表现层只使用计算后的进度。
- `Runtime/Play/BranchLaneView.cs`：分支轨道底色、右侧谱面字样及升降级动画，使用打平的 Nijiiro 素材与原生坐标。
- `Runtime/Play/BalloonCounterView.cs`：7 号气球的剩余次数、Nijiiro 气泡、数字弹动、膨胀与破裂淡出，跟随游玩时钟及画布缩放。
- `Runtime/Play/SoulGaugeView.cs`：50 格魂槽、加高的黄色过关区、450 ms 新格淡入、满槽彩虹与魂火。Easy／Normal+Hard／Oni+Edit 过关阈值分别为 60%／70%／80%，与结算一致。每次彩虹帧过渡为 75 ms，8 帧循环 600 ms；失去满槽或过关状态时恢复对应样式。
- `Runtime/Core/NoteScroll.cs`：按 BPM、SCROLL 和判定点到轨道右边缘的距离计算流速。连打头尾始终使用头部速度整体移动，长度保持恒定。
- `Runtime/Core/SongDefinition.cs`：在 Inspector 中指定谱面 TextAsset、音乐 AudioClip、难度和音画偏移。谱面以 `.txt` 导入，内容仍是 TJA；WAVE 字段由显式 AudioClip 引用替代。
- `SongSelect.unity`、`Runtime/Scenes/SongSelectScene.cs`：纵向曲目板（按 Navigator 的 135 px 行距、±120 px 展开间隔、每行 40 px 斜移）、选中板的 508 ms 等待与 `anim/song_board` 展开／收起、光标光晕脉动、难度小牌与おに／裏交替、试听（从 DEMOSTART 播放，离开后 330 ms 恢复选曲 BGM），以及难度选择面板（课程卡、星级、皇冠、1P 气泡、裏切换动画）。光标规则在 `Runtime/Core/DifficultyCursor.cs`，谱面信息由 `SongInfo.cs` 读取。
- `Result.unity`、`Runtime/Scenes/ResultScene.cs`、`ResultBackground.cs`：Nijiiro 结算背景（云层按原导出时间轴漂移、过关后切换金色天空与富士山弹动）、成绩板、魂槽 0.7 倍填充、行数字逐行落定、总分、皇冠、评语气泡与最高分条。时间轴在 `Runtime/Core/ResultSequence.cs`，与原 `result_player.lua` 的帧数一致。
- `Runtime/Core/LumenClip.cs`：读取 `Assets/OurTaiko/Animations/*.txt`（原 `Scripts/anim/*.lua` 导出表的原样副本）并线性采样，只当作数据，不运行 Lua。
- `Entry.unity`、`Runtime/Scenes/EntryScene.cs`、`EntryViews.cs`、`GlobalOverlays.cs`：Entry 场景与全局街机元素（计时器、操作指引、フリープレイ、状态芯片）；菜单 **OurTaiko/Create Entry Scene**。选曲也显示 QR 芯片、2P 邀请云和计时器占位，结算显示フリープレイ；菜单 **OurTaiko/Apply Global Overlays**。
- `Runtime/Core/PlayerInfo.cs`、`Runtime/Scenes/PlayerInfoController.cs`、`NameplateView.cs`、`Generated/Nameplate.prefab`：玩家名牌数据（`player.json`）、全局持有者与 Nijiiro 名牌显示；迁移入口菜单 **OurTaiko/Apply Nijiiro Nameplate**。`Runtime/Play/ScoreCounterView.cs`：游玩分数计数器。
- `Runtime/Core/PlayResult.cs`、`ScoreStore.cs`：结算数据与本地最佳成绩（`Application.persistentDataPath/scores.json`）。与原 `save_score` 相同，自动演奏不保存；选曲板和难度卡显示保存的皇冠。
- `Scenes/SongSelect.unity` 保存选曲、难度选择、演奏选项、名牌与计时器的完整层级。选中 `SongSelectScene` 控制组件，在 Inspector 切换「选曲列表／难度选择／演奏选项」预览，无需进入 Play；预览切换只控制显示状态。
- `Generated/SongBoard.prefab` 与 `Generated/PlayOptions.prefab` 可在 Prefab Mode 中编辑。运行时代码绑定保存的引用，更新歌曲内容、输入与动画；静态布局保留 Inspector 调整，动画从保存的位置与尺寸施加偏移。迁移菜单 **OurTaiko/Apply Song Select Layout** 只处理尚未迁移的场景。
- `Editor/ProjectBuilder.SongSelectResult.cs`：菜单 **OurTaiko/Create Song Select And Result Scenes**，导入新素材、生成切片与描边材质，仅在场景不存在时创建，之后可直接编辑场景。
- `Editor/ProjectBuilder.cs`：通过 Editor API 创建初始场景和 sprite 切片。生成后不自动覆盖场景，后续直接编辑现有场景。

选曲／结算尚未移植：文件夹与类别、搜索与排序、独立音色面板、演奏スキップ、段位、2P、成绩等级（粋／雅／極）演出、3D 咚、曲目板飞入动画、难度决定后的标记弹出，以及皇冠光芒的加算混合（目前按普通透明度绘制）。TRIPLE HELIX 的 Edit（裏）谱面使用字母扩展音符，可在选曲中选择但会显示“CHART COULD NOT LOAD”。

此阶段提取的是独立的单人游玩模块。联网/成绩上传、双人、段位、3D 咚角色、原皮肤全部 Lua 特效、逐帧回放尚未移植。大音符不需要双手同时击打，这是有意设计而非缺失功能。自动连打仍为 15 次／秒，原版随 BPM 变化的自动连打节奏尚未移植；它与 Shinuchi 基准分使用的预计连打次数是两项独立规则。

全局切换顺序参考 MajdataPlay 的 `Assets/Scripts/Global/SceneSwitcher.cs`：锁定输入并通知当前场景停止游玩／音频，关闭过渡（0.9 秒），等待准备任务，异步加载，等一帧和 50 ms，再打开过渡（0.8 秒）。过渡采用相同的 OutQuint 曲线和实时时钟，本项目使用独立 uGUI 遮罩淡入淡出。`CurrentScene`、`LastScene`、`MainCamera` 和 `OnSceneChanged` 由控件统一更新。准备任务失败或取消会恢复旧场景的可见性；切换期间的重复请求不会再启动加载。

需要在加载后继续初始化时，调用 `SwitchSceneAsync(sceneName, autoFadeOut: false)` 保持遮罩，再调用 `FadeOutAsync()` 揭示场景。`FadeIn`／`FadeOut` 的含义与 MajdataPlay 一致，分别关闭／打开遮罩；`SetLoadingText` 更新加载提示，`SwitchSceneAfterTaskAsync` 等待普通或带结果的 .NET Task 后再加载。游玩在遮罩打开后才开始完整倒计时。全局控件不持有额外的 EventSystem、Camera 或 AudioListener，直接运行 SinglePlayScene 也会自动创建它。

## 素材来源

当前只有一套 **Nijiiro** 皮肤。轨道、音符、魂槽、背景、19 帧舞者、字体和打击音效直接复制自相邻仓库的 `Skins/YataiDONNijiiro`；其缺少的分支字样等资源已从 Green 直接复制补齐，全部打平到 `Assets/OurTaiko/Art` 和 `Audio`。没有皮肤继承、回退加载或 Green 切换模式。素材来源逐项记录在 `Documentation/ImportedAssets.json`，原仓库未作修改。原项目自带的两份谱面和 TRIPLE HELIX 音频继续使用；保留原 `LICENSE`、`NOTICE` 及各资源的权利归属。

魂槽行为按当前原模拟器的 `gauge.cpp`、Nijiiro `skin_config.json` 和 `game/animation.json` 实现。源码没有“每 0.5 秒整条闪黄”的循环；其新增格子采用 450 ms 淡入，因此这里没有另加未经源码确认的周期闪黄。

## 验证

用 Unity Test Runner 运行测试。`OurTaiko.Tests`（EditMode）和 `OurTaiko.PlayModeTests`（PlayMode）是进行中功能的测试；已完成功能的测试在 `Tests/Finished/` 的 `OurTaiko.FinishedTests` 和 `OurTaiko.FinishedPlayModeTests`，共享辅助在 `Tests/Shared/`（`OurTaiko.TestSupport`）。已完成的 EditMode 测试覆盖谱面、判定、流速、分支阈值／时序、Shinuchi 预算／取整和魂槽增减／边界；已完成的 PlayMode 测试覆盖选曲 → 游玩 → 暂停/恢复 → 重开 → 返回、独立打开 SinglePlayScene、音乐时间同步、分支表现与结算，以及实际场景分数、魂槽、气球与连打。PlayMode 测试将实际场景渲染图输出到 `TestResults/`。

```sh
unity test . --mode EditMode --output TestResults/editmode.xml
unity test . --mode PlayMode --output TestResults/playmode.xml
unity run . -- -executeMethod OurTaiko.Editor.ProjectBuilder.BuildMac
```

Mac 开发构建输出到 `Builds/OurTaikoPlayerUnity.app`。以上构建与测试不代表移动设备的音频延迟已经校准。
