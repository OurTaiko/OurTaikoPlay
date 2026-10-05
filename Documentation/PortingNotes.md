# 单人游玩模块移植记录

当前版本使用打平的 Nijiiro 素材和 1920×1080 画布；下方早期 Green／1280×720 和简化计分／魂槽的记录保留为移植历史，当前规格见文末。

## 原版到 Unity 的对应

| 原版参考 | Unity C# 实现 |
| --- | --- |
| `src/libs/screen.*`、`src/scenes/game.*` 场景生命周期 | `SceneSwitcher` + `SongSelectScene` + `PlayScene` |
| `src/libs/parsers/tja.*` | `TjaParser` + `TaikoChart` |
| `src/objects/game/player.*` 的打击时窗、输入处理 | `PlaySession`，分离成可测试的纯 C# |
| `skin_config.json` / 各 `texture.json` 的位置、裁切 | `ProjectBuilder` 生成 Sprite 切片和可编辑的 RectTransform 层级 |
| 皮肤的背景/舞者动画行为 | C# `SpriteFlipbook`，选取一组舞者帧；不运行 Lua |
| 原生音频起停、重开 | Unity `AudioSource.PlayScheduled` + `AudioSettings.dspTime` |

原项目只读；图片直接复制，PNG 没有二次处理。未复制任何 C++、Lua、原生二进制或原项目联网代码。

## 已验证

- Unity 6000.3.25f1 成功编译并生成两个持久化场景，已加入 Build Settings。
- EditMode：12/12 通过，包含原 TRIPLE HELIX Oni 和校准谱完整解析及自动演奏、offset、空小节、BPM/拍号/滚动命令、长音符、错色输入、同帧连打及气球。
- PlayMode：2/2 通过，包含场景切换、音乐起播、暂停后时钟冻结、恢复音乐同步、重开清空成绩、返回后无多余游玩对象、单一 EventSystem / AudioListener / SceneSwitcher、直接打开 PlayScene。
- 1280×720 相机渲染图已人工检查：`PlayScene.png` 和 `SceneSwitcher.png`。
- macOS Development Player 构建成功，输出 `Builds/OurTaikoPlayerUnity.app`；日志 `Logs/BuildMac.log` 确认为 `Build Finished, Result: Success.`。未声称已完成独立播放器长时间手动游玩或移动设备验收。
- 两组测试的 XML 原始报告输出在忽略版本控制的 `TestResults/` 中。

## 当前范围

这是可运行的单人游玩基础模块，不是整个 OurTaikoPlayer 的等价移植。完整双人、段位、账号/联网、成绩上传、3D 咚角色及全部皮肤特效没有接入。Shinuchi 计分和单人魂槽数值已按原源码还原，详见本文末尾；大音符按设计单侧击打即可（不需要双手），自动连打仍为 15 次／秒。已支持 p/r 分支；s 分数分支、LEVELHOLD、BMSCROLL/HBSCROLL、字母扩展音符不支持。未知非时序命令会写警告，不支持的分支条件、滚动模式或音符会报错。

已验证桌面编辑器与原有两份谱面；分支验证使用新增 Branch Training 与边界测试谱。移动端触控布局与真机音频延迟尚未验收。

## 音符流速校正

初版 Unity 位置计算误用了 `时间差（秒）× BPM × 1.8 × SCROLL`。
原版 `Player::get_position_x/y` 的毫秒公式换为秒后应为：

```
距判定点的位移 = 时间差（秒）× BPM / 240 × SCROLL × (屏幕宽度 - 判定点X)
```

PyTaikoGreen 的设计宽度为 1280，判定点 X=414，移动距离为 866。正确系数是 866/240≈3.608333，初版约为正确速度的 49.8845%。BPM 160、SCROLL 1 时，已从 288 修正为约 577.333 设计像素/秒。SCROLL 1 的音符应在四拍内从右边缘到达判定点，与小节拍号无关；音画偏移只改变相位，不改变速度。

`NoteScroll.cs` 统一负责位移计算，`PlayScene` 从当前轨道宽度减去局部判定点位置得到移动距离。普通音符、小节线、X/Y 方向滚动共用这一计算。连打头尾始终使用头部的 BPM/SCROLL，以相同流速整体移动，长度由头尾时间差乘头部速度得到并保持恒定。连打期间的 BPM/SCROLL 命令继续影响后续音符，但不会改变当前连打尾部的流速。

原版另外还有玩家速度倍率（作用于横向 SCROLL）、BMSCROLL/HBSCROLL 的运行时 `scroll_multiplier`。Unity 目前只有普通滚动模式，未提供这些额外模式；本次对齐的是原版默认倍率的普通滚动。

校正后在当前 Unity Editor 中验证：EditMode 25/25、PlayMode 3/3 通过，其中包含实际帧间音符位移与原版公式的比较。测量使用画面该帧实际采用的 DSP 时间，避免混用随后音频块的实时钟值。JSON 报告保存在 `TestResults/scroll-editmode.json` 和 `TestResults/scroll-playmode.json`。本次更新了工程源码；`Builds/` 下先前的初版播放器未重新打包。

## 默认帧率与 FPS 显示

原代码已经设置 `Application.targetFrameRate = 120`，默认 Ultra 画质的 `vSyncCount` 也为 0，没有固定 60 FPS 的代码限制。部分其他画质档开启了 VSync；桌面端启用 VSync 时会覆盖 `targetFrameRate`，以屏幕刷新率控制渲染。现在 `SceneSwitcher.Awake` 明确关闭 VSync、设置每帧渲染并以 120 FPS 为目标，入口和独立打开 SinglePlayScene 都会应用。

两个场景左上角加入 `FpsCounter`，每 0.5 秒按实际帧数除以真实经过时间更新，不依赖歌曲时钟或 `Time.timeScale`，暂停与结果界面也持续显示。使用现有 uGUI / TMP，面板及文字不拦截点击。

本次 Unity Editor 实测约 120.1 FPS，系统报告的屏幕刷新率约 120 Hz，Game 视图 VSync 已关闭；没有复现固定 60 FPS，不能据此确认此前约 60 帧的具体原因。PlayMode 3/3 通过，覆盖直接进入场景时覆盖旧的 VSync/60 FPS/隔帧渲染配置、暂停且 `timeScale=0` 时仍有实测 FPS、场景切换后只有一个计数器，以及既有游玩流程和音符流速。构建目录中的旧播放器未重新打包。

## 分支逻辑移植

参考相邻仓库的 `src/libs/parsers/tja.cpp`（`handle_BRANCHSTART`、`handle_N/E/M`、`handle_SECTION`），以及 `src/objects/game/player.cpp`（`get_load_time`、`handle_branch_param`、`evaluate_branch`、`handle_section` 和判定计数）。新增实现全部为 C#，原仓库未修改。

- `p`：良计 1、可计 0.5、不可／漏音计 0，除以该统计区间已判定音符数，再乘 100、向下取整并限制在 0–100；没有已判定音符时为 0。
- `r`：统计普通／大连打的击打数，气球和彩球不计入。对判定时仍在持续的连打，取区间计数与当前连打总数的较大值，与原版一致。
- 达到玄人阈值但未到达人阈值，且玄人阈值非负时选择玄人；否则达到达人阈值选择达人；其余选择普通。`#SECTION` 和每次分支判定完成都会清零区间统计。
- 判定时机：先按原版在 SECTION 小节线（没有则分支起点）前两个四拍小节进入候选阶段；以达人路线首个对象的加载时刻决定路线，达人为空则依次尝试玄人、普通。加载时刻包含音符半宽 64；即使小节线被隐藏或是空小节，也保留其时间数据。负滚动用绝对速度，纯纵向滚动用纵向速度，静止对象以判定时刻作为加载时刻。
- 三路线共享起点，切换解析路线时恢复时刻、BPM、拍号、SCROLL、GOGO、小节线开关和气球游标。公共段从最后书写的路线末尾继续；支持相邻分支之间省略 BRANCHEND，以及在 END 前省略 BRANCHEND。缺失／重复路线、跨路线未闭合连打会明确报错。
- 未选路线的音符和小节线不显示，也不参与输入、漏音、自动演奏、分数或魂槽。基础分与魂槽分母按公共段＋达人路线计算，具体计分／魂槽公式沿用现有简化实现。连打头尾同速不变。
- 分支事件与 SECTION 按歌曲时间顺序处理；跨多帧或一次推进较长时间也不会把未来判定纳入过去的分支。暂停保留选择，重开创建新的统计与路线历史。

入口新增无音乐 `Branch Training`，依次验证命中率分支和连打分支。游玩页显示当前路线，分支起始小节线用黄色标记。参考模拟器没有实现 s 分数条件或 LEVELHOLD，本移植对此明确拒绝，而不猜测规则。

自动演奏连打仍沿用本移植已有的 15 次／秒，尚未移植原版按 BPM 变化的自动连打节奏；r 分支按本局实际连打次数判断。此次对齐的是分支条件、区间统计、路线选择和出现时机。

本次验证：EditMode **52/52**、PlayMode **6/6**。包含三路线实际显示、手动进入普通／玄人、自动演奏连续进入达人并完整结算、暂停后保持路线、重开清空分支历史，以及旧谱、流速、FPS 与音乐同步回归。原始 JSON 报告保存于忽略版本控制的 `TestResults/branch-editmode.json`、`TestResults/branch-playmode.json`。本次验证为 Unity Editor 内运行，没有重新构建 `Builds/` 中的独立播放器。

## 分支视觉对齐

参考 `src/objects/game/branch_indicator.cpp`、`player.cpp` 的绘制顺序，以及 PyTaikoGreen 的 `Graphics/game/branch/texture.json`、`game/animation.json`（41–45）和 `skin_config.json`。移除独立的 `BRANCH` 文本框，新增 C# `BranchLaneView`，复用七张原始 PNG：

- 普通路线保留深色底，玄人／达人分别叠加蓝色／紫色背景；覆盖区域为轨道局部 `(332, 1)`、`948×136`，最大透明度 0.5，与原版一致。
- 右侧使用「普通譜面／玄人譜面／達人譜面」精灵，局部位置 `(1071, 43)`、尺寸 `176×56`。背景与字样均放在音符、判定圈等前景下方。
- 切换先以二次缓出移动 20 像素（100 ms），再以二次缓出移动 70 像素并交叉淡化（133 ms）；升降级方向相反。提示图以 116 ms 放大到 1.2 倍并回弹，淡入后停留 1160 ms，再用 116 ms 淡出。同一路线不重播动画。
- 动画由游玩时钟推进，暂停时冻结；重开恢复普通路线。非分支谱面隐藏整组分支视觉。

Unity Editor 内 PlayMode **7/7** 通过，覆盖三路线实际选择后的精灵／位置／底色、升降级过渡与暂停冻结、同一路线不重复提示、重开恢复普通和非分支隐藏，以及原有音符流速、120 FPS 配置和音乐同步。原始报告为 `TestResults/branch-visuals-playmode.json`。已检查 1280×720 渲染图：[普通](BranchNormal.png)、[玄人](BranchExpert.png)、[达人](BranchMaster.png)。本次没有重新构建独立播放器。

## Nijiiro 素材与魂槽

两个场景的 CanvasScaler 和设计区域均为 **1920×1080**，默认播放器窗口也改为该尺寸。当前使用单套打平素材，128 张图片都已与来源逐字节核对；其中 Nijiiro 没有的资源直接从 Green 复制补齐，没有运行时皮肤继承、回退或皮肤切换。字体改用 Nijiiro 字体并重建 SDF，旧字体已解除引用并移除；咚／咔音效与舞者完整 19 帧也已替换。

音符按 Nijiiro 图集重新切为 192×192，判定点 X=618，轨道右边缘 X=1920，流速距离为 1302；分支对象加载边界包含音符半宽 96。鼓面、判定圈、背景、分支底色与字样采用原生尺寸。连打仍使用头部速度整体移动，头尾同速。

魂槽参考 `src/objects/game/gauge.cpp`、`YataiDONNijiiro/Graphics/game/gauge/texture.json`、`skin_config.json` 的 `gauge_cells=50` / `gauge_cell_fade_in=true`，以及 `game/animation.json` 的 10、25、63 号动画，全部用 C# 实现：

- 50 格，每格宽 21。普通红色段高 33；过关后的首格使用圆角黄色过渡贴图，之后由 34 高的上段与 32 高的下段填满，不再把整根条都当成红色图片缩放。
- Easy 过关线为 60%，Normal／Hard 为 70%，Oni／Edit 为 80%；底板、分隔线、彩虹和过关文字位置采用对应档位。结果页与魂槽显示使用同一阈值，原有简化加减槽数值保持不变。
- 新增格子按原代码用 **450 ms 淡入**，完成后交给实心条绘制，避免多出一格或残留亮格。当前参考代码没有固定周期的整条黄色闪烁，本次没有猜测一个 0.5 秒周期加入。
- 满槽后彩虹用 **450 ms 淡入**，同时按源码每 **75 ms** 向下一张过渡，**8 帧／600 ms** 循环。魂火按 **50 ms／帧、400 ms 循环** 显示；魂字叠层按源码指定帧点亮。掉出满槽后立刻撤下彩虹和魂火，再次填满重新开始淡入。
- 网格叠层 alpha 使用当前 `gauge.cpp` 的 0.15；未过关／过关分别使用暗／亮「クリア」和魂字。暂停冻结动画，重开清空。

当前渲染示例：[过关黄色区](GaugeClear.png)、[满槽彩虹](GaugeRainbowA.png)、[彩虹另一时刻](GaugeRainbowB.png)。历史分支与游玩截图已更新为 Nijiiro 的 1920×1080 版本。

本次 Unity Editor 验证：EditMode **57/57**、PlayMode **8/8**。覆盖三档过关线前后、50 格宽度／黄色高度、新格淡入、彩虹循环及帧间混合、掉槽后撤下特效、再次填满重置、暂停与重开，以及现有分支、滚动、音乐同步、默认 120 FPS 和菜单流程。报告保存在忽略版本控制的 `TestResults/nijiiro-editmode.json` 与 `TestResults/nijiiro-playmode.json`。没有重建 `Builds/` 下的独立播放器。

## 气球音符对齐

7 号气球的脸位于 Nijiiro 192×192 切片的 `(114, 96)`，比切片中心靠右。原版 `player.cpp::draw_balloon` 会扣除 `balloon_offset`；原始配置为 12／128，换算到 Nijiiro 设计尺寸是 18／192。Unity 原先把整张切片居中，导致气球的脸向右偏，已在修复前的运行测试中复现。

现在用音符宽度的 **12/128（9.375%）** 设置贴图的相对锚点，同时让贴图尺寸跟随音符容器。实际偏移随音符大小及 Canvas 缩放，未写死屏幕像素，也没有新增皮肤继承。普通音符与 9 号彩球保持居中；判定时刻、停留规则及流速计算保持原样。

新增运行测试检查移动中的脸位置、到达判定圈后的击打对齐、提前击打无效及打破后隐藏；并验证 96／192／288 的音符尺寸，以及 1280×720、1920×1080、2560×1440、1280×1024 四种渲染分辨率的屏幕坐标对齐。示例：[气球停在判定圈](BalloonAtJudge.png)。

本次 Unity Editor PlayMode **9/9** 通过，包含气球、分支、魂槽、流速、音乐同步和场景流程。报告为 `TestResults/balloon-playmode.json`。未重建独立播放器。

## Nijiiro 气球剩余次数

参考 `src/objects/game/balloon_counter.cpp`、`Player::check_balloon`／`balloon_counter_manager`，以及 Nijiiro 的 `game/balloon/texture.json`、`balloon_counter_margin` 和 6／7 号动画。新增的 `bubble.png`、`counter.png`、`pop.png` 直接复制到打平的素材目录，运行逻辑全部为 C#。

- 与原版相同，第一次有效咚击打后显示气泡和「总次数 − 已击打次数」，咔不会触发。数字从原生图集切成 10 张 77×90 精灵，字间距为字宽的 64/77，并按位数居中；支持跨位数递减，不再在右下角用 `BALLOON` 文本显示 7 号气球计数。
- 数字沿用 50 ms 拉伸＋116 ms 分段回弹；气球依击打进度使用 0、2、3、4、5、6 帧，打破后显示第 7 帧和数字 0，整体用 166 ms 淡出。未打破到期立即隐藏，下一只气球独立计数。
- 补上原版 `notes/10` 的气球尾部。移动时尾部与头部通过相对锚点衔接；击打后换成膨胀素材，当前对齐的气球脸保留。气泡使用 Nijiiro 原生布局，通过现有 CanvasScaler 适配分辨率。
- 动画跟随游玩时钟，暂停冻结，重开清空；手动和自动演奏使用同一计数事件。9 号彩球仍沿用当前显示，未混用 7 号气球的素材。

渲染示例：[Nijiiro 气球剩余次数](BalloonCounter.png)。

本次 Unity Editor PlayMode **10/10** 通过，覆盖 10→9 的位数变化、三位数字布局、精确动画时刻、暂停、未打破到期、连续气球、重开与自动演奏，以及已有的多分辨率对齐、分支、魂槽、流速和场景流程。报告保存为 `TestResults/balloon-counter-playmode.json`。没有重建独立播放器。

气球破裂音效使用 Nijiiro 的 `Sounds/game/balloon_pop.ogg`，原样复制到 `Assets/OurTaiko/Audio/balloon_pop.ogg`（约 0.414 秒）。按 `Player::check_balloon`，达到要求击打次数的那次判定通过现有打击音效通道播放一次，手动和自动演奏共用；未打破到期不播放。短音效预加载并在加载时解压，播放独立于气泡淡出。已核对源文件一致、音频数据可读取且非静音；气球专项 PlayMode 回归 1/1 通过（含手动／自动吹爆和预加载引用），报告为 `TestResults/balloon-pop-audio-playmode.json`。

