# OurTaikoPlay

**用 Unity 与 C# 制作的太鼓节奏游戏，采用 Nijiiro 风格界面。**

**简体中文** · [English](README.en.md) · [日本語](README.ja.md)

OurTaikoPlay 是使用 Unity 与 C# 从零编写的太鼓节奏游戏，开发过程中借鉴了 [OurTaikoPlayer](https://github.com/OurTaiko/OurTaikoPlayer)、[MajdataPlay](https://github.com/TeamMajdata/MajdataPlay) 等项目的设计思路与玩法表现。项目持续开发中，目前专注于单人体验，提供键盘与触控操作、本地与在线曲目，以及可在 Unity Editor 中编辑的场景和 UI。

## 目前可以做什么

- **完整单人流程**：Entry → 服务器登录／游客模式 → 选曲 → 加载 → 演奏 → 结算，支持暂停、重开和返回选曲。
- **Nijiiro 界面**：曲目板、难度卡、名牌、魂槽、舞者、皇冠，以及包含「粋／雅／極」的七种 ScoreRank 图标和结算演出。
- **TJA 演奏**：支持常用音符、连打、气球、BPM／滚动速度变化和普通／玄人／达人分支。详细判定与支持范围见[判定说明](Documentation/JudgingSystem.md)和[开发记录](Documentation/PortingNotes.md)。
- **在线曲库与成绩**：内置 Fanmade、ESE 服务器配置，支持账号登录、游客浏览、分类文件夹、谱面与音频下载、最高分拉取，以及带重试队列的成绩上传。
- **成绩展示**：本地成绩存入 SQLite；在线历史成绩来自服务器。选曲显示最佳成绩、皇冠和 ScoreRank，在线皇冠读取 `ClearStatus`。
- **演奏与系统设置**：自动演奏、速度、ドロン、あべこべ、随机、鼓音，以及音量分组、音频后端、帧率和触控鼓开关。
- **原生音频**：采用 BASS 解码与混音，设计参考 MajdataPlay；输出后端为跨平台的 BassSimple，Windows 另有 BassWASAPI／BassASIO。不提供 Unity 音频后端（延迟过高），没有输出设备时静音运行。

## 从源码运行

需要 **Unity 6000.3.25f1**、Unity Hub 和 Git。项目使用 **URP 17.3.0 / Universal 2D、uGUI、TextMeshPro、Input System**。

```sh
git clone --recurse-submodules https://github.com/OurTaiko/OurTaikoPlay.git
cd OurTaikoPlay
```

如果已经克隆过仓库，请补齐或更新 ManagedBass 子模块：

```sh
git submodule update --init --recursive
```

1. 用 Unity Hub 打开项目，等待包解析与资源导入完成。
2. 打开 `Assets/Scenes/Entry.unity`，点击 Play。
3. 敲咚或点击画面加入，选择「演奏ゲーム」。
4. 在服务器页面登录、以游客进入，或跳过服务器只玩本地歌曲。
5. 选择歌曲和难度，开始演奏。

游戏不自带歌曲。本地歌曲放在 `Application.persistentDataPath/Songs`（首次启动自动创建；macOS 为 `~/Library/Application Support/OurTaiko/OurTaikoPlay/Songs`），每次经过服务器页面都会重新扫描：

- 读取 `.tja`（BOM 决定编码，无 BOM 时为 UTF-8，解码失败按 Shift-JIS），音频为同目录下 `WAVE:` 指定的文件；至少有一个 Easy～Edit 课程才列出。
- 含 `box.def`（自身或子目录）的顶层目录成为文件夹，标题与类别取 `#TITLE`／`#TITLE<语言>`、`#GENRE`，其下所有谱面按标题排序；其余谱面直接列在选曲列表，按文件名排序。本地文件夹排在服务器分类之前。

Entry 的「ゲーム設定」可进入系统设置。

## 操作

| 场景 | 操作 | 默认按键 |
| --- | --- | --- |
| 演奏 | 左／右咚 | F / J |
| 演奏 | 左／右咔 | D / K |
| 选曲与菜单 | 移动 | D / K 或 ← / → |
| 选曲与菜单 | 确认 | F / J 或 Enter |
| 菜单 | 返回上一级 | Esc |
| 演奏 | 暂停／继续 | Space 或 Esc |
| 演奏 | 重开 | F1 |

支持鼠标与触控；演奏时可使用屏幕鼓面。暂停菜单依次为 **Resume / Restart / Back to Song Select**。同时含おに与裏おに的曲目，在おに难度上连续向右操作 10 次可切换。

