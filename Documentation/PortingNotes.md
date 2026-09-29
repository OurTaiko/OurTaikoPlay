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

这是可运行的单人游玩基础模块，不是整个 OurTaikoPlayer 的等价移植。完整双人、段位、账号/联网、成绩上传、3D 咚角色及全部皮肤特效没有接入。真打基分和魂槽使用明确的简化实现；尚无大音符双手时间窗口。TJA 分歧、BMSCROLL/HBSCROLL、字母扩展音符不支持。未知非时序命令会写警告，分歧或不支持的音符会报错，不静默选择错误路线。

已验证桌面编辑器与当前两份谱面。移动端触控布局与真机音频延迟尚未验收。

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