## 大小连打身体与尾部

参考 `Player::draw_drumroll`、`TextureWrapper::draw_texture`／`read_tex_obj_data` 和 Nijiiro 的 `game/notes/texture.json`。原 Unity 版把大小连打都画成固定高度的纯色矩形，缺少尾部；现改用已有 Nijiiro 图集的四张切片，没有增加运行时皮肤依赖：

- 小／大身体分别使用 `(0,1544,72,192)`、`(72,1544,72,192)`，只沿长度方向拉伸，完整保留各自的粗细、边框和透明区域。
- 小／大尾部分别使用 `(0,2120,80,192)`、`(0,2312,120,192)`，放在连打结束位置，并保持原始宽高比。绘制顺序为身体、尾部、头部，让头部覆盖起点接缝。
- 原版目标宽度会加上身体贴图本身的宽度，再加 `drumroll_width_offset`。Nijiiro 下是 `长度 + 72 - 70.5`，即重叠 1.5 设计单位；Unity 用音符高度的 `1/128` 表达重叠量，随音符与 Canvas 缩放。反向滚动将身体和尾部沿水平方向翻转，圆端朝外；头部表情保持正向。
- 图集改为与原版裁剪贴图相同的 Point 采样，避免身体边界混入邻近帧，在小连打接缝处产生竖线和杂色。
- 长度仍按头部 BPM／SCROLL 计算，身体和尾部与头部整体移动。`NoteScroll.RollLength` 未改动，连打中途的 BPM／SCROLL 命令不会让头尾采用不同速度。

已检查 1080p／720p 渲染图：[小连打](DrumrollSmall.png)、[大连打](DrumrollBig.png)。专项测试还覆盖反向、静止、极短连打、接缝重叠、绘制层级以及中途变速时的恒定长度。

本次 Unity Editor EditMode **57/57**、PlayMode **11/11** 通过，含新增连打渲染测试及已有分支、魂槽、气球与场景流程回归。报告保存为 `TestResults/drumroll-editmode.json`、`TestResults/drumroll-playmode.json`。没有重建独立播放器。

## 全局 SceneSwitcher 与测试入口（2026-09-30）

场景切换架构按用户指定参照相邻 MajdataPlay 的 `Assets/Scripts/Global/SceneSwitcher.cs`。原 `Assets/Scenes/SceneSwitcher.unity` 通过 `AssetDatabase.MoveAsset` 改名为 `Test_DefaultScene.unity`，保留 GUID 和测试选曲布局，只移除旧场景内的切换脚本对象，并更新 Build Settings 和 Editor 工具路径。

`Assets/OurTaiko/Resources/SceneSwitcher.prefab` 是独立全局 uGUI 控件，由 `BeforeSceneLoad` 自动创建，跨场景保留，启动时完全打开。任何场景的切换从 `SwitchScene`／`SwitchSceneAsync` 发起；现有 `Play`、`Restart`、`ReturnToMenu` 均转交同一流程，运行代码中只有控件内部调用 `SceneManager.LoadSceneAsync`。控件统一维护 `CurrentScene`、`LastScene`、`MainCamera` 和 `OnSceneChanged`。控件没有自己的 EventSystem、Camera 或 AudioListener。

切换流程为：锁定键盘与指针输入、发出 `SceneChanging` 通知游玩冻结并停止音频，0.9 秒关闭遮罩，等待准备 Task，异步加载，等一帧和 50 ms，0.8 秒打开遮罩。采用 MajdataPlay 相同的 OutQuint 曲线和实时时钟，以纯 C# 实现 uGUI 淡入淡出；没有移入 MajdataPlay 的皮肤、灯光硬件、UniTask／LitMotion 依赖或三角网格 Shader。准备任务失败／取消时打开旧场景，销毁控件会取消未完成的切换；重复请求不发起第二次加载。

`SwitchSceneAfterTaskAsync` 支持普通和带结果的 .NET Task。`autoFadeOut: false` 可保持加载后的遮罩，让目标场景完成初始化后调用 `FadeOut`／`FadeOutAsync`。接口命名遵循 MajdataPlay：`FadeIn` 关闭遮罩，`FadeOut` 打开遮罩；`SetLoadingText` 可更新提示及颜色。游玩先在遮罩后初始化，等遮罩打开再开始完整倒计时并安排 DSP 音乐起播。直接打开 SinglePlayScene 仍可运行。

新增 `GlobalSceneSwitcherTests` 覆盖全局预制体、独立生命周期、准备任务等待、切换时遮罩和输入阻挡、timeScale=0、重复请求、当前／上一场景及相机、手动揭示、泛型结果、同场景重开、无效目标／任务失败／取消恢复与销毁取消。既有渲染测试现在明确选择活动场景的 Canvas，避免误选持久化的过渡 Canvas。

本次 Unity Editor 完整验证：EditMode **57/57**、PlayMode **15/15** 全部通过，报告为 `TestResults/global-switcher-editmode.json`、`TestResults/global-switcher-playmode.json`。已检查测试入口和游玩页渲染图；原入口仅移除旧控件对象，改名后 GUID 保持不变。Nijiiro 字体资源测试后与操作前快照完全一致。未重新打包独立播放器。

另在没有 LaunchMenu、PlayScene、Camera 或 EventSystem 的临时空场景进入 Play mode，确认 `BeforeSceneLoad` 独立自动创建了唯一 SceneSwitcher，位于 `DontDestroyOnLoad`，不依赖具体场景脚本调用。实际全屏遮罩和 LOADING 提示已检查，截图保存在忽略目录 `TestResults/SceneTransition.png`；检查后已退出 Play mode 并重新打开 Test_DefaultScene。

## Shinuchi 计分与魂槽数值（2026-09-30）

先将上一项全局场景切换改造提交为 `a34ee00 refactor(unity): make scene switching a global control`，再实施本次规则还原。来源为相邻 OurTaikoPlayer 的 `src/libs/parsers/tja.cpp::calculate_base_score`、`src/objects/game/player.cpp::reset_chart/check_note/check_drumroll/check_balloon/check_kusudama`、`src/objects/game/gauge.h` 与 `gauge.cpp`。参考仓库仅读取。

模块边界如下：`ChartStatistics` 固定统计公共段和每段达人路线；`ShinuchiScore` 只管理基准分和累计分；`SoulGaugeRules` 只定义难度／星级表；`SoulGauge` 管理数值、百分比及过关状态。`PlaySession` 管理判定与分支，并把判定分发给两个数值模块。`PlayScene` 将结果交给 HUD 和 `SoulGaugeView`，后者仅负责皮肤、格子及动画。

真打基准分为 `ceil((1000000 - 气球预算次数×100 - 连打预算毫秒×16.920079999994086f/1000×100) / 普通音符数 / 10)×10`，保留原 `float` 常量的精度及运算顺序。每个 7／9 号预算次数取 `min(100, 要求次数)`；未写 BALLOON 时按原解析器默认 1。没有普通音符时基准分为 1000000。良得基准分，可按原整数除法得到一半并取 10 分整数倍，不可／漏音为 0；大音符、GOGO、连击不改变分值。5／6／7／9 号每次有效击打均为 100，吹爆不加 GEN3 的 5000 分。累计分不限制为 100 万。

原版连打预算读取头后第一个谱面对象，不一定是尾，跨小节或源文本分段时可能被隐藏小节线截短。初版曾保存 `NextObjectTime` 复现这一行为，后续按用户要求修复：预算直接使用 `(EndTime - Time)×1000`，删除该字段和专用解析标记。现在使用对应头尾的完整时长，隐藏小节线与分行不改变基准分；位移继续使用头部 BPM／SCROLL，头尾同速。

魂槽上限 10000；良为 `1000000 / (max(1, 普通音符数) × soul_percent)`，可／不可乘相应查表倍率。每次判定后限幅到 0–10000，距过关或满槽边界小于 `1e-6` 点时归整；百分比为 `floor((points + 1e-6)/10000×100)`。Easy／Normal+Hard／Oni+Edit 的过关点分别为 6000／7000／8000。LEVEL 缺失或为 0 时按原 `reset_chart` 使用 Oni ★10，其他星级限到查表的 ★1–10；原表未使用行保持零值。Normal／Hard 的部分倍率在原源码中标记为 assumed，本次保留原表，不另作猜测。

长音符击打不增减魂槽，也不重启格子淡入。普通判定逐次更新魂槽显示，结果页直接采用同一数值模块的过关状态。沿用现有 50 格、450 ms 淡入、满槽彩虹与魂火表现。现有自动连打 15 次／秒及大音符单侧输入仍属之前的实现；本次没有扩展为原版自动连打节奏或双手判定。

完整 Unity Editor 验证：EditMode **97/97**、PlayMode **16/16** 通过。新增逻辑测试覆盖基准分／可的 10 分取整、超过 100 万、长音符预算和实际得分、各路线固定分母、独立逗号和空小节、魂槽不同难度／星级、缺失 LEVEL、未使用行、过关／满槽归整、手动不可与漏音一致。新增 `ScoreGaugeFlowTests` 验证保存场景的分数 HUD、魂槽过关与满槽、长音符不重启淡入、结算及重开清零，已有全部场景、音频和表现回归通过。报告为 `TestResults/shinuchi-editmode.json`、`TestResults/shinuchi-playmode.json`。字体资源与本次操作前快照完全一致，未重新打包独立播放器。

## 连打计分预算时长修复（2026-09-30）

按用户要求修复参考实现的 nextobj 缺陷：`ChartStatistics` 直接累加 `(EndTime - Time)×1000`，不再以连打头后的第一个对象估算时长。删除 `ChartNote.NextObjectTime` 和解析器的专用分段标记／记录逻辑。预算现在包含跨小节、源文本分行、空小节及 DELAY 的完整头尾时间差；头部 BPM／SCROLL 控制位移的规则不变，参考仓库未修改。

逻辑测试验证小／大连打、隐藏小节线、分行、中途变速、独立逗号、空小节和 DELAY 的时长；另验证完整 3 秒连打的基准分不受分行影响，以及选中普通路线时仍按公共段＋达人路线完整连打预算计分。Unity Editor EditMode **101/101**、PlayMode **16/16** 全部通过。报告为 `TestResults/shinuchi-tail-editmode.json`、`TestResults/shinuchi-tail-playmode.json`；字体与本次操作前快照一致。

## 选曲与结算场景（2026-10-01）

- 新增 `SongSelect.unity`、`Result.unity`，入口 → 选曲 → 游玩 → 结算 → 选曲全部经由全局 SceneSwitcher；`SceneSwitcher.Play(song, course, auto)` 记录发起场景作为返回目标，`ShowResult` 交付 `PlayResult`。游玩结束时 `PlayScene.Finish` 生成结果并切到结算，由 `ResultScene.Awake` 保存本地成绩或发起在线上传（2026-10-04 调整），不再显示场景内结果面板（谱面加载失败仍用该面板提示）。
- 选曲参照 `scenes/song_select.cpp`、`objects/song_select/player.cpp`、`file_navigator/navigator.cpp` 与 Nijiiro `Scripts/song_select/song_select.lua`：纵向画廊（中心 y=540，行距 135，展开间隔 120，斜移 40/行，移动 166 ms 三次缓出）；选中板在导航后等待 61 帧@120fps（508.33 ms）再按 `anim/song_board` 的 select_on 展开，收起 13 帧（216.7 ms）；进入场景与从难度返回时立即展开。难度面板淡入为 400 ms 延迟 + 483 ms；其他曲目板 800 ms 退出并 166 ms 淡出，返回时 500 ms 归位。初始光标遵循 `last_difficulty`（初值 -1 → もどる）。音色面板未移植，因此光标按 option_neiro_row 布局：首个难度 ↔ 扳手 ↔ もどる。
- 结算参照 `scenes/result.cpp`、`objects/result/player.cpp` 与 Nijiiro `Scripts/result/*.lua`：淡入（100 ms 延迟 + 316.67 ms）→ 等待 100 帧 → 每格 7 帧填充魂槽 → 等待 100 帧 → 各行每 50 帧落定、总分再 100 帧 → 500 帧后皇冠 → 过关时再 150 帧显示评语与金色背景（未过关立即显示）。演出结束后需等待 500+500 帧才可返回，3600 帧后自动返回。ScoreRank 已补齐：达到等级门槛后在皇冠前插入 2 秒演出，详见「ScoreRank」章节。
- 动画曲线全部来自原 `Scripts/anim/*.lua` 导出表（云层、富士山、成功背景、皇冠、皇冠光芒、评语气泡、数字弹出、魂火、彩虹、最高分条、曲目板、光晕、おに／裏交替），原样复制为 `.txt` 后由 `LumenClip` 线性采样并在首末行处截断。
- TMP 的 Mobile SDF 只有在 `OUTLINE_ON` 关键字下绘制描边；新增 `Generated/Nijiiro SDF Outline.mat` 供新场景文字使用，使该着色器变体也会进入播放器构建。
- 修复：歌曲 AudioClip 为 DecompressOnLoad 且不预载，首次 `PlayScheduled` 会同步解码约 1 秒；当时改为在 PlayScene 初始化时（全局遮罩仍关闭）调用 `LoadAudioData`。2026-10-03 原生音频重做后，此步骤仅保留给 Unity 后端；BASS 后端在幕布下直接准备原始编码文件。
- 验证：EditMode 108/108、PlayMode 18/18（`TestResults/songselect-editmode.json`、`songselect-playmode.json`）。`SongSelectResultTests` 覆盖选曲、难度光标、裏切换、自动演奏开关、游玩、结算跳过与返回，以及未过关结算；截图在 `TestResults/SongSelect*.png`、`Result*.png`。PlayMode 测试使用临时成绩文件，不写入玩家数据。

## 游玩页触控鼓（2026-10-01）

游玩场景原先的四个矩形「D / KA」「F / DON」等按钮替换为原版触控鼓外观。原版在 `OurTaiko.cpp` 中于开启 `touch_input` 时把 `global/overlay/touch_drum.png`（Nijiiro 版，1920×1080 半鼓）作为全局叠加层以 `fade=0.5` 绘制；Unity 按要求只放在 SinglePlayScene，位于暂停／结果面板之下。按下时重启全局动画 66（Nijiiro 无全局 `animation.json`，取 PyTaikoGreen：`texture_resize` 70 ms，1.0→0.95，`reverse_delay` 0，二次缓出），以中心缩放再下移 `h/2×(1-scale)`，等价于底边中心为轴；使用真实时间，暂停时也播放。原版在其他手指仍按住时抬起一根也会重启该动画，Unity 只在按下时重启。

判定区照搬 `input.cpp::touch_quadrant_vkey`：上半屏左右为咔；下半屏中以设计区底边中心为圆心、横纵半径为宽度 ×0.262／×0.242 的椭圆内为咚，椭圆外为咔，左右按中线划分。区域按 1920×1080 设计区比例计算，随 Canvas 缩放；与原版一样覆盖整个屏幕（含留边）。为不吞掉 PAUSE／RESTART／BACK 等 uGUI 按钮，落在 `Selectable` 上的点不计为打击。场景通过 `ProjectBuilder.ApplyTouchDrum()` 定向迁移，PlayMode 测试 `TouchDrumMatchesOriginalZonesAndSqueeze` 覆盖外观、各区域边界、按钮避让与缩放动画。

## 游玩场景改名（2026-10-01）

`Assets/Scenes/PlayScene.unity` 通过 `AssetDatabase.MoveAsset` 改名为 `SinglePlayScene.unity`，GUID 不变，Build Settings 由 Editor 自动更新。`SceneSwitcher.GameScene` 改为 `"SinglePlayScene"`，ProjectBuilder 各迁移入口使用新路径。场景内控制组件类 `PlayScene`（`Runtime/Play/PlayScene.cs`）未改名。本文更早的记录中的 PlayScene 场景名与 `PlayScene.png` 截图名保留为历史。

## 演奏オプション（2026-10-01）

