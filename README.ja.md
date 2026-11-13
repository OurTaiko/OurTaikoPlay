# OurTaikoPlay

**UnityとC#で開発された、クロスプラットフォーム対応の太鼓シミュレーター。**

[![Project Status: Active Development](https://img.shields.io/badge/Project%20Status-Active%20Development-brightgreen.svg)](https://github.com/OurTaiko/OurTaikoPlay)
[![License: GPL-3.0](https://img.shields.io/badge/License-GPL--3.0-blue.svg)](LICENSE)
[![Unity: 6000.3](https://img.shields.io/badge/Unity-6000.3-black.svg?logo=unity)](https://unity.com/)

[简体中文](README.md) · [English](README.en.md) · **日本語**

OurTaikoPlay は、Unity（C#）でゼロから開発されているオープンソースの太鼓リズムゲームです。アーケードさながらのニジイロ風 UI、低遅延のネイティブ音声、多彩なゲームモード、オンライン曲庫に対応しています。

## 主な機能

- **充実した演奏体験**：Entry → 選曲 → 演奏 → リザルト。基本音符、連打、風船、くす玉、BPM／スクロール変化、譜面分岐（普通／玄人／達人）に対応。
- **多彩なプレイモード**：1人プレイ、2人プレイ（くす玉共有の Double Play）、ゴーストバトル、段位道場（外伝コース対応）。
- **ニジイロ風の演出**：楽曲ボード、難易度カード、ネームプレート、魂ゲージ、踊り子、王冠、7種類の ScoreRank アイコン、リザルト演出。
- **低遅延ネイティブ音声**：ManagedBass による音声処理（MajdataPlay を参考）、BassSimple／BassWASAPI／BassASIO に対応。
- **楽曲管理・選曲**：ローカルの `.tja` 譜面と `box.def` フォルダー読み込みに対応。内蔵 OurTaiko サーバーからの曲ダウンロード、オフラインキャッシュ、成績同期をサポート。
- **マルチプラットフォーム・多言語**：キーボード、マウス、画面タッチ太鼓に対応。日本語・英語・簡体字中国語の UI 表示に対応。

## 操作方法

| 場面 | 操作 | 初期キー |
| --- | --- | --- |
| 演奏 | 左／右 ドン（面） | `F` / `J` |
| 演奏 | 左／右 カッ（フチ） | `D` / `K` |
| 選曲・メニュー | 左右移動 | `D` / `K` または `←` / `→` |
| 選曲・メニュー | 決定 | `F` / `J` または `Enter` |
| メニュー | 1つ前に戻る | `Esc` |
| 演奏 | 一時停止／再開 | `Space` または `Esc` |
| 演奏 | やり直し | `F1` |

- マウス操作および画面上のタッチ太鼓に対応しています。
- 大音符は片側を叩くだけで通常判定されます。
- おにと裏おにの両方がある楽曲は、おにの難易度カードで右方向を10回連続入力すると切り替わります。

## ソースから起動する

### 必要環境

- **Unity 6000.3.25f1** または互換性のある 6000.3 系
- Unity Hub および Git

### リポジトリのクローン

```sh
git clone --recurse-submodules https://github.com/OurTaiko/OurTaikoPlay.git
cd OurTaikoPlay
```

*すでにクローン済みの場合はサブモジュールを初期化してください：`git submodule update --init --recursive`*

### 実行手順

1. Unity Hub でプロジェクトを開き、パッケージ解決とアセットインポートを完了させます。
2. `Assets/Scenes/Entry.unity` を開き、**Play** ボタンを押します。
3. ドンを叩くか画面をタップしてエントリーし、「演奏ゲーム」を選んで開始します。

## 楽曲とデータ保存

- **ローカル楽曲**：データフォルダー内の `Songs/` に `.tja` 譜面と音源を配置します（初回起動時に自動作成）。`box.def` によるフォルダー分類に対応しています。
- **オンラインキャッシュ**：サーバーからプレイした曲は `cache/` に自動保存され、オフライン時でも演奏できます。

### データフォルダーの場所

| プラットフォーム | パス |
| --- | --- |
| Windows / Linux | ゲームの実行ファイルと同じフォルダー（書込不可時は `persistentDataPath`） |
| macOS | `OurTaikoPlay.app` の中（右クリック →「パッケージの内容を表示」） |
| iOS / iPadOS | 「ファイル」App →「この iPad／iPhone 内」→ OurTaikoPlay |
| Android / Unity Editor | `Application.persistentDataPath` |

主なデータファイル：`settings.json`（システム・音声設定）、`options.json`（演奏オプション）、`player.json`（ネームプレート情報）、`scores.sqlite3`（ローカル成績・キャッシュ索引）。

## ビルド

Unity 内の **OurTaiko → Build** から各プラットフォーム向けにビルドできます。

| 対象 | 出力先 | アーキテクチャ |
| --- | --- | --- |
| Windows | `Builds/Windows/OurTaikoPlay.exe` | x64 |
| macOS | `Builds/macOS/OurTaikoPlay.app` | Universal (Intel + Apple Silicon) |
| Android | `Builds/Android/OurTaikoPlay.apk` | ARM64 |
| iOS / iPadOS | `Builds/iOS/OurTaikoPlay.xcodeproj` | ARM64 |

*※ iOS ビルドには macOS と Xcode が必要です。*

## 謝辞

- **[Sigaer](https://github.com/Sigaer)** — [DDFont.ttf](https://github.com/OurTaiko/OurTaikoPlay/blob/main/Assets/OurTaiko/Art/DDFont.ttf) フォントの提供。
- **[TeamMajdata / MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)** — 音声設計の着想および [ManagedBass フォーク](https://github.com/TeamMajdata/ManagedBass) のメンテナンス。
- **[YataiDON](https://github.com/Yonokid/YataiDON)** & **[OpenTaiko](https://github.com/0auBSQ/OpenTaiko)** — ゲームプレイや譜面処理の設計参考。
- BASS、[unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)、[Material Symbols](Documentation/ThirdParty/MaterialSymbols/README.md)、ならびにコミュニティのアセット制作者の皆様（詳細は [NOTICE](NOTICE) および [素材一覧](Documentation/ImportedAssets.json) を参照）。

## ライセンス

本プロジェクトのコードは [GNU GPL v3](LICENSE) に基づいて公開されています。外部ライブラリ、フォント、スキン、画像、楽曲、効果音の権利は各著作者に帰属します（詳細は [NOTICE](NOTICE) を参照）。
