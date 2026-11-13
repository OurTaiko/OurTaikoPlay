# OurTaikoPlay

**A X-Platform Taiko Simulator built with Unity and C#.**

[![Project Status: Active Development](https://img.shields.io/badge/Project%20Status-Active%20Development-brightgreen.svg)](https://github.com/OurTaiko/OurTaikoPlay)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![Unity: 6000.3](https://img.shields.io/badge/Unity-6000.3-black.svg?logo=unity)](https://unity.com/)

[简体中文](README.md) · **English** · [日本語](README.ja.md)

OurTaikoPlay is an open-source Taiko rhythm game built from scratch in Unity (C#), featuring arcade-authentic Nijiiro visuals, low-latency native audio, versatile game modes, and online song library integration.

## Features

- **Full Gameplay Loop**: Entry → Song Selection → Gameplay → Results. Supports standard notes, drumrolls, balloons, Kusudama, BPM/scroll changes, and branch charts (Normal / Professional / Master).
- **Game Modes**: Single-player, Two-player (Double Play with shared Kusudama), Ghost Battle, and Dan-i Dojo (including Gaiden courses).
- **Nijiiro Presentation**: Song boards, difficulty cards, nameplates, soul gauge, dancers, crowns, seven-tier ScoreRank icons, and result animations.
- **Low-Latency Audio**: Native audio mixing and decoding via ManagedBass (inspired by MajdataPlay), supporting BassSimple, BassWASAPI, and BassASIO.
- **Song Management**: Reads local `.tja` charts and nested `box.def` folders. Connects to built-in OurTaiko online servers with chart downloads, offline caching, and score synchronization.
- **Cross-Platform & Accessible**: Keyboard, mouse, and on-screen touch drums. Full UI localization in English, Japanese, and Simplified Chinese.

## Controls

| Context | Action | Default Keys |
| --- | --- | --- |
| Gameplay | Left / Right Don | `F` / `J` |
| Gameplay | Left / Right Ka | `D` / `K` |
| Menus / Selection | Move Left / Right | `D` / `K` or `←` / `→` |
| Menus / Selection | Confirm | `F` / `J` or `Enter` |
| Menus / Selection | Back | `Esc` |
| Gameplay | Pause / Resume | `Space` or `Esc` |
| Gameplay | Restart | `F1` |

- Supports touch screen drums and mouse clicks.
- Big notes register fully on a single-side strike.
- For charts with both Oni and Ura Oni, press Right 10 times consecutively on the Oni card to toggle.

## Running from Source

### Prerequisites

- **Unity 6000.3.25f1** or compatible 6000.3 version
- Unity Hub and Git

### Clone the Repository

```sh
git clone --recurse-submodules https://github.com/OurTaiko/OurTaikoPlay.git
cd OurTaikoPlay
```

*If already cloned without submodules, run: `git submodule update --init --recursive`*

### Launch

1. Open the project in Unity Hub and let package resolution and asset import finish.
2. Open `Assets/Scenes/Entry.unity` and click **Play**.
3. Hit Don or tap the screen to join, then select 「演奏ゲーム」 to play.

## Songs & User Data

- **Local Songs**: Place `.tja` charts and accompanying audio files into the `Songs/` folder inside your data directory (created on first run). Nested folders with `box.def` are supported.
- **Online Cache**: Songs played from the server are automatically cached in `cache/` for offline play.

### Data Directories

| Platform | Location |
| --- | --- |
| Windows / Linux | Game executable directory (falls back to `persistentDataPath` if read-only) |
| macOS | Inside `OurTaikoPlay.app` (Right-click → Show Package Contents) |
| iOS / iPadOS | Files app → On My iPad / iPhone → OurTaikoPlay |
| Android / Unity Editor | `Application.persistentDataPath` |

Core data files: `settings.json` (system & audio settings), `options.json` (gameplay modifiers), `player.json` (profile & nameplate), `scores.sqlite3` (local records & cached chart index).

## Building

Build the player using **OurTaiko → Build** inside Unity.

| Target | Output | Architecture |
| --- | --- | --- |
| Windows | `Builds/Windows/OurTaikoPlay.exe` | x64 |
| macOS | `Builds/macOS/OurTaikoPlay.app` | Universal (Intel + Apple Silicon) |
| Android | `Builds/Android/OurTaikoPlay.apk` | ARM64 |
| iOS / iPadOS | `Builds/iOS/OurTaikoPlay.xcodeproj` | ARM64 |

*Note: iOS builds require macOS and Xcode.*

## Acknowledgments

- **[Sigaer](https://github.com/Sigaer)** — Providing the [DDFont.ttf](https://github.com/OurTaiko/OurTaikoPlay/blob/main/Assets/OurTaiko/Art/DDFont.ttf) font.
- **[TeamMajdata / MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)** — Audio architecture inspiration and the [ManagedBass fork](https://github.com/TeamMajdata/ManagedBass).
- **[YataiDON](https://github.com/Yonokid/YataiDON)** & **[OpenTaiko](https://github.com/0auBSQ/OpenTaiko)** — Gameplay and chart parsing references.
- Authors and contributors of BASS, [unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net), [Material Symbols](Documentation/ThirdParty/MaterialSymbols/README.md), and community art/audio assets (see [NOTICE](NOTICE) and [ImportedAssets.json](Documentation/ImportedAssets.json)).

## License

Project code is licensed under the [GNU GPL v3](LICENSE). Third-party libraries, fonts, artwork, and audio assets retain their respective licenses and attribution (see [NOTICE](NOTICE)).