- 面板逻辑照 `objects/song_select/modifier.cpp`（`ModifierSelector`），绘制照 Nijiiro `song_select.lua::draw_option_board`（Nijiiro 用它取代 C++ 绘制）。Nijiiro 开启 `option_skip_row`、`option_neiro_row`，所以 7 行：オート、はやさ、ドロン、あべこべ、ランダム、演奏スキップ、音色。咔改值并播放咔音，咚进入下一行，最后一行后确认；音色变化时预听新音色的咚（無音不播）。打开时播放 `voice_options_1p`。纯逻辑在 `Runtime/Core/OptionMenu.cs`，绘制在 `Runtime/Scenes/OptionPanel.cs`。
- 布局（设计像素）：`modifier/top` 静止于 (5,532)，1P 标 (32,549)，标题「演奏オプション」字号 32 居中于 (215,581)；第 i 行 `mod_bg` 在 (31, 618+61i)，数值框 (208, 626+61i)，图标 (165, 626+61i)，箭头 (214, 630+61i) 与镜像 (354, …)，行名 x=44，数值居中 x=300，字号 26。非默认值数值框染黄 (255,255,0)；光标行高亮按 `anim/option_cursor`（复制为 `Animations/option_cursor.txt`，60 帧循环）脉动；按下的箭头外移 5 px，250 ms 二次缓出回位；灰显行为 (166,168,171) 底板加 50% 黑色遮罩。Nijiiro 的 Lua 绘制没有 C++ 版的数值横向滑动，因此不移植。
- 滑入／滑出取 `song_select/animation.json` 的 28／39：333.33 ms 移动 548 px，经 75 px 过冲，分段线性（入：73.68% 时到 623；出：21.05% 时到 −75）。滑出结束后保存设置，对应 `Player::update` 中的 `save_player_data`。滑入／滑出期间输入都交给面板（与原版一样，确认后的咔／咚被忽略）。
- 设置为 `Runtime/Core/PlayOptions.cs`（`options.json`），对应 `PlayerData` 的 `modifier_*` 与 `neiro_index`（無音 = −1）。オート即原自动演奏开关，A 键与面板共用并立即保存。
- 游玩：`ChartModifiers.Apply` 在 `PlaySession` 之前修改谱面（同 `Player::reset_chart` 在计算出现时间前调用 `apply_modifiers`），顺序为あべこべ → ランダム → はやさ；はやさ只乘 `ScrollX`（含小节线），因此出现时间和分支选线时机也随之变化。音色按 `game.cpp` 读取 `hit_sounds/<neiro>/don.ogg`、`ka.ogg`（21 套原样复制到 `Audio/hit_sounds/`，名称来自 `neiro_list.txt`，汇总为 `Generated/HitSounds.asset`）；無音时不播放打击音。
- 有意偏离（用户决定，不要按原版还原）：
  - **ドロン只隐藏音符**（含连打身体与气球），小节线保留；原 `modifier_display` 对小节线也设 `display=false`。
  - **ランダム按每个咚／咔音符独立概率换色**：きまぐれ 30%、でたらめ 50%。原 `modifier_random` 从全部对象（含小节线、连打、气球）中抽 (总数/5)×档位 个再只换咚咔，实际比例偏低且随谱面变化。
  - **演奏スキップ灰显**：原版需要空闲的 2P 轨道接收咔；单人场景没有 2P 鼓，按原「grayout」样式显示且不可改，跳过功能未实现。
  - **轨道徽章用整数行**：`Player::draw_modifiers` 按 Nijiiro `mod_badge_grid`（x 170、y 77、44×44、3 列）排列，原代码行号用浮点 `slot/3.0` 导致第 2、3 个徽章下移约 15、29 px，此处按整数行。徽章素材为 Nijiiro `game/lane/mod_speed_x*` 与 `mod_doron` 等；`mod_shinuchi` 未添加。
  - 触控扩展：点行名选中该行（已选中时等于咚），点数值框左／右半边等于该行的左／右咔，点面板外或按 Esc 一次确认全部并滑出。原版只有键盘。
- 迁移入口 `ProjectBuilder.ApplyPlayOptions()`（菜单 OurTaiko/Apply Play Options）：导入素材、生成 `HitSounds.asset`、为 SongSelect 绑定 `optionArt`，为 SinglePlayScene 的 NoteLane 添加 `ModifierBadges` 并绑定音色库。
- 验证：EditMode 115/115、PlayMode 22/22（`TestResults/options-editmode.json`、`options-playmode.json`）。截图 `TestResults/SongSelectOptions.png`（面板）、`PlayOptionsBadges.png`（x2.0／ドロン／あべこべ 徽章，音符隐藏、小节线保留）。没有重建独立播放器。

## 音符文字（2026-10-01）

- 分配：`Core/NoteMoji.cs` 对应 `tja.cpp::modifier_moji`、`find_streams`、`get_note_interval_type`。基础帧 咚0／咔3／大咚5／大咔6／连打7／大连打8／气球9／尾10／彩球11；8、12、16、24、32 分依次查找连续段（后者覆盖前者），段内除最后一个外咚→1（ド）、咔→4（カ），长度恰为 3 且全为咚时中间→2（コ）。间隔以前一对象的 BPM 判定，容差 15 ms，判定顺序 8／16／12／24／32／4 分。
- 列表：原版对公共 NoteList 与各分支各路线的 NoteList 分别执行，列表内含小节线与 8 号尾。解析器因此记录 `TaikoChart.NoteLists`（`ChartEntry` 区分头／尾／小节线），不使用按时间排序后的 `Notes`。小节线参与连续段：跨小节的连续 8 分在小节线处结束一段，小节线前的音符为ド。原版把一小节拆成多行时会多插隐藏小节线，此差异不复制。
- 渲染：`player.cpp::draw_notes` 第二遍绘制全部文字，因此 `MojiClip`（RectMask2D，X 同 `LaneClip`，高为整条轨道 264）紧排在 `LaneClip` 之后；层内与音符一样早的在上。文字中心 = 音符中心下移 123。连打：`moji_drumroll_mid` 从头部起宽 8＋长度，然后头字、尾字；Y 不随 SCROLL 虚部（同原版 `draw_drumroll`）。气球文字跟随气球（到判定点后停住），计数器显示时隐藏（`skip_note`）。
- 有意的统一：显隐跟随音符（命中即消失、漏音继续流过判定点、ドロン同时隐藏）。原版 ドロン 仍绘制文字，按用户要求与音符行一致而隐藏。
- 验证：EditMode `NoteMojiTests`，PlayMode `NoteMojiFlowTests`（帧、层级、位置、命中／漏音／ドロン、连打横条），截图 `TestResults/NoteMoji.png`。EditMode 127/127、PlayMode 24/24 通过。

## 选曲加载幕布与 SongLoadingScene（2026-10-01）

- 来源：`Scripts/global/transition.lua`（`SongTransition:update/draw_bg`）、`anim/loading_song.lua`（街机 `loading/loading_song.nulm`，60 fps）、`src/objects/game/transition.cpp::draw_song_info`、`song_select.cpp::select_song`、`game.cpp`；素材 `global/rainbow_transition` 原样复制，时间轴为 `Animations/loading_song.txt`，由 `LumenClip` 采样。
- 流程：`SceneSwitcher.Play()`（SongSelect 决定与 Test_DefaultScene 开始）→ 幕布关闭（532 ms 映射帧 5→55，标题在 song_info_fade 的 266 ms 延迟后 266 ms 淡入）→ 加载 `SongLoadingScene`，幕布停在帧 55 → 解析 TJA 并应用演奏オプション、`LoadAudioData` 载入歌曲，至少停留 2 秒（用户决定；原模拟器无停留、街机停到加载完成）→ 加载 SinglePlayScene，幕布在游玩场景上打开（532 ms 映射帧 60→109，标题 133 ms 淡出）→ 打开结束后才开始倒计时。重开（F1／暂停／结算）与其他切换保持原淡入淡出。
- 架构：幕布是 `Resources/SceneSwitcher.prefab` 内的 `SongTransition`（`Runtime/Scenes/SongTransition.cs`），作为第二种过渡样式（`TransitionStyle.Curtain`）。遮罩在打开前保持自身样式：幕布停住时，下一次切换跳过关闭，并以幕布打开新场景。`SongLoadingScene` 场景只有黑底相机与 `SongLoadingScene` 组件，画面全部来自全局幕布；直接运行该场景会立即停住幕布。解析结果通过 `SceneSwitcher.SetPreparedChart`／`TakePreparedChart` 交给 `PlayScene`（只取一次，重开重新解析与重抽ランダム）。
- 图层与数值：帧 35–78 用整张 `rainbow_bg`，其余为 `rainbow_bg_top` 左右半幅（中心 tx、宽 960×sx）；底部光晕 `rainbow_bg_bottom (0,288,1600,512)` 画在 (0,568,1920,512)，加算混合（`Generated/UI Additive.mat`，`Mobile/Particles/Additive`）；11 颗星为固定位置与缩放；咚／咔 560×560 沿轨迹；标题带 (0,0,1600,256) 中心 (960,400)，打开时 sx 0.25→1、关闭时 sx→1.5／sy→0.1；提示区为皮肤 `chara_center.png` 原图（用户决定，截图中的街机插画提示卡不在皮肤内）。标题／副标题中心 y=382／462（skin 1606／1686 减 rainbow_up 816×1.5），64／40 px 白字黑边；TMP 描边以字形边缘为中心，因此同时扩张字面使边框全部在外侧。
- 演奏スキップON 徽章仅在跳过功能启用时显示；本项目该选项灰显未实现，因此不显示、也未复制素材。
- 验证：PlayMode `SongLoadingCurtainTests`（帧映射、停住状态、2 秒停留、音频已载入、预解析谱面被取用、在旧／新场景上播放关闭／打开、重开仍为淡入淡出），截图 `TestResults/CurtainClosing.png`、`SongLoading.png`、`CurtainOpening.png`。EditMode 127/127、PlayMode 26/26 通过。迁移入口 `ProjectBuilder.ApplySongLoadingCurtain()`（菜单 OurTaiko/Apply Song Loading Curtain）。

## 音符飞向魂槽与 GaugeHitEffect（2026-10-01）

- 来源：`src/objects/game/note_arc.cpp`、`gauge_hit_effect.cpp`、`player.cpp::note_correct`／`check_drumroll`／`draw_overlays`；Nijiiro `Graphics/skin_config.json` 的 `notes`、`note_arc_start_x_offset`、`gauge_hit_effect_note`、`note_arc_pivot`、`note_arc_duration`。
- 路径取舍：OurTaikoPlayer 当前代码是二次贝塞尔（22 帧、`note_arc_curve_height` 608）；Nijiiro 皮肤另外声明了街机实测的圆弧（`note_arc_pivot` (1269.22,415.14)、`note_arc_duration` 30 帧），读取它们的实现在 OurTaikoPlayer 提交 `7a08ced` 中、后来合并时被丢弃，但皮肤键保留。本项目只做 Nijiiro，按皮肤键实现：音符中心以等角速度沿圆周从判定圈（轨道局部 618,110）经上方扫到魂徽章（1834,-30），半径取起止两半径的平均（约 719.14），-154.89°→-38.24°；时长 30×16.67 ms。两种路径顶点同为判定中心上方 409，飞出设计区顶边的部分由 `NoteArcs` 的 RectMask2D 裁掉。
- 触发：良／可的 1–4 号音符飞自身贴图；5／6 号连打每次有效击打飞一个小咚或小咔（按所敲的鼓，自动演奏为咚）；7 号气球只在吹爆时飞一个气球；不可、彩球（9 号）不飞。到达后的那一帧即移除，不在终点绘制。
- 实现：`Core/NoteArcPath.cs`（纯 C# 路径）、`Play/NoteArcView.cs`（对象池；后生成的画在上面）。`NoteArcs` 层铺满 1920×1080 设计区、紧排在 `SoulGauge` 之后（原版先画魂槽再画 `draw_overlays`），坐标经 `NoteLane` 的变换换算，不写屏幕像素。时间用歌曲时钟，暂停时冻结、重开随场景重载清空。迁移入口 `ProjectBuilder.ApplyNoteArcs()`（菜单 OurTaiko/Apply Nijiiro Note Arcs）。
- GaugeHitEffect：音符到达时（按到达时刻 `开始+30 帧` 计时）在徽章中心 (1834,-30) 播放；同时只有一个，新到达的清除旧的（`gauge_hit_effect.clear()`）。按 Nijiiro `animation.json`：2 号换帧 `gauge/hit_effect` 三帧 232×232（≤33.33 ms 帧 0、≤66.66 ms 帧 1、之后帧 2）；32 号尺寸 116.67 ms 前为 0.8，随后 266 ms 线性到 1.5，以中心缩放；33 号 300 ms 后 83 ms 淡出，383 ms 时移除。光圈按尺寸着色（raylib 色值）：≤0.80 黄 (253,249,0)、≤0.90 橙 (255,161,0)、其余红 (230,41,55)，即约 155 ms 后转红。音符贴图画在光圈之上、同一中心，与光圈同步淡出。31 号（光圈淡入）只作用于 `hit_effect_circle*`，Nijiiro 中这两张是全透明 8×8 占位图；34 号旋转固定为 0。两者因此不绘制（2026-10-02 起这两张图不再导入项目）。
- 实现：`Generated/Clips/GaugeHitEffect.anim`（时间轴与着色，2026-10-02 由 `GaugeHitEffectTiming` 转为剪辑）、`Core/GaugeHitEffectLayout.cs`（位置）、`Play/GaugeHitEffectView.cs`；`GaugeHitEffect` 层紧排在 `NoteArcs` 之后（`draw_overlays` 先画 arc 再画 gauge_hit_effect），帧在 `game/gauge/hit_effect.png` 的导入设置中切片（`GaugeHitEffect0–2`）。由 `NoteArcView.ShowTime` 在处理完 arc 后驱动，同属迁移入口 `ProjectBuilder.ApplyNoteArcs()`。
- 未移植：气球吹爆时的彩虹拖尾（`balloon/rainbow`／`note_arc_balloon_*`）。
- 验证：EditMode `NoteArcPathTests`（圆心、半径、起止角、等角速度、30 帧）、`GaugeHitEffectTimingTests`（换帧、尺寸、淡出、着色），PlayMode `NoteArcFlowTests`（触发规则、贴图、层级、起点在判定圈、终点与光圈在魂徽章中心、到时移除、光圈帧／尺寸／颜色／淡出），截图 `TestResults/NoteArc.png`。EditMode 131/131、PlayMode 27/27 通过。

## 名牌、PlayerInfoController 与分数计数器（2026-10-02）

- 来源：`src/objects/global/nameplate.cpp`、Nijiiro `Scripts/global/nameplate.lua`（街机 `name_plate.nulm` 的标签与文字框）、`Graphics/global/nameplate/texture.json`；位置取 `skin_config.json` 的 `game_nameplate_1p` (-44,161)（相对轨道 y）、`song_select_nameplate_1p` (14,908)，以及 `result_player.lua` 的 `nameplate_pos` (2,922)。数据字段对应 `scores.h` PlayerData 的 username／title／title_bg／dan／gold／rainbow。
- 数据：`Core/PlayerInfo.cs`（name、title、titleBackground、dan、gold、rainbow，默认 Don-chan／Donder Debut!／0／-1，即原 scores.db 种子玩家）。全局对象 `Scenes/PlayerInfoController.cs`（用户决定）：首场景前自动创建、跨场景保留，初始化时读 `Application.persistentDataPath/player.json`（不存在时写入默认值，便于编辑），`Changed` 事件让屏幕上的名牌即时更新；`Reload()` 重读文件，`Set()` 写入并通知，`UseUnsaved()` 供测试使用不落盘的数据。没有游戏内编辑界面。
- 规则（照搬 nameplate.lua）：「Donder Debut!」或空字串视为无称号；dan 只接受 0–24（初級…達人），超出视为无段位；无称号且无段位时为 coin 牌（只画白牌、1P 徽章与 30 号名字，不画称号带和段位）；否则画称号带（frame_top 第 titleBackground 帧，超出 0–4 归 0）与描边、段位底板与段位字（gold 用金色版）、1P 徽章、黑色 22 号称号（框 215 宽，中心 226.5,34.5）、名字（框 190 宽；有段位中心 261,67.5，否则 226,67.5，24 号）。文字只横向压扁到框宽，不缩字号；字距 2 px。单人始终有 1P 徽章，不移植 2P／AI 牌（未导入 2p.png／ai.png）。
- 彩虹称号带：原版 `tex.get_animation(12, "global")` 从未 `start()`，因此 OurTaikoPlayer 中彩虹带永远停在第 0 帧。用户决定按设计让它循环：6 帧、每帧 50 ms、300 ms 一循环，第 k>0 帧下面先画第 k-1 帧；用真实时间（原版 current_ms），暂停时也继续。
- 名字描边：原版白字黑边 3 px（OutlinedText 2×1.5）。现使用独立的宽留白 UI 字体恢复 3 px 外描边；早期 padding 9／outline 0.6 的近似已被替换，见「UI 字体外描边修复」。
- 实现：`Scenes/NameplateView.cs` 挂在预制体 `Generated/Nameplate.prefab`（408×96，子物体按原绘制顺序：Shadow、BandUnder、Band、Outline、DanBackground、Dan、Badge、Title、Name），切片 ``global/nameplate/frame_top.png` 导入设置中的 NameplateTitle0–4（2026-10-02 起不再是 Generated 资产）`、`NameplateRainbow0–5`、`NameplateDan00–24`、`NameplateDanGold00–24`。SinglePlayScene 在 `NoteLane` 下保存预制体实例（drum／连击／判定之后、BalloonCounter 之前，与 `Player::draw` 一致，压在鼓面左缘之上）；SongSelect 在 `Awake` 中实例化到 Wheel 与 CoursePanel 之间（原版在选曲轮之上、选项面板之下）；Result 在 `Build` 中实例化到 SoulSheen 之后、FadeIn 之前。迁移入口 `ProjectBuilder.ApplyNameplate()`（菜单 OurTaiko/Apply Nijiiro Nameplate），会重建预制体并更新三个场景，可重复执行。
- 自动演奏（用户决定，有意偏离）：原版自动演奏时在名牌位置画 `lane/auto_icon` 取代名牌；本项目名牌始终显示，自动演奏只在演奏オプション徽章区（`ModifierBadgeView`）第一位加入选曲的 `song_select/modifier/mod_auto`（40×40，modifier.cpp 中 auto 排第一），没有其他视觉差别。
- 分数计数器（用户要求一并移植，因原 TMP 占位分数与名牌重叠）：照搬 `score_counter.cpp`。`lane/lane_score_cover` 画在轨道局部 (0,12)；`lane/score_number` 十个 56×64 数字右对齐到 x 255、间距 30（Nijiiro 未覆盖 `score_counter_margin`，继承 Green 的 20×1.5），不补零，顶边 5.5（277.5-272）。分数变化时重启 TextStretch（id 4，与气球数字同一公式，抽成 `Core/TextStretch`）：50 ms 内升到 12 px，再按 16.57 ms 阶梯回落，最后两帧略为负值，数字底边固定、向上伸长。Nijiiro `delay_score_addition` 为 false，分数即时更新。实现 `Play/ScoreCounterView.cs`，作为 `NoteLane` 最后一个子物体（原版最后画分数）；切片 ``game/lane/score_number.png` 导入设置中的 ScoreNumber0–9`。单次加分动画已于 2026-10-04 补齐，见下。
- 移除：SinglePlayScene 左上调试文字「OURTAIKO / PLAYER 1」（PlayerName）与「READY n／AUTO PLAY／1 PLAYER」（PlayState），均为用户决定，自动演奏因此只体现在徽章区；以及原 TMP 分数标签 `Score`。
- 验证：EditMode `NameplateTests`（coin／称号／段位判定、越界回退、名字框、彩虹帧时序、JSON、分数布局、TextStretch），PlayMode `NameplateFlowTests`（三个场景的位置与层级、称号带／段位／金色、信息变更即时更新、长名字压扁到 190、彩虹循环、AUTO 徽章在首位、分数计数器初始 0／伸长／回落／位置、PlayerName／PlayState 已移除），截图 `TestResults/NameplatePlay.png`、`NameplatePlayCoin.png`、`NameplateSongSelect.png`、`NameplateResult.png`。EditMode 139/139、PlayMode 29/29 通过。

