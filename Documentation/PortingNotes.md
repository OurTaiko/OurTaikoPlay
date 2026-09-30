# 单人游玩模块移植记录

## 原版到 Unity 的对应

| 原版参考 | Unity C# 实现 |
| --- | --- |
| `src/libs/screen.*`、`src/scenes/game.*` 场景生命周期 | `SceneSwitcher` + `LaunchMenu` + `PlayScene` |
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

这是可运行的单人游玩基础模块，不是整个 OurTaikoPlayer 的等价移植。完整双人、段位、账号/联网、成绩上传、3D 咚角色及全部皮肤特效没有接入。真打基分和魂槽使用明确的简化实现；尚无大音符双手时间窗口。已支持 p/r 分支；s 分数分支、LEVELHOLD、BMSCROLL/HBSCROLL、字母扩展音符不支持。未知非时序命令会写警告，不支持的分支条件、滚动模式或音符会报错。

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

原代码已经设置 `Application.targetFrameRate = 120`，默认 Ultra 画质的 `vSyncCount` 也为 0，没有固定 60 FPS 的代码限制。部分其他画质档开启了 VSync；桌面端启用 VSync 时会覆盖 `targetFrameRate`，以屏幕刷新率控制渲染。现在 `SceneSwitcher.Awake` 明确关闭 VSync、设置每帧渲染并以 120 FPS 为目标，入口和独立打开 PlayScene 都会应用。

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
