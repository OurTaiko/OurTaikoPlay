# OurTaikoPlay

**A taiko rhythm game built with Unity and C#, featuring a Nijiiro-style interface.**

[简体中文](README.md) · **English** · [日本語](README.ja.md)

OurTaikoPlay is a taiko rhythm game written from scratch in Unity and C#, drawing inspiration from the design and gameplay of projects such as [OurTaikoPlayer](https://github.com/OurTaiko/OurTaikoPlayer) and [MajdataPlay](https://github.com/TeamMajdata/MajdataPlay). It is under active development, with a focus on single-player play, keyboard and touch input, local and online songs, and scenes and UI that remain editable in the Unity Editor.

## Features

- **A complete single-player flow**: Entry → server login / guest access → song selection → loading → gameplay → results, with pause, restart, and return to song selection.
- **Nijiiro presentation**: song boards, difficulty cards, nameplates, soul gauge, dancers, crowns, and seven ScoreRank icons—including 粋, 雅, and 極—with a results animation.
- **TJA gameplay**: common note types, drumrolls, balloons, BPM and scroll changes, and Normal / Expert / Master branches.
- **Online catalogs and scores**: built-in Fanmade and ESE server configurations, account login, guest browsing, category folders, chart and audio downloads, best-score retrieval, and queued score uploads with retries.
- **Score display**: local records stored in SQLite; online history supplied by the server. Song selection shows best scores, crowns, and ScoreRank. Online crowns use `ClearStatus`.
- **Play and system settings**: autoplay, speed, Doron, swapped note colors, randomization, drum sounds, volume groups, audio backend, frame rate, and an on-screen drum toggle.
- **Native audio**: a BASS backend with a design inspired by MajdataPlay, with a Unity audio backend option; Windows also supports WASAPI / ASIO.

## Run from source

Install **Unity 6000.3.25f1**, Unity Hub, and Git. The project uses **URP 17.3.0 / Universal 2D, uGUI, TextMeshPro, and the Input System**.

```sh
git clone --recurse-submodules https://github.com/OurTaiko/OurTaikoPlay.git
cd OurTaikoPlay
```

For an existing checkout, initialize or update the ManagedBass submodule:

```sh
git submodule update --init --recursive
```

1. Open the project in Unity Hub and wait for package resolution and asset imports to finish.
2. Open `Assets/Scenes/Entry.unity` and press Play.
3. Hit Don or tap the screen to join, then choose 「演奏ゲーム」.
4. Log in to a server, continue as a guest, or skip the servers to use local songs.
5. Choose a song and difficulty, then play.

The repository includes TRIPLE HELIX, Input Calibration, and Branch Training examples. The latter two are silent calibration / practice charts. Choose 「ゲーム設定」 on Entry to open system settings.

## Controls

| Context | Action | Default keys |
| --- | --- | --- |
| Gameplay | Left / right Don | F / J |
| Gameplay | Left / right Ka | D / K |
| Song selection and menus | Move | D / K or ← / → |
| Song selection and menus | Confirm | F / J or Enter |
| Menus | Go back one level | Esc |
| Gameplay | Pause / resume | Space or Esc |
| Gameplay | Restart | F1 |

Mouse and touch controls are supported, including on-screen drums during play. The pause menu contains **Resume / Restart / Back to Song Select**. For songs with both Oni and Ura Oni, press right ten consecutive times while on Oni to switch.

A large note needs only one side of the drum for a full judgment. Only the earliest drum input in each frame is processed; this is an intentional gameplay rule. Autoplay results are neither saved nor uploaded. Menu timers are decorative and do not count down.

## Songs and user data

Online servers are configured in `Application.persistentDataPath/servers.json`, with `https://fanmade.ourtaiko.org` and `https://ese-backend.llx.life` included by default. Set an entry's `enabled` field to `false` to disable it.

Local songs are currently configured as Unity assets: import TJA content as `.txt`, create a `SongDefinition` via **Assets → Create → OurTaiko → Song**, assign its chart, music, and offsets, then add it to the SongSelect controller's `songs` list.

User data lives in Unity's `Application.persistentDataPath`:

| File | Contents |
| --- | --- |
| `settings.json` | System settings, including volume, display, and advanced audio parameters |
| `options.json` | Play options |
| `player.json` | Player nameplate |
| `servers.json` | Online server and login configuration |
| `scores.sqlite3` | Local best scores and pending uploads separated by server / account |

General → Language currently changes song titles and subtitles only, preferring Japanese when a translation is unavailable. Full menu localization is not yet available. README translations are independent of the in-game language setting.

## Build

Use **OurTaiko → Build** in Unity. Install the appropriate platform modules through Unity Hub first.

| Target | Output | Architecture |
| --- | --- | --- |
| macOS | `Builds/macOS/OurTaikoPlay.app` | Intel + Apple Silicon |
| Windows | `Builds/Windows/OurTaikoPlay.exe` and adjacent dependencies | x64 |
| Android | `Builds/Android/OurTaikoPlay.apk` | ARM64 |
| iOS / iPadOS | `Builds/iOS/OurTaikoPlay.xcodeproj` | ARM64 devices |

iOS builds require macOS and Xcode. Automatic signing defaults to the Hoshino Network LLC team; sign in to Xcode with a developer account that has access to that team, or change the team in Unity Player Settings. Mobile builds use `org.ourtaiko.play` and landscape orientation.

## Development status and documentation

The current focus is single-player gameplay with the Nijiiro skin. Two-player play, Dan courses, search / sorting, and full replay playback are not implemented, and not every TJA extension is supported. Runtime logic and Editor tools use C#; native audio libraries are platform dependencies. Imported animation tables are read as data, without executing Lua.

- [Asset manifest](Documentation/ImportedAssets.json): imported artwork and audio sources.
- Tests are in `Assets/OurTaiko/Tests/`. Run EditMode and PlayMode tests with Unity Test Runner; `Finished/` retains regression tests for completed features.

Bug reports through [Issues](https://github.com/OurTaiko/OurTaikoPlay/issues) and Pull Requests are welcome. For gameplay, audio, or touch issues, include your platform, device, reproduction steps, and relevant logs.

## Acknowledgments

1. **[Sigaer](https://github.com/Sigaer)** — Thank you for providing the project's [DDFont.ttf](https://github.com/OurTaiko/OurTaikoPlay/blob/main/Assets/OurTaiko/Art/DDFont.ttf) font! And a special thank-you for using up my Project Sekai stamina for me so I can focus on making this game.
2. **[TeamMajdata / MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)** — For inspiration in scene-transition and audio design, and for maintaining the [ManagedBass fork](https://github.com/TeamMajdata/ManagedBass).
3. **Third-party library and asset creators** — Thank you to the creators of BASS, [unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net), [Material Symbols](Documentation/ThirdParty/MaterialSymbols/README.md), and the skins, artwork, music, sound effects, and charts. See [NOTICE](NOTICE) and the [asset manifest](Documentation/ImportedAssets.json) for attribution.

## License and asset attribution

Project code is licensed under [GNU GPL v3](LICENSE); see [NOTICE](NOTICE) for source and third-party acknowledgments. Third-party libraries, fonts, skins, artwork, music, sound effects, and charts retain their respective ownership and licenses. The code license does not replace the licenses of those resources.