## 删除 Test_DefaultScene（2026-10-02）

- 用户要求删除测试入口 `Test_DefaultScene.unity` 及其全部代码：`LaunchMenu.cs`、`InputKey.NextSong`（Tab）／`InputKey.SongSelect`（S）、ProjectBuilder 的 `CreateMenu`、`AddSongSelectEntry` 与旧的 `MigrateGlobalSceneSwitcher`（把旧 `SceneSwitcher.unity` 改名为测试入口的一次性迁移）。本文更早记录中的 Test_DefaultScene／LaunchMenu 为历史。
- SongSelect 成为 Build Settings 首个场景，`SceneSwitcher.MenuScene` 改为 `SongSelect`（`ReturnScene` 默认值与 `ReturnToMenu` 的回退目标随之改变）；`CreateSongSelectAndResult` 会把 SongSelect 放到 Build Settings 第一位。
- 原版选曲中任意状态按 back 键都进入 Entry 场景（`song_select.cpp`）。Entry 尚未移植（用户决定稍后参照 OurTaikoPlayer 与 Nijiiro 皮肤移植），因此选曲 Esc 只在演奏オプション打开时关闭面板，否则无作用；选曲底部按键提示去掉「ESC もどる」。
- 测试：原先从测试入口取歌曲的 PlayMode 测试改从 `SongSelectScene.songs` 取；`MenuPlayPauseResumeRestartAndReturn` 改名为 `SongSelectPlayPauseResumeRestartAndReturn`，不再输出 `MenuScene.png`。EditMode 139/139、PlayMode 29/29 通过。

## Entry 场景（2026-10-02）

- 来源：`src/scenes/entry.cpp`、`src/objects/entry/box_manager.cpp`／`box.cpp`／`player.cpp`，Nijiiro `Scripts/entry/entry.lua`、`box.lua`、`player.lua`，`Scripts/global/timer.lua`、`indicator.lua`、`coin_overlay.lua`、`entry_overlay.lua`；Green `Graphics/entry/animation.json`（0、2、4、7、9、12 号）与 `global/animation.json`（9–11 号）；Nijiiro `skin_config` 的 `entry_credit_arcade`=true、`nameplate_entry_left`、`free_play`、`credit_side_*`、`timer_text_margin`。
- 流程（`Core/EntryFlow.cs`）：信用画面开始 120 ms 内不接受输入（0 号 lock_input）；任意咚面让 1P 加入（cloud 音效、entry_start_1p 语音、咚声），信用行中 1P 行用 `credit_row` 的 `#8@4` 闪白 24 帧，再按 `credit_fade` 16→35 帧淡出；名牌（`nameplate_entry_left` (14,910)）与操作指引在 200 ms 内淡入（12 号）；模式板在加入后 1233 ms（7 号云换帧结束：延迟 550+350，持续 333）出现，此前不接受决定；云动画结束且加入语音停止后播放一次 select_mode 语音。决定：咚声 + 9 号 160 ms 淡出 + `mode_board` `choose` 白闪，结束后 `SwitchScene(SongSelect)`。咔只在模式板可选时发出咔声（单板无处可移）。Esc 无作用（原版投币模式的信用画面没有取消；标题画面未移植）。SongSelect 的 Esc 回到 Entry（`song_select.cpp` 任意状态 back → ENTRY，演奏オプション打开时先关闭面板）。
- 画面（`Scenes/EntryViews.cs`）：背景只画 bg、4 个 twinkle（`entry_bg` sx／a，360 帧循环）、2 个 glow（`#16@3/#14@0` a）与前 19 帧的 street_lit，与 Nijiiro `draw_background` 一致（tower／shops／people／lights 不画）。信用行：`credit_pill` (380,404)/(380,580)，标签左对齐 x 517（黑字白边），消息居中 x 1150（白字黑边，黄边副本按 `text_message_instance_2` cr 混合），两行按 `credit_row` 120 帧循环一起闪烁（投币模式 sel=nil）。模式板：只做 演奏ゲーム（帧 0 开、1 关、8 白闪），`in` 时间轴决定板淡入、开合（`board_bg_center_instance` sy 映射为开合度，关→开交叉淡入）、内容与光标；光标脉动取 `cursor_glow` `#12@0`；标题 72 号白字、模式色 (251,1,29) 内边加黑色外边（TMP Underlay 近似），说明两行 34 号白字黑边，行心 568／621。
- 全局元素（`Scenes/GlobalOverlays.cs`、`Core/ArcadeTimer.cs`）：计时器 60 秒，在信用画面不更新（冻结后第一次更新立即减 1，与原版相同），选中后停止；<10 秒换红底白字、每秒 blip、数字 333 ms 二次缓出放大到 1.5 再缩回、高光 200 ms 放大淡出；30／10／5 秒语音（后者打断前者），归零停语音并自动决定。操作指引 (0,12) 352×276：原图 325 格中只用决定循环（210–324 格，30 fps）；信用画面从场景开始计时、alpha 1，加入后随玩家指示器重新计时并随名牌淡入。フリープレイ 40 号居中 (960,1046)；QR 芯片 NG (1570,38)；状态芯片 段位道場 NG (1040,38)、1プレイ4曲 (1200,45)、IC Card NG (1460,38)（离线帧 0）；2P 邀请云在信用行消失后出现（`credit_side` 3 秒周期，云 alpha 0.70 为原版设定，文字压扁到 416）。
- 素材：操作指引原图 4576×6900 超出常规 4096 上限，导入上限 8192、CompressedHQ（未压缩约 126 MB），只切 115 格到 `Generated/ControlGuide210–324`。`entry/global/player_entry_*` 是全透明 8×8 占位图，不导入。
- 未移植：3D 咚与加入云、2P 加入、其他模式板与きせかえ菜单、ALL.Net 图标；全局元素目前只在 Entry（原版选曲／结算也有部分）。TMP 描边受 Nijiiro SDF padding 9 限制，7 px 与双层粗边为近似。
- 迁移：SceneSwitcher 新增 `EntryScene`，`MenuScene` 改为 Entry；Build Settings 首个场景为 Entry。原从菜单场景取 `SongSelectScene.songs` 的已完成测试改为先载入 SongSelect（返回目标随之为 SongSelect）。
- 验证：EditMode `EntryTests`（流程时序、计时器语音／归零、弹动曲线），PlayMode `EntryFlowTests`（信用画面、加入、名牌／指引淡入、行淡出、邀请云、模式板开合、计时器开始、咔无效、决定白闪与淡出、切到 SongSelect），截图 `TestResults/EntryCredit.png`、`EntryModeSelect.png`。进行中 EditMode 11/11、PlayMode 3/3，已完成 PlayMode 27/27 通过。

## SongSelect 与 Result 的全局元素（2026-10-02）

- 来源：`song_select.cpp::draw_overlays`（计时器、ALL.Net、`coin_overlay`、指示器）、`result.cpp::draw_overlay`（fade_in 之后画 `coin_overlay` 与 ALL.Net），`coin_overlay.lua` 的分屏规则：フリープレイ 只在 ENTRY／RESULT 等（不在选曲），QR 芯片只在 ENTRY 与选曲类画面，2P 邀请云只在 ENTRY 与选曲（选曲另需 `songs_played < 2`）。Nijiiro 的 `song_select_indicator` 为 (-2000,1)，操作指引在选曲不显示；`entry_overlay` 状态芯片只在 Entry。
- SongSelect：`GlobalOverlays` 紧排在 `CoursePanel` 之后（原版在玩家层、演奏オプション 之后画），显示计时器、QR 芯片、2P 邀请云（`SceneSwitcher.SongsPlayed < 2`）。计时器按用户决定只作占位：列表显示 100、进入难度选择显示 60，永不倒数（`ArcadeTimer` 不更新，因此无语音、无红区、不自动决定）。`ArcadeTimerView` 按显示位数动态创建数字（100 为三位）。
- Result：フリープレイ 画在 `FadeIn` 之后、触控区之前；无 QR 芯片与邀请云。
- 迁移入口 `ProjectBuilder.ApplyGlobalOverlays()`（菜单 OurTaiko/Apply Global Overlays），与 Entry 共用 `OverlayArt()`。重新切片只会改写 Sprite 资源的 `m_RenderDataKey`，属无意义 diff，提交前还原。
- 字体事故：Entry 移植期间在 Editor 打开时 `git restore` 了动态字体 `Nijiiro SDF.asset`，导致已打开场景的 TMP 文字引用不存在的第 2 页图集并抛 `IndexOutOfRangeException`。已在 Editor 中重新读取字体并刷新所有场景文字；今后只在 Editor 关闭后还原该文件。
- 验证：PlayMode `GlobalOverlayFlowTests`（选曲层级、无フリープレイ、QR 与邀请云、100／60 占位不倒数且三位数字、结算只有フリープレイ且在 FadeIn 之上），截图 `TestResults/OverlaySongSelect.png`、`OverlayCourseSelect.png`、`OverlayResult.png`；进行中 PlayMode 5/5 通过。

## Entry 计时器改为占位（2026-10-02）

- 用户决定：模拟器不限制玩家时间，Entry 的 60 秒计时器与选曲一样只显示 60，不倒数、不 blip、无 30／10／5 秒语音、不自动选择模式板。上文「Entry 场景」中关于计时器倒数与语音的描述为原版行为记录。
- 移除 `EntryScene` 的计时器音效字段与 `TimerVoice` 音源（迁移入口 `CreateEntryScene` 会删除已有场景中的该物体），删除已导入的 `Audio/global/timer_blip.ogg`、`voice_timer_30/10/5.ogg` 及其来源记录。`ArcadeTimer.Update` 保留为原版倒数逻辑的记录，EditMode `EntryTests` 仍覆盖。
- 验证：PlayMode `EntryFlowTests`（加入并进入模式选择后计时器仍为 60）通过。

## 删除计时器倒数代码（2026-10-02）

- 用户要求删除不再使用的倒数代码：移除 `Core/ArcadeTimer.cs`（倒数、30／10／5 秒语音提示、归零决定、数字与高光弹动曲线）及其 EditMode 测试。倒数不再发生，10 秒内的红色表盘、白色数字与高光也一并删除：`ArcadeOverlayArt` 去掉 `timerBackgroundRed`、`timerHighlight`、`timerDigitsWhite`，删除生成的 `Generated/TimerDigitWhite0–9.asset`。`ArcadeTimerView.Show(int)` 只显示固定数字（Entry 60，选曲列表 100、难度选择 60）。原版行为仍记录在上文「Entry 场景」。
- `global/timer/bg_red.png`、`counter_white.png`、`highlight.png` 不再被引用（代码、场景、预制体与资源中均无其 GUID），已删除并从 `ImportedAssets.json` 移除；`Art/global/timer` 只剩 `bg.png` 与 `counter_black.png`。
- 验证：EditMode `EntryTests`，PlayMode `EntryFlowTests`、`GlobalOverlayFlowTests` 通过。

## UI 字体外描边修复（2026-10-02）

- 字体当时为 Nijiiro 的 `Taiko.ttf`（2026-10-02 已换为 `DDFont.ttf`，见「源字体替换」）。旧 `Nijiiro SDF` 使用 90 采样字号／9 padding，粗描边使透明字形边缘出现灰色矩形；选曲的居中描边还会侵蚀白色笔画。
- 新增 `Resources/Nijiiro UI SDF.asset`：64 采样字号／32 padding、SDFAA、1024 动态多图集、构建时清理动态数据。迁移入口 `ProjectBuilder.CreateOutlinedUiFont()`，不重建已有字体、不改动旧动态字体。旧字体的 Editor 自动变更仍应排除提交。
- `SkinUi.OutlineOutsidePixels` 按设计区域单位指定外描边，字面扩张与描边宽度相等，保留白色字形；Canvas 缩放自然作用于描边。换算为 `pixels × pointSize / (2 × padding × fontSize)`，由 TMP 更新材质比例；换字体时同时更新实例材质、描边颜色与 CanvasRenderer 的图集绑定，避免旧材质缓存导致颜色丢失或字形错乱。
- 恢复参考配置：列表标题／副标题为类别色 5／3.5 px；难度标题／副标题为黑色 7／4 px；名牌名字黑色 3 px；2P 邀请黑色 6 px；フリープレイ黑色 4.5 px。宽度均指 1920×1080 设计区域，随画面缩放。
- 旧 `OutlineOutside` 调用兼容原先实际描边厚度；Entry 模式标题的黑色 underlay 单独换算，避免更宽图集放大原来的双层描边。
- 难度文字同步恢复外描边：列表小芯片 18 号／1.5 px；难度卡片 34 号／4.5 px，字距均为 1 个设计单位。
- 选曲列表的描边颜色按用户要求修正：参考 `BOARD_OUTLINE[0]` 错把默认黄色板配成 pops 青色 `(18,72,76)`，Unity 原先照搬了该表。标题与副标题现使用半透明黑色 `(0,0,0,153)`，由 TMP shader 与文字下方的实际背景做 alpha 混合，白色字面仍不透明；删除类别色表，不采样背景或预计算混色。这样渐变与动画底色也自然透过描边；难度面板仍使用纯黑边。
- 后续补齐幕布与游玩名牌：SceneSwitcher 预制体旧 `titleOutline=0.25`／`subtitleOutline=0.4` 覆盖代码默认值，兼容换算后只有约 3.2 px；删除这两个旧字段，主副标题直接使用 5 px 外描边。Nameplate 预制体原先仍保存旧字体，运行时只替换名字；现名字与称号都使用新 UI 字体，名字保留 3 px 黑边，称号仍为无描边黑字。
- `ProjectBuilder.ApplyOutlinedUiFont()` 只更新 Nameplate／SceneSwitcher 预制体的字体与四个持久化材质，不重建场景或布局；未来生成这两个控件的入口也复用同一配置，避免再次写入旧字体引用。
- 验证：Unity Editor 编译通过；`GlobalOverlayFlowTests` 2/2、`NameplateFlowTests` 2/2、`EntryFlowTests` 1/1、`SongLoadingCurtainTests` 2/2 通过。核对选曲列表、难度卡、名牌与 Entry 截图；报告为 `TestResults/font-*.json`。独立播放器未重新构建。
- 补齐后验证：`SongLoadingCurtainTests` 2/2、`NameplateFlowTests` 2/2 再次通过，新增检查覆盖幕布两种字号的 5 px 外描边、游玩名牌两种字号的 3 px 黑边及新字体引用。核对 `SongLoading.png`、`NameplatePlay.png`；报告为 `TestResults/font-followup-curtain.json`、`font-followup-nameplate.json`。预制体保存的四处字体引用经 Editor API 确认为 `Nijiiro UI SDF`；未重新构建独立播放器。
- 半透明描边验证：`SongSelectShowsPlaceholderTimersChipAndInvite` 1/1 通过并核对 `OverlaySongSelect.png`；报告为 `TestResults/song-select-outline-alpha.json`。TMP shader 的 `_OutlineColor.a` 参与 `Blend One OneMinusSrcAlpha`，字面使用独立的不透明 `_FaceColor`。

### 游玩名牌首次渲染灰字修复

此前的字体／材质迁移未消除静态游玩名牌的初始化问题：`NameplateView.OnEnable → Show → SetText` 在 CanvasScaler 稳定前调用 `ForceMeshUpdate`，TMP 后续按 lossyScale 修正时又乘了一次 Canvas 缩放，导致 SDF 网格 `UV0.w` 过小。实时复现中字号 30、图集采样字号 64、Canvas 缩放 0.428125，本应为 0.46875 的值变成 0.200684；材质实际仍为不透明黑边，但 shader 因错误的 SDF 缩放画出浅灰字与字形矩形。

删除该处 `ForceMeshUpdate`，`Squeeze` 直接通过 `preferredWidth` 测量。`NameplateView` 在 `Canvas.willRenderCanvases` 中检测画布比例，仅首次渲染或比例变化时重新生成名字／活跃称号的网格；CanvasScaler 已在 `preWillRenderCanvases` 中完成缩放，避免启动及窗口调整时 TMP 的增量缩放出错。不得用每帧重建或调粗／调黑材质掩盖它。验证必须检查实时 Overlay 的 CanvasRenderer 网格：旧 `TestCapture` 会临时切换 Canvas 渲染模式并重建网格，恰好消除错误，因此之前截图不能证明原始 Game 画面正常。

`NameplateFlowTests` 3/3 通过，包含直接打开 SinglePlayScene 的首次实时网格、50%／125% 画布缩放、正常场景切换及名字／称号变化。报告为 `TestResults/nameplate-live-sdf.json`。最终重新直接启动游玩，在未手动刷新文字／切换 Canvas 模式的情况下确认 `UV0.w=0.46875`；`ScreenCapture.CaptureScreenshot` 截取的真实 Game 画面为 `TestResults/LiveNameplateFixed.png`。

