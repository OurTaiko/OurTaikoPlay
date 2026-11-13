# OurTaikoPlay

**使用 Unity 与 C# 开发的跨平台太鼓模拟器。**

[![Project Status: Active Development](https://img.shields.io/badge/Project%20Status-Active%20Development-brightgreen.svg)](https://github.com/OurTaiko/OurTaikoPlay)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![Unity: 6000.3](https://img.shields.io/badge/Unity-6000.3-black.svg?logo=unity)](https://unity.com/)

**简体中文** · [English](README.en.md) · [日本語](README.ja.md)

OurTaikoPlay 是一个使用 Unity (C#) 开发的开源太鼓节奏游戏，提供还原街机体验的 Nijiiro 风格界面、低延迟原生音频、多模式演奏与在线曲库支持。

## 主要特性

- **完整演奏流程**：Entry → 选曲 → 演奏 → 结算。支持常规音符、连打、气球、くす玉、BPM/滚动变化及普通/玄人/达人谱面分支。
- **多种模式**：单人演奏、同屏双人演奏（共享くす玉）、ゴーストバトル（幽灵对战）与段位道场（含外传课程）。
- **Nijiiro 视觉表现**：还原曲目板、难度卡、名牌、魂槽、舞者、皇冠、七阶 ScoreRank 图标与结算演出。
- **原生低延迟音频**：基于 BASS 混音与解码（参考 MajdataPlay），支持 BassSimple、BassWASAPI 与 BassASIO。
- **曲库与选曲**：支持读取本地 `.tja` 谱面与 `box.def` 分类文件夹；内置 OurTaiko 在线曲库，支持曲目下载、自动离线缓存与成绩同步。
- **多端操作与多语言**：支持键盘、鼠标与屏幕触控鼓；提供简中、英语、日语完整界面语言。

## 操作说明

| 场景 | 操作 | 默认按键 |
| --- | --- | --- |
| 演奏 | 左／右 咚（面） | `F` / `J` |
| 演奏 | 左／右 咔（渊） | `D` / `K` |
| 选曲与菜单 | 左右移动 | `D` / `K` 或 `←` / `→` |
| 选曲与菜单 | 确认选择 | `F` / `J` 或 `Enter` |
| 菜单 | 返回上一级 | `Esc` |
| 演奏 | 暂停／继续 | `Space` 或 `Esc` |
| 演奏 | 重新开始 | `F1` |

- 支持触控屏鼓面与鼠标点击。
- 大音符单侧敲击即可判定。
- 同时包含「おに」与「裏おに」的曲目，在おに难度上连续向右按 10 次即可切换。

## 从源码运行

### 环境要求

- **Unity 6000.3.25f1** 或更高 6000.3 系列
- Unity Hub 与 Git

### 获取代码

```sh
git clone --recurse-submodules https://github.com/OurTaiko/OurTaikoPlay.git
cd OurTaikoPlay
```

*如果已克隆仓库，请补齐子模块：`git submodule update --init --recursive`*

### 启动项目

1. 在 Unity Hub 中添加并打开项目，等待资源导入与包解析完成。
2. 打开 `Assets/Scenes/Entry.unity`，点击 **Play**。
3. 敲咚或点击画面加入，选择「演奏ゲーム」即可开始游玩。

## 曲目与数据存储

- **本地曲目**：将 `.tja` 谱面与对应音频放入数据目录下的 `Songs/` 文件夹（首次启动自动创建），支持使用 `box.def` 构建分类层级。
- **在线缓存**：游玩过的在线曲目与音频会自动缓存到 `cache/`，服务器离线时仍可游玩。

### 数据目录位置

| 平台 | 路径 |
| --- | --- |
| Windows / Linux | 游戏执行文件同级目录（不可写时转至 `persistentDataPath`） |
| macOS | `OurTaikoPlay.app` 包内（右键「显示包内容」） |
| iOS / iPadOS | 「文件」App →「我的 iPad／iPhone」→ OurTaikoPlay |
| Android / Unity Editor | `Application.persistentDataPath` |

主要数据文件包括：`settings.json`（系统设置）、`options.json`（演奏选项）、`player.json`（玩家档案）、`scores.sqlite3`（本地成绩与缓存索引）。

## 构建发布

在 Unity 菜单中选择 **OurTaiko → Build** 进行打包。

| 平台 | 输出产物 | 架构 |
| --- | --- | --- |
| Windows | `Builds/Windows/OurTaikoPlay.exe` | x64 |
| macOS | `Builds/macOS/OurTaikoPlay.app` | Universal (Intel + Apple Silicon) |
| Android | `Builds/Android/OurTaikoPlay.apk` | ARM64 |
| iOS / iPadOS | `Builds/iOS/OurTaikoPlay.xcodeproj` | ARM64 |

*注：iOS 构建需在 macOS 上使用 Xcode。*

## 致谢

- **[Sigaer](https://github.com/Sigaer)** — 提供 [DDFont.ttf](https://github.com/OurTaiko/OurTaikoPlay/blob/main/Assets/OurTaiko/Art/DDFont.ttf) 字体支持。
- **[TeamMajdata / MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)** — 音频架构启发与 [ManagedBass 分支](https://github.com/TeamMajdata/ManagedBass) 维护。
- **[YataiDON](https://github.com/Yonokid/YataiDON)** & **[OpenTaiko](https://github.com/0auBSQ/OpenTaiko)** — 玩法表现与谱面解析参考。
- 以及 BASS、[unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)、[Material Symbols](Documentation/ThirdParty/MaterialSymbols/README.md) 与社区美术、音频资源的作者（详见 [NOTICE](NOTICE) 与 [素材清单](Documentation/ImportedAssets.json)）。

## 许可证

本项目代码基于 [GNU GPL v3](LICENSE) 协议开源。第三方库、字体、美术与音频资源保留其各自权利归属与许可（详见 [NOTICE](NOTICE)）。