大音符单侧击打即可完整判定。每帧只处理最早的一次鼓面输入，这是当前玩法的有意设计。自动演奏成绩不保存、不上传；菜单计时器只作显示，不倒数催促选择。

## 曲目与存档

在线服务器从 `Application.persistentDataPath/servers.json` 读取；内置 `https://fanmade.ourtaiko.org` 和 `https://ese-backend.llx.life`。可将对应配置的 `enabled` 设为 `false` 停用服务器。

本地曲目目前通过 Unity 资源配置：将 TJA 内容导入为 `.txt`，通过 **Assets → Create → OurTaiko → Song** 创建 `SongDefinition`，设置谱面、音乐和偏移，再加入 SongSelect 控制器的 `songs` 列表。

用户数据位于 Unity 的 `Application.persistentDataPath`：

| 文件 | 内容 |
| --- | --- |
| `settings.json` | 系统设置，包括音量、显示和高级音频参数 |
| `options.json` | 演奏选项 |
| `player.json` | 玩家名牌 |
| `servers.json` | 在线服务器与登录配置 |
| `scores.sqlite3` | 本地最佳成绩，以及按服务器／账号隔离的待上传队列 |

General → Language 当前只影响歌名和副标题，缺失翻译时显示谱面原文（TITLE／SUBTITLE）；尚不提供整套菜单翻译。README 的语言版本与游戏内语言设置相互独立。

## 构建

在 Unity 中使用 **OurTaiko → Build**。先通过 Unity Hub 安装目标平台对应的构建模块。

| 目标 | 构建产物 | 架构 |
| --- | --- | --- |
| macOS | `Builds/macOS/OurTaikoPlay.app` | Intel + Apple Silicon |
| Windows | `Builds/Windows/OurTaikoPlay.exe` 及同目录依赖 | x64 |
| Android | `Builds/Android/OurTaikoPlay.apk` | ARM64 |
| iOS / iPadOS | `Builds/iOS/OurTaikoPlay.xcodeproj` | ARM64 真机 |

iOS 构建需要 macOS 和 Xcode；项目默认开启 Hoshino Network LLC 团队的自动签名，Xcode 需登录有该团队权限的开发者账号；使用其他团队时请在 Unity Player Settings 中修改。移动端包名为 `org.ourtaiko.play`，仅使用横屏。完整工具链、命令行构建与验证记录见[构建说明](Documentation/Building.md)。

## 开发状态与文档

目前专注于单人、Nijiiro 皮肤。双人演奏、段位模式、搜索／排序和完整回放播放尚未实现，也不保证兼容所有 TJA 扩展。运行逻辑与 Editor 工具使用 C#；原生音频库作为平台依赖提供，导入的动画表只作为数据读取，不执行 Lua。

- [构建说明](Documentation/Building.md)：各平台工具链、产物与验证边界。
- [判定说明](Documentation/JudgingSystem.md)：计时、判定与玩法细节。
- [开发记录](Documentation/PortingNotes.md)：设计思路、实现细节与已知限制。
- [素材清单](Documentation/ImportedAssets.json)：美术与音频的导入来源。
- 测试位于 `Assets/OurTaiko/Tests/`，可在 Unity Test Runner 中运行 EditMode 与 PlayMode 测试；`Finished/` 保留已完成功能的回归测试。

欢迎通过 [Issues](https://github.com/OurTaiko/OurTaikoPlay/issues) 报告问题，或提交 Pull Request。反馈演奏、音频或触控问题时，请附上平台、设备、复现步骤和相关日志。

## 致谢

1. **[Sigaer](https://github.com/Sigaer)** — 感谢提供项目使用的 [DDFont.ttf](https://github.com/OurTaiko/OurTaikoPlay/blob/main/Assets/OurTaiko/Art/DDFont.ttf) 字体！也特别感谢你帮我清 Project Sekai 的体力，让我能够专心写游戏。
2. **[TeamMajdata / MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)** — 感谢场景切换与音频设计方面的启发，以及维护 [ManagedBass 分支](https://github.com/TeamMajdata/ManagedBass)。
3. **第三方库与素材的作者** — 感谢 BASS、[unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)、[Material Symbols](Documentation/ThirdParty/MaterialSymbols/README.md)，以及皮肤、美术、音乐、音效与谱面的作者。相关归属见 [NOTICE](NOTICE) 与[素材清单](Documentation/ImportedAssets.json)。

## 许可与资源归属

项目代码遵循 [GNU GPL v3](LICENSE)，来源与第三方声明见 [NOTICE](NOTICE)。第三方库、字体、皮肤、美术、音乐、音效和谱面保留各自的权利归属与许可；代码许可证不替代这些资源的许可。