### 游玩暂停菜单（2026-10-02）

按用户要求，游玩界面只保留 Pause 按钮，原 Restart／Back 移入暂停菜单；三项固定为 Resume、Restart、Back to Song Select。Nijiiro 未提供专用暂停菜单，复用 `Graphics/dan_select/confirm_box` 的 `bg.png`、`selection_box.png`、`selection_box_outline.png`、`selection_box_highlight.png`，原图复制不修改，通过 Sprite 九宫格组成蓝色青海波樱花板与金橙按钮。资源来源已写入 `ImportedAssets.json`；定向迁移入口 `ProjectBuilder.ApplyPauseMenu()`，仅更新原暂停面板和这三个按钮，场景在 Editor 中保存。

- 暂停面板移到主 Canvas 最后一个子物体，遮罩覆盖窗口与留边，面板仍在居中的 1920×1080 设计区域中，覆盖轨道、触控鼓与 FPS。全局 SceneSwitcher 的切换遮罩保持其跨场景层级。
- `PauseMenuView` 使用真实时间各 0.5 秒淡入／淡出。打开时立即冻结谱面时钟、停止歌曲与击打音效，禁用并注销本场景 DrumPad，清除鼓面挤压；淡出期间仍冻结且遮挡点击，结束后才恢复歌曲、判定和鼓面。连续确认只执行一次；Restart／Back 也先等菜单淡出再交给 SceneSwitcher。Back 明确前往 SongSelect，直接运行游玩场景时也一致。
- Space／Esc 打开和恢复；↑／↓、←／→ 或 D／K 选项，Enter／F／J 确认；按钮同时支持鼠标和触摸。InputManager 统一处理键盘，关闭 uGUI 自带按钮导航避免双重确认。菜单打开当帧与恢复当帧屏蔽残留输入；恢复淡出期间失焦时保持暂停并重新显示菜单，避免后台继续演奏。F1 仍可重开。
- 新的 `PauseMenuFlowTests` 验证淡变、音频／音符冻结、禁用鼓面、遮罩层级、键盘／鼠标／触摸，以及重开和返回；旧行为测试中恢复后的立即操作改为等待淡出完成。

验证：Unity Editor 编译通过；新增暂停菜单专项 5/5，所在进行中 PlayMode 程序集 10/10；Finished PlayMode 回归 28/28。报告 `TestResults/pause-final-playmode.json`、`TestResults/pause-regression-playmode.json`。实时 Overlay 画面 `TestResults/PauseMenuLive.png`；1920×1080 与 1440×1080 渲染检查通过，后者另验证面板未越界且宽高比保持 1100:780。截图 [PauseMenu.png](PauseMenu.png)。独立播放器未重新构建。


## 魔王／里魔王长按切换（2026-10-04）

难度牌的鼠标／触控短按采用两步确认：点击未选中的难度只移动光标并播放咔音，点击当前已选中的难度才决定游玩，里魔王同样适用。返回与演奏选项按钮保留原有点击行为。

难度选择页的魔王牌支持鼠标左键和触控长按：持续超过 1 秒，在同时具有魔王与里魔王的歌曲中切换表／里并聚焦该难度，复用原有翻牌动画和音效。每次按住只切换一次，松开不确认歌曲；之后短按仍正常确认。移出牌面、失去窗口焦点、隐藏牌面或打开演奏选项会取消正在等待的长按。只有表谱或只有里谱时不切换；原鼓键连续十次右咔切换保持不变。`PointerRelay` 的长按回调仅绑定魔王牌。

## SongSelect 界面持久化（2026-10-02）

按用户要求，将选曲界面从 `Awake` 创建全部对象改为绑定场景／Prefab 的持久化引用。`SongSelectView` 保存曲目板、难度卡、演奏选项、名牌与覆盖层；`SongBoardView` 保存每块曲目板的图文与 5 个难度条目；`OptionPanelView` 保存 7 行菜单和点击区域；`SongSelectOverlayView` 保存计时器 60／100 两套数字、QR 与邀请云。`PointerRelay` 拆成独立同名脚本，供场景与 Prefab 持久化，点击回调仍由运行时绑定。

- `Generated/SongBoard.prefab` 提供新增歌曲的模板，当前 3 首歌直接使用场景中保存的实例；`Generated/PlayOptions.prefab` 保存展开的默认设置菜单，便于单独编辑。课程面板与其余静态对象直接保存在 SongSelect。
- 原 `CreateBoard`／`BuildCoursePanel` 等构建逻辑移到 Editor 定向迁移 `ApplySongSelectLayout`。再次执行不会覆盖现有布局；材质保存为资产，避免重开 Editor 后临时描边材质丢失。
- 运行时负责替换内容、播放动画和处理输入。标题移动、板面扩张、皇冠、背景滚动、设置面板滑动及箭头偏移都以保存的位置／尺寸为基线。曲目轮整体间距与中心通过 `SongSelectView` 编辑；单独曲目板的位置微调仍保留。
- Inspector 提供列表、难度、选项预览，只切换编辑态显示，不播放音频、不创建全局控制器、不读写玩家选项。列表预览使用收起基线；展开动画在 Play 中运行。
- 这次迁移范围是 SongSelect 及其演奏设置面板；Entry／Result 沿用原构建方式。

验证（最终代码与资产）：EditMode `SongSelectSavedAssetTests` 2/2；名称含 SongSelect 的 PlayMode 测试 8/8（含 `SongSelectSavedLayoutTests`），报告 `TestResults/song-select-saved-asset-final-editmode.json`、`TestResults/song-select-flow-final-playmode.json`。连续执行两次 `ApplySongSelectLayout()`，场景与两个 Prefab 的文件不变；编辑态三种预览已离屏渲染检查（`TestResults/SongSelectEditPreview*.png`）。独立播放器未重新构建。

## 游玩连击数（2026-10-02）

照搬 `objects/game/combo.cpp` 与 Nijiiro `game/combo/texture.json`／`skin_config.json`，取代原 TMP 占位文字。素材 `counter`／`counter_100`／`counter_gold`（10 个 64×80 数字，导入设置中切片 `Combo0–9`／`ComboSilver0–9`／`ComboGold0–9`）、`combo_ja`／`combo_100_ja`、`gleam` 原样复制自 Nijiiro。

- 位置（轨道局部，y 向下）：`combo_ja` (320,136)；数字顶边 y 58，行宽 = 位数×52（`combo_margin`），整行以 x 401 为中心（counter x 395 + 64/2 − 52/2），与鼓面中心 x 400 对齐。
- 显示与档位（用户决定，与 Nijiiro `combo_min`=10 一致；OurTaikoPlayer 代码写死 3）：连击 < 10 隐藏；10–49 白色数字；50–99 银色（`counter_100`，`combo_color_tiers`）；≥100 金色数字＋`combo_100_ja`＋闪光。
- 弹动：连击变化时重新开始 TextStretch（id 5，与分数计数器相同的 50 ms），数字向上伸长，真实时间。
- 闪光：`ComboGlimmer.anim`（500 ms 循环，歌曲时钟），三行 `gleam`，第 j 行提前 (2/3)×500×j ms；每行前 250 ms 每 16.67 ms 上升 1 px（取整），86 ms 后线性淡出至 250 ms，其余时间隐藏。位置取 PyTaikoGreen（Nijiiro 的父皮肤）`combo_glimmer_1–3` ×1.5 加 `gleam` y −276；每行固定 3 个、间隔 52，与位数无关（同原代码）。
- 场景结构 `NoteLane/Combo`（`ComboView`）：`Caption`、`Digits`（数字行，运行时只换 sprite 与排版，位数多时追加）、`Glimmer/Row0–2/Rise/Gleam0–2`（剪辑只写 `Rise` 的 y 与 CanvasGroup 透明度，Row 位置可在场景中调整）。编辑态保存金色「123」预览，开始游玩时隐藏。迁移入口 `ProjectBuilder.ApplyCombo()`（菜单 OurTaiko/Apply Nijiiro Combo），已有 `ComboView` 时只刷新 sprite 与剪辑、保留布局。
- 每 100 连击提示（`combo_announce.cpp`＋Nijiiro `Scripts/game/combo_announce.lua`）：已移植，见下文「连击提示与语音」。

验证：EditMode `AnimationClipTests` 21/21（含 `ComboGlimmerRowsRiseAndFadeOnTheirOwnPhase`）；PlayMode `ComboFlowTests` 1/1、`ScoreGaugeFlowTests` 1/1、`NameplateFlowTests` 3/3；截图 `TestResults/Combo10.png`、`Combo50.png`、`Combo101.png`。2026-10-02 用户确认完成后，`ComboFlowTests` 移入 `OurTaiko.FinishedPlayModeTests`，闪光测试拆成 `ComboGlimmerClipTests` 移入 `OurTaiko.FinishedTests`（`AnimationClipTests` 余 20 个）。

### 连击提示与语音（2026-10-02）

- 触发：连击数变为 100 的正整数倍时（`Player::check_note`，只有普通音符增加连击），新提示取代旧的；重开时随场景重置。
- 画面：`announce_bg_1p` 卷轴 (362,−264)、`announce_digit_1p`（10 个 104×104 竖排，切片 `ComboAnnounce0–9`）在 y −196、`announce_text`（コンボ!）在 y −137，坐标相对轨道顶边。排版照搬 Nijiiro lua 的 `layout()`：≤3 位间距 64、原宽；4 位缩至 0.85、间距 54；更多位按 4/n 再缩；コンボ! 随之横向压缩并右移。Nijiiro 使用 lua 版绘制，不走引擎的百位／千位 `announce_number`／`announce_add` 合成，这两张图未导入。
- 层级：原版先画魂槽，再在 `draw_overlays` 中画提示，因此 `ComboAnnounce` 放在 Viewport 中 `GaugeHitEffect` 之后，RectTransform 与 `NoteLane` 相同（子物体使用轨道坐标）。场景结构 `ComboAnnounce`（`ComboAnnounceView`，CanvasGroup）/`Background`、`Number`/`Digit0–n`、`Text`；位置可在场景中调整，数字与コンボ! 的相对排版由代码按位数计算。编辑态保存「300」预览，开始游玩时隐藏。
- 时间：`ComboAnnounce.anim`（歌曲时钟，暂停冻结）CanvasGroup 透明度 100 ms 淡入（动画 65）、保持到 1666.67 ms、100 ms 淡出，1766.67 ms 后隐藏。
- 语音：Nijiiro `Sounds/game/combo/<n>_1p.ogg`（100–5000，每 100 一个，共 50 个）复制到 `Assets/OurTaiko/Audio/combo/`，提示出现时由 `hitAudio.PlayOneShot` 播放一次；超过 5000 没有语音（原版 `has_sound` 失败时不播放）。`50_1p.ogg` 原版从不播放（提示只在 100 的倍数出现），未导入；2P 语音未导入。
- 迁移入口 `ProjectBuilder.ApplyComboAnnounce()`（菜单 OurTaiko/Apply Nijiiro Combo Announce），已有 `ComboAnnounceView` 时只刷新 sprite、语音与剪辑。
- 验证：`ComboFlowTests` 1/1（9 隐藏、10 白、50 银、100 提示出现且透明度 0.5／1／0.5 并在 1.8 s 后隐藏、200 取代并显示 2/0/0、101 金色、漏音隐藏连击）；`ScoreGaugeFlowTests` 1/1、`NameplateFlowTests` 3/3、`PauseMenuFlowTests` 5/5；截图 `TestResults/ComboAnnounce100.png`。

## 判定计数器（2026-10-02）

按用户提供的街机截图（良／可／不可／連打数 四行计数）制作，取代原版 `judge_counter.cpp` 的百分比设计：Nijiiro 不带 judge_counter 皮肤，PyTaikoGreen 版位于轨道下方并显示百分比，与参考不同。

- 位置（用户选择，参照截图）：Viewport 左上 (29,50)，352×212，位于 `ComboAnnounce` 之后（原版 `draw_overlays` 中 judge_counter 也在 combo_announce 之后）。原 FPS 读数 (36,198) 会压住 連打数 行，迁移时移到 y 2。
- 面板：圆角 20、5 px 浅橙边 (255,162,50)、填充 (240,88,40,α225)；行条：白色 α128 圆角条 174×34，行距 45、首行 y 18。两张图由 `GenerateJudgeCounterArt()` 用有符号距离抗锯齿绘制并九宫格切片，每次迁移重写相同字节（GUID 不变）。颜色取自参考截图采样（面板约 (250,102,54)，行条约白色 50%）。
- 标签：从 `result/score/max_combo_ja.png` 切出 良／可／不可／連打数（字形外 2 px），缩放 0.68，水平居中于行条（用户要求；各切片字形左右各留 2 px，切片居中即字形居中）。该图改为 Multiple 后保留整图切片 `ResultJudgeLabels`，Result 场景的 `judgeLabels` 改指向它，`CreateSongSelectAndResult` 的构建代码同步更新。
- 数字：分数计数器的 `score_number`（`PlayScene.scoreCounter.digits`），高 38、间距为高度 × 30/64（与分数计数器同比例），右边缘 x 300；`JudgeCounterView.Show(good, ok, bad, rolls)` 在每次判定（含连打）后由 `UpdateHud` 调用，只在数值变化时重排。
- 结构：`JudgeCounter`（面板 Image＋`JudgeCounterView`）/`Good|Ok|Bad|Roll`/`Bar`、`Label`、`Count`（数字容器，编辑态预览 0）。迁移 `ProjectBuilder.ApplyJudgeCounter()`（菜单 OurTaiko/Apply Judge Counter），已有视图时只刷新数字 sprite；连续执行两次场景、两张生成图与 .meta 哈希不变。
- 原调试文字 `JudgmentCounters`（GOOD/OK/BAD/ROLL）与 `PlayScene.counters` 删除（用户决定）；`NoteMojiFlowTests` 仍通过。
- 调试文字 `RollCounter`（连打「DRUMROLL n」、9 号彩球「BALLOON n」）与 `PlayScene.rollCounter` 也删除（用户决定）；9 号彩球因此没有剩余次数显示，原版 kusudama 演出仍未移植。`SceneFlowTests` 13/13 通过（删去其中检查该文字为空的断言）。
- 验证：`JudgeCounterFlowTests` 1/1（10 良、1 可、1 不可、3 连打显示为 10/1/1/3，右对齐与高度）；`SongSelectResultTests` 2/2（结算标签仍为整图）、`ScoreGaugeFlowTests` 1/1；截图 `TestResults/JudgeCounter.png`。

## GlobalSettingScene 与 SettingManager（2026-10-02）

用户要求：新增全局设置场景与 SettingManager，从 Entry 进入，本地 json 存储；第一个类型「Play」，第一个设置「Enable Drumpad for Single Player Mode」，默认 true，控制 SinglePlayScene 触控鼓的启用与显示；只用咚咔或只用触控都能完成设置。

- **Entry 模式列表**：照搬 Nijiiro `Scripts/entry/box.lua`（街机 mode_select 列表）与原 `box_manager.cpp` 的板顺序：演奏ゲーム 在前、ゲーム設定 在后（中间的特訓モード／きせかえ 未移植）。ゲーム設定 板用 `mode_select/box` 9（开）／10（关）帧（box.lua `MODES.settings`，烘焙自 `aprilfool`），标题边色 (0,132,212)，说明「ゲームのせっていを／かえられるよ！」。选中板居中打开，其余按 `Animations/mode_list.txt`（`entry.nulm mode_select_list_instance`，原样复制）`wait` 标签的 kanban 槽位关闭排列（上一格 −50,−305，下一格 +50,+305），超过一格淡出。左咔上移、右咔下移并在两端夹住（`BoxManager::move_left/right`），咔音在两端也会响；移动时所有板 9 帧线性滑到新槽位，新选中板在滑动结束（150 ms）后才播 select_on，旧板立即 select_off（box.lua ROUND 49 的顺序）。首次出现用 `in`：选中板淡入打开，关闭的板从 3 格外（`kanban_3` 在 `in` 标签的位置）停 10 帧后 12 帧二次缓出到位。决定时选中板播 `choose` 白闪，列表淡出后切到该板的场景。
- **Entry 触控**（用户发现全屏点击只会触发咚，无法用触控选到ゲーム設定）：投币画面点任意处＝咚（加入）；模式列表中点非选中板＝移动到该板（咔音），点选中板＝决定，点空白不响应；纵向滑动每约 200 px 移动一格（上滑同右咔）。每块板加透明 `Hit` 点击区，大小按打开程度在 box.lua 记录的可见板面 964×157（关）与 1050×436（开）之间插值，避免关闭板 1160×460 贴图的透明边挡住打开的板；全屏 `TouchArea` 移到 `ModeBoards` 之下，`SwipeRelay` 同时挂在两者上（从板上开始的拖动沿层级冒泡到 `ModeBoards`）。原版 Entry 的触控只是全局触控鼓（上半屏咔、下半屏咚），这里按用户要求改为 SongSelect 式点板。`EntryTouchFlowTests` 用 `EventSystem.RaycastAll` 验证各点的实际命中对象。
- **存储**：`GameSettings`（`[Serializable]`，`play.singlePlayerDrumPad`）以 `JsonUtility.ToJson(…, true)` 写入 `Application.persistentDataPath/settings.json`，读取用 `FromJsonOverwrite`，缺失字段保留默认，以后加设置不破坏旧文件。`SettingManager` 与 `PlayerInfoController` 同构：`BeforeSceneLoad` 自动创建、`DontDestroyOnLoad`、文件不存在时写入默认、`Set` 先写临时文件再替换、`Changed` 事件、`UseUnsaved` 供测试。
- **菜单**：`SettingsMenu` 三级焦点。进入时焦点在左侧类型；咔（左＝上、右＝下）在当前列表循环移动，咚确认：类型 → 该类型的项目；项目 → 该项目的选项（初始为当前值）；选项 → 应用并保存，焦点回到项目。两个列表末尾都有 Return：类型的 Return 返回 Entry，项目的 Return 回到类型。Esc 逐级后退（选项不应用）。原版 settings 场景是横向盒子列表＋左右咔，这里按用户要求改为左右两列与三级焦点。
- **触控**：与 SongSelect 曲目板一致——点其他行移动到该行，点已聚焦的行确认；点项目行会把焦点从类型移到项目；点选项按钮直接应用。两列表另有纵向滑动（`SwipeRelay`，每拖动约 0.8 行距移动一行，上拖为下一行）：滑动会先把焦点移入被滑的列表。说明：SongSelect 实际只有点击，没有滑动。
- **画面**：PyTaikoGreen `Graphics/settings`（Nijiiro 皮肤没有自有设置美术，原版运行时继承 Green）1.5 倍：`background` 铺满、`box`／`box_highlight` 为类型行（567×138）、`title`／`title_highlight` 九宫格为项目行（1170×130，标签 40 号自动缩到 26 号以免压到右侧当前值）、`overlay` 九宫格为选项弹窗（名称、说明，内容内缩 90 以避开图片透明边；用户决定只在焦点进入选项时出现，居中 (375,340)，下方全屏 55% 黑色 `PopupShade` 遮住列表，点遮罩不改值关闭；浏览类型／项目时隐藏）、`button_off`／`button_on` 1.2 倍为选项按钮（亮的是焦点选项）、`blue_arrow` 指向焦点（行或按钮右侧）、`footer` 置底。标题「ゲーム設定」左上。选项标签沿用 settings_template 的 Enabled／Disabled，并显示原模板风格的说明文字。BGM 为 Green `Sounds/settings/bgm.ogg`，移动用咔音，确认／返回用咚音。
- **游玩**：`PlayScene.drumPad` 引用 `TouchDrum`；`Start` 中 `SetActive(settings.play.singlePlayerDrumPad)`，关闭时不绘制、不注册 InputManager；`DisableDrumPads` 只收集启用中的鼓，因此暂停／恢复不会打开它。
- **导入陷阱**：Unity 6 2D 项目对新复制的 PNG 默认 Multiple 自动切片（9.png 被切成 966×387 的修剪 sprite）；`ImportEntryArt` 现把 `mode_select/box/*.png` 强制为 Single，`ImportSettingArt` 同样处理并设九宫格边。
- **验证**：`SettingsMenuTests` 5/5、`GlobalSettingFlowTests` 3/3；全部程序集 35/35、136/136、15/15、30/30（`TestResults/settings-*.json`）；截图 `TestResults/EntrySettingsBoard.png`、`SettingsTypes.png`、`SettingsChoice.png`、`SinglePlayNoDrumPad.png`。`CreateGlobalSettingScene()`／`CreateEntryScene()` 连续执行，场景与 Build Settings 文件哈希不变。
- 未做：全局覆盖层（计时器、フリープレイ、操作指引）未放入设置场景；项目超过 5 行时会压到 footer，届时需加滚动（详情已改为弹窗，迁移 `ConfigureSettingPopup` 只在场景还没有遮罩时执行一次）；多语言未做（标签为英文，标题与 Entry 文字为日文）。

## 源字体替换（2026-10-02）

- 用户要求用 `DDFont.ttf`（字族 FOT-OedKtr Std）取代原 Nijiiro 的 `Taiko.ttf`（DFPKanTeiRyu-XB）。新字体位于 `Art/DDFont.ttf`，`Taiko.ttf` 已删除。
- `Generated/Nijiiro SDF.asset`（2026-10-03 已删除）与 `Resources/Nijiiro UI SDF.asset` 原地改指新字体（GUID 不变，场景、预制体与描边材质的引用不受影响）：更新源字体引用与 FaceInfo（采样字号不变：90／64），清空动态图集后按原字符表重新生成。迁移入口 `ProjectBuilder.ApplySourceFont()`（菜单 OurTaiko/Apply Source Font，重复执行不改动）；新建字体的生成代码也改用 `DDFont.ttf`。
- 新字体字形更宽。SongSelect 保存的 2P 邀请文字是生成场景时按字体压扁的，已在 Editor 中按新字体重新 `Squeeze` 到 416 并保存场景（`InviteMessage` 横向比例 0.960→0.874；用户要求直接改场景中的比例，不在运行时重算）。今后换字体须同样在 Editor 中更新这类保存的比例。
- 验证：PlayMode `NameplateFlowTests` 3/3，截图检查选曲、游玩与结算文字。

## 单一字体与黑色描边（2026-10-03）

- 用户要求：不同时保留两个 SDF，只留更通用的一个；浅色文字用不透明纯黑描边，本身为黑色的文字不加描边；粗细只取决于字号与缩放，不写死像素。期间试过 60% 透明黑与按背景亮度（L = 0.2126R+0.7152G+0.0722B）调整 alpha／宽度的方案，均由用户撤回，不要恢复。
- 保留 `Resources/Nijiiro UI SDF.asset`（64 采样字号／32 padding，能容纳粗描边）；删除 `Generated/Nijiiro SDF.asset`（90／9 padding）及其 `Nijiiro SDF Outline.mat`、`Generated/Nijiiro UI *.mat`（10 个按用途、按 px 计算的材质）与 `Generated/SongSelectMaterials`（9 个去重样式）。
- 浅色文字共用 `Resources/Nijiiro UI SDF Outline.mat`：`OUTLINE_ON`，`_OutlineColor` 纯黑，`_OutlineWidth` = `_FaceDilate` = `SkinUi.OutlineWidth`（0.125）。TMP 的归一化宽度是 em 的比例，所以描边随字号与 RectTransform 缩放（包括 `Squeeze` 的横向压缩）变化；在本字体上外描边约为字号的 1/8（24 号 3 px、64 号 8 px），等值 dilate 使描边在字面之外，白字不被侵蚀。黑色／近黑色文字（`SkinUi.IsDark`：颜色亮度 < 0.3）用字体自带材质（描边宽度与 dilate 为 0）：名牌称号 000000、暂停按钮 2D1E0E、设置详情标题 281E14 与说明 463C32、Entry「１人プレイ／２人プレイ」000000，即原本就没有描边的那些文字。
- 运行时：`SkinUi.Text(name, parent, size)` 与 `TMP_Text.UseUiFont()` 只绑定共享字体，并按文字颜色选两种共享材质之一，不生成材质实例（改变文字深浅后需再调用 `UseUiFont`）；`OutlineOutside`／`OutlineOutsidePixels` 删除。原先各处的 px／颜色（Entry 模式板色边与黑色 underlay 双层边、Entry 黄色高亮边、结算标题棕边、选曲 60% 黑边等）一律改为统一黑色描边；FPS 等原本无描边的白色文字也加描边。场景控制器的 `font`／`outlineMaterial` 字段删除。
- 迁移：`ProjectBuilder.ApplyUnifiedUiFont()`（菜单 OurTaiko/Apply Unified UI Font）先处理全部预制体再处理 `Assets/Scenes` 下全部场景，把字体换成上述字体、按文字颜色选材质并清空 TMP 材质缓存；重复执行不改动文件。保存的 59 段文字中 53 段描边、6 段无描边（名牌预制体称号、3 个暂停按钮、2 段设置详情；场景中的名牌实例继承预制体）。
- 字体资产：Editor 与测试运行时 TMP 会把新字形写进动态字体资产。提交前执行 `ProjectBuilder.RegenerateUiFontAtlas()`（菜单 OurTaiko/Regenerate UI Font Atlas），清空图集，不留种子字符：额外图集页每次创建都会得到新的随机 fileID，种子字符超过一页（本字体 1024² 一页约 64 字）就会使结果每次不同。清空后提交的资产只取决于 `DDFont.ttf` 且每次相同；Editor 重绘打开的场景会再次加字形，所以执行后立即提交。

## 在线服务器与 ServerLogin（2026-10-03）

移植 OurTaikoPlayer `src/libs/fanmade.cpp`（Fanmade 游戏 API）与 MajdataPlay 的 Login 场景。库沿用 MajdataPlay：`System.Net.Http.HttpClient`（托管 `HttpClientHandler`，不引入原生 curl）＋ Newtonsoft JSON（`com.unity.nuget.newtonsoft-json` 3.2.2）。

- **流程**：Entry「演奏ゲーム」→ `ServerLogin`（Build Settings 在 GlobalSettingScene 之后）→ SongSelect。每次进入都重新连接全部启用的服务器（同原版每次从模式选择进入选曲时刷新）。每台服务器依次显示：ログイン（登录并拉取账号成绩）、ゲスト（只拉曲库，不上传成绩）、スキップ（不连接）、もどる（回 Entry）。没有启用的服务器时直接进入 SongSelect。成功登录过的账号下次自动登录（`autoLogin`），密码错误或选择ゲスト后关闭自动登录。键位：咔／方向键移动焦点，咚／Enter 执行；在输入框中 Enter 进入下一栏或登录、Esc 退出输入框；请求中 Esc／もどる 取消请求。
- **配置**：`persistentDataPath/servers.json`（`ServerList`：`name`、`baseUrl`、`username`、`password`、`httpProxy`、`enabled`、`autoLogin`）。内置两台服务器 OurTaiko Fanmade `https://fanmade.ourtaiko.org` 与 ESE `https://ese-backend.llx.life`（用户指定）：文件缺失时写入两者，已有文件缺少某个内置地址时补上；不要的服务器设 `"enabled": false`（删掉会被补回）。空 `httpProxy` 表示直连（也不读环境代理）。Token 只在内存中。
- **曲库与文件夹（用户决定：分类文件夹）**：连接时拉 bootstrap 后依次请求所有分类（OurTaikoPlayer 要等打开服务器文件夹才请求）。SongSelect 在本地歌曲之后为每台服务器的每个分类放一个文件夹板，默认全部关闭；层级只有一层（不做服务器文件夹）。照搬 `Navigator::load_current_directory` 无子文件夹的就地展开：文件夹板换成「もどる」（`bar_genre_back`），歌曲按 API 顺序接在后面，每 10 首再插一个もどる（`songs_added % 10 == 0`）；聚焦停在もどる；打开另一个文件夹先收起当前（`collapse_inline_now`），所以同时只有一个；もどる 或 Esc 收起并聚焦回文件夹板。列表末尾另有一个根「もどる」（用户要求，原版 `setup_back_box` 在根目录不加）：曲目轮循环，它位于第一首歌上方，初始聚焦仍是第一首歌；选它与 Esc 一样回到 Entry。从文件夹内歌曲游玩回来时重新打开该文件夹并聚焦该曲（`reopen_folder_path`）；经过 ServerLogin 后全部关闭。文件夹板（Nijiiro `draw_folder_board`）：关闭为 `bar_genre`，聚焦后按 `anim/folder_board`（`Animations/folder_board.txt`，select_on 5／select_off 30，关闭 8 帧）放大为 `folder_graphic`，`box_chara` 左右角色从 340 滑到 440 并淡入，标题上移 94，下方显示「N songs　服务器名」。板颜色按分类 genre（`OnlineManager.GenreFrame`），文件夹内歌曲用所在文件夹的颜色。未移植：Nijiiro 的文件夹进入／退出整轮飞出动画（wheel_decide）、genre 背景条展开、事件／排序文件夹。**曲目轮按需绑定视图**：场景保存的前几块板仍固定属于对应的本地歌曲（保留 Inspector 微调），其余歌曲／文件夹在进入屏幕时从 `SongBoard.prefab`／`Generated/FolderBoard.prefab` 池中取视图、离开时归还；1500 首的文件夹展开时约 120 FPS、视图 < 30 个。迁移 `ProjectBuilder.ApplySongSelectFolders()`（导入 `folder_graphic`／`bar_genre_back` 3-slice、`box_chara` 切左右半，生成 FolderBoard 预制体并绑定到 SongSelect；重复执行文件哈希不变）。
- **下载**：确定难度后在 SongLoadingScene 的幕布下重新取详情（作者新版本此时生效）、按 SHA-256 校验／下载 TJA 与音频、生成 `play.tja`（API 的块与标题、`WAVE:audio.ogg|mp3`、UTF-8；Shift-JIS 用 `Encoding.GetEncoding(932)`）。进度与错误显示在幕布新增的 `Status` 文字上（迁移 `ProjectBuilder.ApplyCurtainStatus()`）；Esc 取消；失败显示错误码 3 秒后回到选曲。原生后端直接读取缓存文件交给 BASS 解码；显式 Unity 后端才使用 `UnityWebRequestMultimedia`。缓存 `persistentDataPath/cache/fanmade/objects/<端点>/<谱面>/<版本>/`。
- **成绩（2026-10-04 更新）**：正常结束并进入 ResultScene 后，由结算场景发起保存／上传；非自动演奏、该难度 `cloudScoreEligible` 且已登录对应服务器时，写入 `scores.sqlite3` 的独立 `PendingScoreUploads` 表后台发送（每 30 秒重试，重启后继续，同一请求体与 `Idempotency-Key`）；按服务器＋账号隔离。上传成功删除，4xx（401／408／429 除外，含 409 换版）标为 Rejected 保留但不再重试。服务器声明 `scoreReplayVersion: 1` 时附带 `replay_data`（游戏时间毫秒及音画偏移）。在线历史最佳只使用登录 bootstrap 和成功上传响应返回的服务器成绩，不写本地 BestScores，也不把未上传成绩当作历史最佳。
- **代码**：`Runtime/Online/`（`ServerConfig`、`FanmadeModels`、`FanmadeEndpoint`、`FanmadeClient`、`PlayableTja`、`OnlineManager`），`Runtime/Scenes/ServerLoginScene.cs`／`ServerLoginView.cs`，Editor `ProjectBuilder.ServerLogin.cs`（菜单 OurTaiko/Create Server Login Scene：仅缺失时生成场景，并把 Entry 演奏ゲーム板的 scene 改为 ServerLogin；重复执行文件哈希不变）。界面用 PyTaikoGreen 设置美术（同 GlobalSettingScene）。
- **测试**：EditMode `FanmadeClientTests`（本地 `HttpListener` 夹具 `Tests/Shared/FanmadeFixture.cs`）、PlayMode `ServerLoginFlowTests`（登录错误／成功、文件夹开合与回来重开、下载、游玩、成绩与回放上传；ゲスト／スキップ、一次只开一个文件夹、下载失败返回；1500 首文件夹的もどる间隔与池化；无服务器直通）。`TestData.Use` 默认无服务器并使用临时缓存；`EntryFlowTests` 的上一场景断言改为 ServerLogin。
- **未做**：服务器层文件夹、谱师署名轮播、`Loading.png`、真机与独立 Player 验证。

## 跨平台原生音频（2026-10-03，按 MajdataPlay 重做）

行为参考为 TeamMajdata/MajdataPlay `dc19722d602f099131d93b37091624ad30150ad1` 的 `AudioManager`、`BassHelper`、`BassSimpleAudioSample`、`BassAudioSample` 和 `GamePlayManager.AudioTimeUpdate`。参考项目只读。旧的 AudioClip→GetData→浮点 WAV 桥接及八路音效池已删除。

### 依赖

- `Assets/Plugins/ManagedBass` 是 `https://github.com/TeamMajdata/ManagedBass.git` 的 Git 子模块，锁定 `5944aad3842484d78f588d63d5bdc8a85569495b`，不修改源码或 asmdef。克隆后执行 `git submodule update --init --recursive`；更新主仓库后也执行此命令，不用 `--remote`，不自动跟随上游最新版本。
- `Assets/csc.rsp` 与参考工程一样启用 `-unsafe` 和 `-langVersion:preview`；`System.Runtime.CompilerServices.Unsafe` 6.1.2 的原版 DLL、包来源和 MIT 许可在 `Assets/Plugins/System.Runtime.CompilerServices.Unsafe`。无需把上游 `Unsafe.As` 改成装箱转换。
- BASS、BASSmix、BASS FX、Opus，以及 Windows/Linux/Android 的 AAC，来自参考项目的原生库；Windows 另含 WASAPI/ASIO。文件散列在 `Un4seen.Bass/provenance.json`，各库许可分别保留。macOS/iOS 的 AAC 由 BASS 调用系统解码器。
- iOS 通过 Player Settings 的 `__STATIC_LINKING__` 配置 BassMix，使用上游原有条件编译；Bass/Opus/FX 已有 iOS 条件。

### 解码与播放

`NativeAudioSample` 保留原文件编码字节并固定其内存，按 `BassHelper` 顺序尝试 BASS→Opus→AAC（AAC 分支仅 Windows/Linux/Android）。使用 `Prescan|AsyncFile`，不经过 Unity 解码、不复制整首 PCM。

- BassSimple：独立原生播放流，关闭流缓冲；不创建全局 BASSmix。
- WASAPI/ASIO：无声设备解码，每个采样先经立体声重采样器，再按输出矩阵进入全局浮点混音器；保留参考工程的暂停标志、设备声道数、ASIO channel join 和 WASAPI exclusive/raw/shared 降级顺序。
- 同一音效重复触发时把原有采样归零重播，不新建声音实例或八路池。不同音效各持一个采样。
- 歌曲采用参考实现的峰值扫描归一化，场景音效/语音/BGM 不归一化；静音输入保留增益 1。正式歌曲启用 FX tempo，预览关闭（同 SongDetail）；不新增游戏演奏选项。
- 原生开始由单调时钟到点触发；BassSimple 在开始/恢复后两秒按参考工程的 0.8 系数修正音频与谱面时钟。暂停/离场取消待开始的播放并停止音效。实际输出延迟必须在设备上测量。
- 保留显式 Unity 后端和原生初始化失败时的 Unity 后备；只有该后端使用 Unity 解码和混音。

### 资源与场景衔接

场景保留 AudioSource/AudioClip 供 Inspector 编辑，播放调用统一经过 `AudioPlayback`。Editor 读取 AudioClip 对应原文件；构建前 `AudioAssetBuild` 将原文件无损包装为 Resources 字节资产并生成引用映射，构建后删除临时目录。这个包装适配 Unity 场景资源及 Android APK，只负责取原文件字节，不负责音频解码。导入资源使用 CompressedInMemory、关闭 preload，不再强制 DecompressOnLoad。

在线歌曲下载完成后保存实际音源路径；SongLoadingScene 在线程池建立原生采样、扫描峰值，将它交给 PlayScene。不创建下载音源的 AudioClip。读盘/解码失败显示错误并回选曲；取消加载会释放未接收的采样。选曲预览在线程池扫描峰值，切歌/离场后丢弃并释放过期结果；直接运行和重开也使用同一原始数据路径。

`AudioBus` 拥有当前歌曲/预览及预载音效，销毁时释放流和固定的编码缓冲；`NativeAudioSample` 对异常中途创建失败同样清理。参考实现中的静音增益无穷大及部分错误路径未释放资源没有照搬。

### 验证

- 全部 EditMode 189/189、全部 PlayMode 59/59（420 秒），覆盖原始 OGG/MP3/M4A/Opus 解码、归一化、FX、混音重采样、暂停恢复、预览、在线下载与完整演奏/结算。报告 `TestResults/audio-redo-editmode.json`、`audio-redo-playmode-final.json`。
- Windows、Android、iOS、Linux、WebGL 条件编译通过；原生库来源的 60 个文件 SHA-256 与清单一致，Android ARM64 五个音频库均为 16 KB ELF LOAD 对齐。
- 独立构建与链接结果见 `Documentation/Building.md`。平台编译/构建不等于 Windows 声卡或 Android/iOS 真机输出、延迟验收。

## Sound 设置（2026-10-03）

Entry「ゲーム設定」→ Sound，沿用咔移动／咚确认、触控选择及遮罩取消。用户最新决定：**界面显示六个音量组（Master／BGM／Track／Drum／Effects／Voice）、Output Backend 和 Return**。音量以 0–200%、5% 一档显示，确认后立即保存并生效；鼓音／语音确认时试听。详细设备参数留在 `settings.json` 的 `audio` 中，不在设置场景显示。所有原生平台提供 Automatic／BASS／Unity，Windows 增加 WASAPI／ASIO；WebGL 只提供 Unity。修改后端不会覆盖其他配置。

配置文件保留 `audio.volume` 的 master、bgm、track（歌曲及预览）、drum、effects、voice；值为倍率（1=100%，支持 0–2），总音量与组音量相乘，旧配置缺失字段取 1。Unity 最终单源音量仍受 0–1 上限约束。设备字段保留 BASS devicePeriodMs/deviceBufferMs/updatePeriodMs/playbackBufferMs、Windows WASAPI／ASIO 参数及 Android androidAAudio。手工修改配置文件后重新启动读取；不监听文件变化。

确认后端时即时写盘，退出设置时再次保存，并通过 `SceneSwitcher.SwitchSceneAfterFadeAsync` 在画面淡黑后应用，随后进入 Entry；无须重启游戏。页面显示实际后端及 Applies on exit，改回已应用的值会清除提示。没有设备变化时不重建输出。

切换先等待后台原生解码完成（逐帧等待生命周期锁），停止并释放所有 AudioBus 和未领取的 NativeAudioSample，再关闭设备与混音器并重建。异步加载捕获 generation，旧任务迟到时不能向新设备创建流；SongDefinition 不会领取已失效样本。显式后端初始化失败时恢复上次配置（保留新音量）、重新保存并留在设置界面显示原因、恢复 BGM；恢复设备也不可用时沿用启动流程的 Unity 兜底。WASAPI 仍保留 exclusive/raw 到 shared 的兼容尝试，Automatic 仍可选用 BASS。

Sound 共 8 行（含 Return），每页显示 4 行，支持分页、滑动、滚轮；选项弹窗最多显示相邻 3 项，左右按钮供 Windows 的 5 种后端选择，点选项或咚确认后才保存。新增控件保存在 `GlobalSettingScene.unity`，迁移 `ProjectBuilder.ApplySoundSettings()`（菜单 OurTaiko/Apply Sound Settings）；重复执行场景内容不变。音量试听使用 Drum／Voice 分组。

此前完整设备参数界面的测试报告 `TestResults/sound-settings-*.json`、`SoundDeviceSettings.png` 及后端单项界面的 `backend-only-settings-*.json` 属于历史验证；当前截图为 `SoundSettings.png`、`SoundVolumeChoice.png`、`SoundBackendChoice.png`。

音频退出切换验证（2026-10-03）：PlayMode Settings 7/7（含 BASS→Unity→BASS、无设备变化不重建、淡黑后才切换、失败恢复／留在设置／恢复 BGM、未领取样本失效、后台准备锁等待）、Native 9/9（与 Settings 重叠一项音量测试）；Windows／Android／iOS／WebGL／Linux 条件编译全部通过。报告 `TestResults/sound-reload-settings-playmode.json`、`sound-reload-native-playmode.json`、`sound-reload-platform-compilation.txt`。实际后端往返在 macOS Editor 验证；Windows／移动端设备热切换未实机验证。

## HitFace／HitRing 原生时钟回归修复（2026-10-03）

2026-10-04 判定文字修复：`PlayScene.OnJudged` 原先在 `Judgment.Roll` 时也重置 `feedbackTime`，但仍保留上次普通判定的贴图，导致击打气球或连打不断重播旧的良／可／不可。现在只有普通判定更新文字与淡出起点，5／6／7／9 号长音符击打不干预已有文字的 250 ms 淡出。真实帧回归在修复前复现（125 ms 时应约半透明，实际被重置为 1）；修复后 `HitFeedbackClockTests` 3/3，通过普通／练习场景的四种长音符、气球打爆、后续普通音符及原有 HitFace／HitRing。报告 `TestResults/long-hit-judgment-before-fix.json`（复现失败）、`TestResults/long-hit-judgment-fixed-playmode.json`（通过）。

切换原生音频后，`AudioEngine.Clock` 使用持续递增的 Stopwatch。`PlayScene.Update` 在帧开始读取时间，随后 `OnJudged` 又读取较晚的时间作为动画起点，最后用帧开始的时间调用 ShowTime；HitFace／HitRing 因 elapsed<0 立即取消，表现为笑脸与外圈消失。对象、素材和层级没有丢失。原测试暂停后手动调用 ShowTime，因此未覆盖真实帧路径。

最初修复为 Update 内临时共享歌曲时间。随后统一时钟时改为 `SongClock.Time` 每帧更新、持续保留该帧结果，替代临时快照；判定与动画仍共享歌曲时间。`HitFeedbackClockTests.HitFaceSurvivesJudgmentFrameWithNativeClock` 使用真实自动演奏及帧末观察，修复前复现失败（`TestResults/hit-face-native-before-fix.json`）。动画时长、显隐规则、每帧输入互斥均保持原规格。

本次最终验证：Settings EditMode 15/15、Settings PlayMode 7/7、HitFace PlayMode 2/2。报告 `TestResults/backend-only-settings-editmode.json`、`backend-only-settings-playmode.json`、`hit-face-native-fixed-playmode.json`；截图 `SoundSettings.png`、`SoundBackendChoice.png`、`HitFaceNativeClock.png` 已检查。Windows／Android／iOS／WebGL／Linux 条件编译通过（`backend-menu-hit-face-platform-compilation.txt`）；未重新构建独立 Player。

恢复音量组后的验证：SoundSettings EditMode 7/7、Settings PlayMode 7/7，覆盖六组音量、后端平台过滤、即时音量、语音试听、分页／滚轮、隐藏设备配置保留和退出切换。报告 `TestResults/volume-groups-restored-editmode.json`、`volume-groups-restored-playmode.json`；`SoundSettings.png` 已检查。


## 统一时钟与按帧判定（2026-10-03）

- `GameTimeline` 集中读取时钟，`GameLoop`（执行顺序 -32000）先采样再发布输入。FrameTime 用 Stopwatch 的单调时间，AudioFrameTime 按后端选择同一单调时间或 Unity DSP 时间；同一帧重复读取保持不变。异步场景切换在 Update 前恢复时也能按 frameCount 得到新帧快照。AudioNow 仅供音频调度，Realtime 供 FPS 等真实耗时测量。
- `SongClock` 管理倒计时、暂停冻结、恢复后的播放时间与提前 50 ms 调度，以及 Bass 后端启动／恢复后的 2 秒位置校正（0.8 权重）。PlayScene 负责每帧调用及玩法，删除原有 startDsp、frozenTime、audioSyncUntil、frameSongTime 与重复就绪状态。UI／幕布／鼓面闪光等统一使用 FrameTime，歌曲暂停时这些界面时间仍继续。
- **用户明确要求保留按帧判定**：输入时间戳只用于选出同帧最早的一击，其余打击丢弃；判定使用本帧 SongTime 减 audioOffset，不使用单次按键事件的时刻。120 FPS 下的帧时间量化是有意选择。DrumInputMutexTests 同时检查一帧只记录一击、记录的判定时间等于本帧歌曲时间。
- 删除 AudioEngine.Clock 和独立 InputManagerUpdater，由 GameTimeline／GameLoop 接管；未添加未使用的时间源接口或每个动画一份计时器。
- 验证：全部 EditMode 200/200、PlayMode 67/67，通过原生／Unity 音频调度、按帧输入互斥、HitFace 真正演奏帧、暂停恢复与场景流程；报告 `TestResults/unified-clock-editmode.json`、`unified-clock-playmode.json`。未重新构建独立 Player。


## General 语言设置（2026-10-03）

`settings.json` 新增 `general.language`，默认 `en`；旧配置缺少该分区时使用默认。General › Language 提供 English、日本語、简体中文、繁體中文、Korean（现有 DDFont 没有韩文字形，选项用英文避免方框）。仅选择歌曲主标题／副标题的语言，菜单文字、类别、皮肤与玩法均不受影响。

`SongInfo.Read(text, language)` 读取 TJA 的 TITLE／SUBTITLE 及语言后缀，支持 JA／JP、ZH／CN／ZH_CN、TW／ZH_TW、KO、EN。在线目录与下载后的谱面沿用现有 API 元数据（en／ja／zh／ko）；API 不提供繁体中文，不扩展接口字段，选择繁体时回退到日文。每个字段独立按 **所选语言 → 日文** 回退；都缺失时保留基础 TITLE／SUBTITLE。空译名视为缺失。`ReadInfo()` 仍读取基础元数据，显示入口单独使用 `ReadDisplayInfo()`，覆盖选曲板、难度页、加载幕布、游玩和结算；不修改谱面判定数据或成绩标识。

五个类型行（含 Return）使用 142 行距，保留原有行尺寸，在 footer 之前放下；语言选项使用现有三项可视区域及左右切换。

验证：EditMode 208/208，语言完整流程 PlayMode 1/1，现有设置相关 PlayMode 9/9；报告 `TestResults/language-editmode.json`、`language-flow-playmode.json`、`language-settings-playmode.json`，截图 `SettingsGeneral.png`／`SettingsLanguage.png` 已检查。未重新构建独立 Player。

## SongSelect 最佳成绩窗口与 SQLite（2026-10-03）

- `SongSelectView.bestScore`／`SongBestScoreView` 是场景保存的左侧小窗口，选曲列表中显示当前选中歌曲的成绩，进入难度选择继续显示；文件夹、返回项、无记录时隐藏。优先显示おに／裏おに：两者都有记录时每秒轮流显示，只有一个时固定显示；两者都没有时，仅显示むずかしい→ふつう→かんたん中有成绩的最高难度（不按分数大小选）。普通难度不会轮播。
- 按用户要求改为直接复用 SinglePlay 场景中保存的 `JudgeCounter`（组件和四行层级）、橙色面板／浅色行条和计分数字；上方增加难度图标、自己ベスト和最高分。良／可／不可／连打数属于该次最高分的记录。难度图标切自已有 `game/lane/lane_difficulty.png` 不透明图集，不另外导入 PNG；`courseMarks` 对应的是最高 alpha 153/255 的背景水印，不能用于成绩窗；删除不再使用的白底／金条／旧数字与专用小图标副本。资产来源见 `ImportedAssets.json`。迁移菜单 `OurTaiko/Apply Song Best Score`，重复执行保留已有布局；位置、大小、数字和轮播间隔可在场景编辑。
- 用户指定 UPM `com.gilzoide.sqlite-net` 1.3.2（Git URL，含该包自身 SQLite 原生库）。`persistentDataPath/scores.sqlite3` 含两张独立表：`BestScores` 仅本地谱面最佳分／判定统计／最佳皇冠；`PendingScoreUploads` 仅已登录账号的待上传请求（含永久拒绝状态）。选择界面读内存缓存，不逐帧查 SQLite。
- `ScoreStore` 首次迁移旧 `scores.json` 的本地条目，忽略旧 `fanmade/` 条目，保留旧 JSON 作备份。旧 `cache/fanmade/pending/<端点>` 的 JSON／rejected 请求保留原幂等键和请求体导入队列表，落盘成功后删除原队列文件，防止再次导入。
- `SongScores` 统一路由本地与在线成绩，选曲皇冠同样使用正确的数据源。在线成绩上传、登录拉取及上传响应均使用 API 的 `ClearStatus`（注意大小写）：0 无皇冠、1 银冠（通关）、2 金冠（全连）、3 彩冠（全良）。上传取本局 `StoredCrown`；歌曲板与难度卡直接使用服务器最高分记录的字段，不再根据判定数推断。缺失或未知值显示无皇冠。已有待上传请求保持原请求体与幂等键，不补写未知通关状态。自动演奏不保存、不入队。
- `ClearStatus` 验证：本地模拟接口 EditMode 19/19（含四种值上传／拉取／响应、缺字段旧记录及重试）；原在线流程 PlayMode 4/4，新增歌曲板／难度卡四种皇冠切换测试 1/1。报告 `TestResults/clear-status-editmode.json`、`clear-status-flow-first.json`（含新增测试检查时机修正前的失败）、`clear-status-crowns-playmode.json`（修正后通过）。未向真实账号提交测试成绩。
- 验证：EditMode 211/211；最终 SQLite 定向回归 3/3；最佳成绩窗口 PlayMode 1/1、在线登录／游玩／上传 4/4、SongSelect 回归 10/10。场景迁移连续执行两次，文件均不变；截图 `TestResults/SongSelectBestScore.png`。尚未重新构建或在手机上验证。
- JudgeCounter 改版后追加验证：`SongBestScoreTests` 1/1（列表显示、切歌隐藏／恢复、四行数据、进入难度、优先级轮播）；在线登录到上传 1/1。两次迁移文件不变，所有 Graphic 不接收射线。截图 `TestResults/SongSelectBestScoreList.png`／`SongSelectBestScore.png`。
- Editor 默认显示 BestScore 示例成绩（1002540，良 853／可 14／不可 2／连打 35），三种选曲预览按钮也会填入示例；不读玩家成绩文件。运行时 Awake 隐藏示例，随后根据真实记录显示，防止示例闪现。

## 游玩圆形暂停按钮（2026-10-03）

- 游玩页 PAUSE 文字按钮改为 Google Material Symbols Outlined `pause_circle`（opsz 48／wght 600／fill 0／grad 0），白色图标＋黑色细边。官方 SVG 栅格化成透明 192×192 PNG，运行时只引用本地 Sprite，不加载字体或联网。源 SVG、Apache-2.0 许可证和转换命令在 `Documentation/ThirdParty/MaterialSymbols/`，版权说明已加入 NOTICE。
- 原按钮对象与 onClick 暂停行为保留，删除文字子对象。位置保存在 SinglePlayScene 的 1920×1080 Viewport 左上角 (24,0)，尺寸 48×48；FPS 面板左侧从 x36 移至 x82，y2 不变，不挡下方判定计数器。
- `CircularHitArea` 让圆内部（含图标透明中心）可点击，四个角不接收按钮点击。原键盘暂停、菜单动画和鼓面输入隔离保持不变。迁移菜单 `OurTaiko/Apply Circular Pause Button`，重复执行不覆盖保存的布局。
- 验证：鼠标／触控暂停恢复与圆形射线范围 1/1，键盘菜单导航 1/1；场景迁移重复执行文件不变，Editor 编译无错误。截图 `TestResults/CircularPauseButton.png`。整套暂停测试因 Editor 中途退出未得到完整结果，重开后上述两项单独通过；尚未在实体手机上验证。


## ScoreRank（2026-10-03）

- `ScoreRank.FromScore` 统一计算七档：500000／600000／700000／800000／900000／950000／1000000，对应白／铜／银／金／粋／雅／極；低于 500000 隐藏。原项目来源：Nijiiro `Scripts/result/result_player.lua` 的 `rank_of`。
- **仅七个图标 Prefab**：`Assets/OurTaiko/Generated/ScoreRank/`。原图来自 `game/rank_result_anim/s74` 至 `s86`（每隔 2）；用户明确不要 yellow_box 的成绩等级素材。Result、选曲歌曲板、难度牌复用这七个 Prefab 的 Sprite，在已保存 Image 中更新；选曲不预建多套图标、不在每次切歌时创建对象。默认设计尺寸分别 208／72／40；White 原图 208×160 保持比例。
- `ScoreRankView` 保存共享 Prefab 引用与 Image，歌曲板在开合时按保存位置加 `rankOpenOffset`。歌曲板选取有等级记录的最高难度，进入难度选择优先当前难度；难度牌各显示本难度，里魔王切换沿用原显隐时序。数据走 `SongScores`，本地读 SQLite，在线只读服务器成绩；等级从最高分派生，不新增存储字段。自动演奏仍不写成绩。
- Result 使用单独 `ScoreRankAnimation` 图层播放 Nijiiro 的两份 `rank_result` 数据表（C# 解析数据，不执行 Lua），保留 60 Hz、缩放、旋转、形状原点和加色混合；7 档共用图标，極有独立演出。修正源脚本只映射 shape 74 而后续帧使用 shape 76 的问题，全部等级形状都映射到实际等级。
- `ResultSequence.RankAtMs` 在分数完成后的原皇冠时间开始，皇冠／评语延后 2000 ms；未过关也按分数显示等级。音效通过 `AudioPlayback` 播放 `scorerank_c.ogg`。跳过立即显示最终图标，不重播等级音效；低分不插入该状态。
- Editor 迁移菜单 `OurTaiko/Apply Score Rank`；只向已有场景／SongBoard 添加绑定，不重建原布局，已存在图标与位置保留。结算 Inspector 预览随示例分数显示银级。
- 验证：ScoreRank EditMode 21/21、既有选曲／结算核心测试 7/7、新增三处显示 PlayMode 1/1、原有选曲→游玩→结算→返回 PlayMode 2/2；两个场景、SongBoard 与七个 Prefab 共 10 个文件在连续两次迁移后哈希不变。报告 `TestResults/score-rank-*.json`，三处截图 `ScoreRankSongBoard.png`／`ScoreRankCourse.png`／`ScoreRankResult.png`。未重新构建独立 Player。

## PracticeScene 练习模式（2026-10-04）

入口为 Entry「演奏ゲーム」下一项「練習モード」，先经过 ServerLogin 再进入现有 SongSelect，以连接服务器并显示账号历史成绩；选曲、难度、演奏选项和 SongLoadingScene 在线歌曲下载均复用原流程。Entry 在决定模式时设置 `SceneSwitcher.PracticeMode`，登录保留该标记，SongLoadingScene 按 `SelectedPlayScene` 进入 PracticeScene，练习返回选曲后继续保持该模式。再次从 Entry 选择普通演奏会清除练习标记。登录界面在练习模式说明可查看历史成绩、练习成绩不保存。

`ProjectBuilder.ApplyPracticeMode()` 先用 `AssetDatabase.CopyAsset` 完整复制 SinglePlayScene 为 `Assets/Scenes/PracticeScene.unity`，保留原游玩层级、素材、音符池、输入互斥、动画与可编辑布局；只在副本增加顶部 `PracticeControls`／`PracticeView`。Entry 只复制新增一块模式板并移动后续板到下一槽位，使用 Nijiiro 原始 `entry/mode_select/box/2.png`、`3.png`（完整 Sprite，来源登记于 ImportedAssets.json）。迁移会拒绝覆盖未保存场景，已有练习控件和入口布局保留，同时将已有练习入口更新为 ServerLogin，可重复执行。

- 首次进入停在第一小节，显示「小节进度」。播放中第一次按暂停会停止音频、冻结歌曲时钟，清空 JudgeCounter、分数、魂槽、连击和反馈特效。咔控制前后小节，咚进入「播放速度」，再咚从选定位置继续。顶部左右／决定按钮提供同样操作；触控鼓遵循原设置。
- 小节取解析后的 `Chart.Bars`，包含隐藏小节线及真实 BPM／拍号／DELAY／OFFSET。分支采用已经选中的路线，尚未判定的分支预览普通路线；不会把三条路线同时列成小节。`PracticeProgress` 在 200 ms 内插值谱面时间，交给原 `RenderNotes`，小节线与音符、连打头尾均通过原位移公式滚动。
- 播放速度使用整数十分位，默认 1.0x、每次 ±0.1x、范围 0.1x–3.0x（兼容 Unity AudioSource 的上限），独立于 HS，不修改演奏设置。`SongClock.Rate` 同时影响谱面时间、负时间预备段、音乐 seek 与调度提前量。原生 BASS FX 使用 Tempo 保持音高；Unity 后备使用 AudioSource.pitch，因此 Unity 后备变速也会改变音高。鼓音和提示音不变速。
- `PlaySession.PracticeAt` 新建练习计数并直接定位事件游标，保留游标之前已经选择的分支、不执行历史判定或漏音惩罚；后退后未来分支重新判定。长音符中途开始可继续击打，自动演奏不会补算跳过部分的连打。画面偏移换算包含音画偏移，使选择的小节线对齐判定点。
- 练习暂停中再次按暂停打开原 SinglePlay 暂停菜单。Resume 回到练习调整层；Restart 回到第一小节并停在调整层；只有菜单 Back to Song Select 返回选曲。Esc／Space 只操作暂停层，快捷 Restart 在练习中禁用。失焦先进入练习暂停，不会退出。
- 播放结束直接回到第一小节并打开调整层，保留当前播放速度；不进入 Result、不累计 SongsPlayed、不保存本地最佳成绩、不调用在线上传。
- **结算负责保存／上传（用户决定，2026-10-04）**：`PlayScene.Finish` 仅生成正常游玩的 `PlayResult` 并调用 `SceneSwitcher.ShowResult(result, song, record)`；不再写入成绩或调用上传。`ResultScene.Awake` 在创建演出序列与绑定视图之前调用 `SaveScore()`，一次性领取本局歌曲和回放，本地歌曲保存到 ScoreStore，在线歌曲读取原最高分并提交现有持久化上传队列；网络发送与重试继续由 FanmadeClient 执行。练习不进入 ResultScene，因此不会触发保存／上传。领取后清空交接数据，重载 Result 保留显示但不重复提交，也不覆盖 PreviousBest；无本局交接的独立预览和自动演奏不保存。

参考仅只读：OurTaikoPlayer `src/scenes/game_practice.cpp` 的小节列表／200 ms scrobble／十分位速度与 `practice_player` 的 seek/reset；MajdataPlay `Scenes/Practice/PracticeManager.cs` 的独立 PlaybackSpeed、`GamePlayManager.cs` 的练习循环和音频 sample 的变速接口。界面交互以本次用户指定的两项顺序及两层暂停为准。

验证：进行中 EditMode 99/99、已完成 EditMode 143/143；练习 PlayMode 3/3（BASS／Unity 的 0.8x 与 1.2x 实际音频位置和谱面时间、鼠标按钮、200 ms 小节滚动、结束回首小节及无成绩保存）与直接启动 PracticeScene 1/1；普通暂停回归 5/5、已完成场景回归 34/34。报告 `TestResults/practice-*.json`，截图 `PracticeMeasures-{Bass,Unity}.png`、`PracticeMeasures720-{Bass,Unity}.png`、`PracticeSpeed-{Bass,Unity}.png`。连续执行迁移两次，Entry／PracticeScene／SinglePlayScene 三个文件的 SHA-256 均不变。验证环境为 macOS Unity Editor，未在 Windows／Android／iOS 设备上实测。

练习入口接入登录后的补充验证：`ServerLoginFlowTests` 6/6（本地模拟服务器，包含从 Entry 选择练习、登录、显示历史最佳成绩／皇冠、下载 TJA／音频、进入 PracticeScene、结束不上传且历史成绩不变）；`PracticeFlowTests` 4/4（无服务器入口、BASS／Unity 两层暂停和变速、直接启动）。报告 `TestResults/practice-server-login-playmode.json`、`TestResults/practice-login-route-regression.json`；重复迁移后上述三个场景文件内容不变。

成绩处理移到 ResultScene 后验证：`ResultScoreFlowTests` 1/1（结算加载前无本地写入、进入后保存、重载不重复保存且 PreviousBest 不变、自动演奏不保存）；`ServerLoginFlowTests` 6/6（结算加载前没有上传队列／已上传记录，进入后提交并保留回放，重载不重复上传，练习仍不提交）。报告 `TestResults/result-score-local-playmode.json`、`TestResults/result-score-online-playmode.json`，使用本地模拟服务器，未向真实账号上传测试成绩。


## 总分上方的单次加分数字（2026-10-04）

参考 OurTaikoPlayer `score_counter_animation.cpp`、`player.cpp` 的 `base_score_list` 与 Nijiiro `Graphics/game/animation.json` 35–39。加分数字复用总分的 `score_number` 0–9（56×64，间距 30），1P 为橙色 (254,102,0)，只显示本次增加的数字，没有加号。50 ms 淡入；前 80 ms 右端由 x285 移到 x255；y255 保持至 146 ms 后切至 y219＋(位序＋1)×5，279.36–345.36 ms 再上移 3；366.74–446.74 ms 淡出。纹理 y=-272 与总分基线统一换算，不改原总分位置和即时计分规则。

`ScoreCounterView.Show` 读取相邻两次实际分数的正增量，生成独立 `ScoreAdditionView`；良、可、大音符以及 5／6／7／9 号每次长音符击打均使用真实增量，不可／初始赋值／相同分数不触发。为满足每次加分都显示，不沿用参考代码对普通击打／连打同时 5 条后的丢弃逻辑；行对象按需扩充、过期复用，不覆盖上一条。动画与现有总分弹动共用 FrameTime。降分／练习清零时清除旧行。

SinglePlayScene 与 PracticeScene 均保存 `NoteLane/ScoreCounter/ScoreAddition` 模板和十位数字引用，`ScoreAddition.anim` 控制透明度与位移，运行时仅复用行和填写数字。新控件相对于总分定位，跟随画布缩放；加分行使用独立绘制层避免被本项目的 JudgeCounter 下缘遮挡，仍低于全局幕布。迁移菜单 `OurTaiko/Apply Score Addition` 只补缺少的模板，不重建已有布局。没有新增图片或修改计分。

验证：ScoreAdditionClipTests 2/2、ScoreAdditionFlowTests 2/2、原 ScoreGaugeFlowTests 1/1；覆盖两场景的动画时间／相对锚点、良／可／不可、5／7／9 号击打增量、独立多行、复用、练习清零与原有计分／结算。1080p／720p 截图 ScoreAddition-{SinglePlayScene,PracticeScene}.png、ScoreAddition720-{SinglePlayScene,PracticeScene}.png 已检查。迁移再次执行后两个场景与动画文件内容不变。报告 TestResults/score-addition-*.json；未重新构建独立 Player。


## A/B 全局延迟与数值设置（2026-10-04）

参照 MajdataPlay `dc19722d`：`GamePlayManager` 将 `AudioOffset` 加到谱面时间；`NoteDrop.JudgeTimingWithOffset` 只将 `JudgeOffset` 加到判定时间；`GameOffsetEnumerator` 的秒制步长为 0.001 秒。OurTaikoPlay 在 Settings › Play 添加 **Offset A (Audio)** 和 **Offset B (Judgment)**，单位固定 ms，默认 0、步长 1；正值推迟，负值提前。A 同时移动音符、小节线和判定相对音乐的位置，B 只移动手动判定窗口（包括漏判、连打及气球有效区间），不移动谱面显示或自动演奏节拍。

`PlaySettings.audioOffsetMs/judgeOffsetMs` 写入原 `settings.json`，缺少字段的旧配置仍为 0。`SettingItem.Number` 接受 defaultValue、step、unit 与字段读写委托，不生成庞大的选项数组。弹窗中央显示当前毫秒数，左右咔或触控箭头按步长调整，咚或点击数值确认保存；Esc／点遮罩取消。说明显示默认值、步长和操作方式；枚举／开关弹窗保持原逻辑。直接复用 GlobalSettingScene 已保存的控件，无需重新生成场景。

SinglePlayScene 与 PracticeScene 共用 PlayScene，在开始时锁定本局偏移。音乐时钟保持音轨进度：谱面时间 = SongTime − A；手动判定时间 = 谱面时间 − B；分支和视觉特效使用谱面时间。兼容既有 SongDefinition 序列化偏移，但不增加单曲设置入口。练习的小节游标采用视觉谱面时间，定位歌曲时只加 A（以及已有视觉偏移），B 不改变定位；重置计数、跳小节、恢复、结束回首小节均保留 B。变速沿用谱面时间单位，A/B 与谱面一起受速度影响（例如 0.8x 下 100 ms 谱面偏移对应 125 ms 实际时间）。

Replay v1 输入继续保存校正后的判定毫秒数，不重复补偿；为保持旧双偏移约定，audio_offset_ms 记录有效 A+B，visual_offset_ms 记录既有视觉偏移−B，因此显示时间仍可由判定时间−visual_offset_ms 重建。练习仍不进入 Result、不保存或上传成绩。

验证：EditMode 设置相关 22/22、BranchTests 27/27、PracticeTests 5/5；PlayMode OffsetFlowTests 5/5（正负 A/B、两场景实际击打与音频时钟、0.8x 练习定位／重置、数值保存与取消、键盘与触控），GlobalSettingFlowTests 3/3、PracticeFlowTests 4/4（含 BASS 与 Unity 后备）、DrumInputMutexTests 2/2。1080p／720p 弹窗截图已检查，无文字重叠。报告 `TestResults/offset-*.json`；验证环境为 macOS Unity Editor，未重新构建独立 Player。

## 首页三项可见与选曲返回牌尺寸（2026-10-04）

Entry 原先沿用 Nijiiro `Scripts/entry/box.lua` 的相邻项可见规则（`abs(relativeSlot) <= 1`），所以选中演奏时第三项设置透明度为 0；仅恢复透明度又会使设置落到画布底部之外。现在保持三项可见，保留 `mode_list` 槽位、各牌编辑位置及 9 帧滑动时序，用 `EntryView.modeSafeArea`（高度比例 0.15–0.925）将牌组整体移入顶部控件与底部 footer 之间。选中项打开，其余收起；三个焦点状态均能看到并点击全部三项。Inspector 预览同样显示三项并调整组位置。

返回牌 `bar_genre_back.png` 原先被 Unity 自动导入为 Multiple，并裁成 952×334、border=0 的子图，TextureImporter 的上下 56 边框没有应用到子图。修复后以 Single 使用完整 960×352 图片及上下 56 边框，与歌曲牌相同，闭合高度 164、展开高度 352。`folder_graphic` 的 13 张图同样修复完整贴图导入，FolderBoard 的展开图改用 Sliced；角色图 `box_chara` 仍保留左右切片。通过 `ApplySongSelectFolders()` 修改导入器、Prefab 和场景 Sprite 引用，未改变源 PNG 或重建场景布局。

验证：PlayMode `MenuBoardLayoutTests` 2/2、`EntryTouchFlowTests` 1/1、`GlobalSettingFlowTests` 3/3、`ServerLoginFlowTests` 6/6（本地模拟服务器）。覆盖三项各自选中时全部可见、真实 EventSystem 点击命中、进入设置、返回牌展开／闭合与歌曲牌同尺寸、文件夹开合与返回。1080p／720p 截图 `EntryThreeModes*.png`、`ReturnBoard*.png` 已检查；报告 `TestResults/menu-board-*.json`。Editor 重复预览位置稳定、三项均启用；重复迁移检查的 54 个场景／预制体／素材元数据文件哈希均不变。验证环境为 macOS Unity Editor，未重新构建独立 Player。

## Fanmade 歌曲 ID 与资源直连（2026-10-05）

接入契约：相邻后端 `docs/GAME_CLIENT_RESOURCE_DOWNLOAD.md`。每个 endpoint 重连后读取 `songIdOnly`、`resourceDownloadVersion`、`courseKeyedDifficulties`，旧服务器保持版本路由；未知下载协议号明确拒绝。base URL 路径前缀仍保留。

资源清单校验歌曲 ID、HTTPS URL、SHA-256、64 位大小及到期时间；音频扩展名来自 contentType。独立资源请求不带 API token、Cookie、幂等键，不重定向，也不会触发游戏重新登录。准备操作最多重新获取一轮详情/清单，处理详情竞态、即将过期、403/404 和暂时错误；禁止失败后退回代理下载。WebGL 使用独立无 token 请求，仍需源站 CORS，未验证浏览器直连。

缓存按 endpoint（服务器+账号）、歌曲、资源种类、哈希保存，实际读盘校验大小和内容；旧版本目录仅在实际内容验证后导入。两份资源验证完成才发布可播放组合，翻译变化仍重建 play.tja；不持久保存签名 URL。临时写入使用原子替换，取消或失败保留旧组合。

新版成绩请求完全移除 versionId，在线最佳成绩键为 songId/course，bootstrap 完整替换快照。SQLite outbox 保存本局两份哈希；上传前核对当前详情和清单，文件变化或旧记录缺少可信哈希时保留记录并停止自动上传。可信旧记录转换只更新原记录的 body，保持幂等键。409/404 等永久拒绝维持 rejected，不自动换 key。

翻译字典完整保留在 DTO，显示按所选语言→en→原文，zh-Hans 映射 zh；本地 TJA 的既有语言回退不变。双人资源按 COURSE + #START P1/P2 匹配，与数组顺序无关。当前单人玩法用两个标有 P1/P2 的歌曲条目分别选择谱面，成绩使用完整 Oni_1p/Oni_2p 等 course，未增加同时双人游玩。

自动测试与现场验证结果见本次交付记录；在线分块试听按用户要求在本次协议提交之后另行接入。

协议阶段验证：`ResourceDownloadTests` 19/19（独立 API/资源 HTTP fixture）、旧 `FanmadeClientTests` 19/19、`ServerLoginFlowTests` 6/6；报告为 `TestResults/resource-*.json`。Unity Editor 6000.3.25f1 编译通过。使用无账号临时缓存从正式 Fanmade 拉取曲库、直连下载并解析真实歌曲，第二次准备的 TJA/audio 状态均为 Cached。未上传真实账号成绩；未构建移动端/桌面 Player，未验证 WebGL CORS、CloudFront 或实机。
