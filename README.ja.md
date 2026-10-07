# OurTaikoPlay

**Unity と C# で開発している、ニジイロ風の画面を備えた太鼓リズムゲームです。**

[简体中文](README.md) · [English](README.en.md) · **日本語**

OurTaikoPlay は、Unity と C# でゼロから開発している太鼓リズムゲームです。[OurTaikoPlayer](https://github.com/OurTaiko/OurTaikoPlayer) や [MajdataPlay](https://github.com/TeamMajdata/MajdataPlay) などの設計思想やゲームプレイを参考にしています。現在も開発を進めており、1 人プレイ、キーボード・タッチ操作、ローカル・オンライン楽曲に対応しています。シーンや UI は Unity Editor 上で編集できます。

## 主な機能

- **1 人プレイの一連の流れ**：Entry → サーバーログイン／ゲスト参加 → 選曲 → ロード → 演奏 → リザルト。一時停止、やり直し、選曲への復帰に対応しています。
- **ニジイロ風の演出**：楽曲ボード、難易度カード、ネームプレート、魂ゲージ、踊り子、王冠、「粋／雅／極」を含む 7 種類の ScoreRank アイコンとリザルト演出。
- **TJA 譜面の演奏**：基本的な音符、連打、風船、BPM・スクロール速度の変更、普通／玄人／達人譜面への分岐。
- **オンライン楽曲と成績**：Fanmade・ESE のサーバー設定を同梱。ログイン、ゲスト閲覧、カテゴリフォルダー、譜面・音源のダウンロード、自己ベストの取得、再試行キュー付きの成績送信に対応しています。
- **成績表示**：ローカル成績は SQLite に保存し、オンラインの成績履歴はサーバーから取得します。選曲では自己ベスト、王冠、ScoreRank を表示し、オンラインの王冠には `ClearStatus` を使用します。
- **演奏・システム設定**：オート、はやさ、ドロン、あべこべ、ランダム、音色のほか、音量グループ、音声バックエンド、フレームレート、画面上の太鼓の表示を設定できます。
- **ネイティブ音声処理**：MajdataPlay の設計を参考にした BASS バックエンドを採用し、Unity 音声バックエンドも選択できます。Windows では WASAPI／ASIO にも対応しています。

## ソースから起動する

**Unity 6000.3.25f1**、Unity Hub、Git が必要です。**URP 17.3.0 / Universal 2D、uGUI、TextMeshPro、Input System** を使用しています。

```sh
git clone --recurse-submodules https://github.com/OurTaiko/OurTaikoPlay.git
cd OurTaikoPlay
```

すでにクローン済みの場合は、ManagedBass サブモジュールを初期化・更新してください。

```sh
git submodule update --init --recursive
```

1. Unity Hub でプロジェクトを開き、パッケージの解決とアセットのインポートが完了するまで待ちます。
2. `Assets/Scenes/Entry.unity` を開いて Play を押します。
3. ドンを叩くか画面をタップして参加し、「演奏ゲーム」を選びます。
4. サーバーにログインするか、ゲストで参加します。サーバーをスキップしてローカル楽曲を遊ぶこともできます。
5. 楽曲と難易度を選んで演奏を始めます。

サンプルとして TRIPLE HELIX、Input Calibration、Branch Training を同梱しています。後者 2 つは音源のない調整用・練習用譜面です。Entry の「ゲーム設定」からシステム設定を開けます。

## 操作方法

| 場面 | 操作 | 初期キー |
| --- | --- | --- |
| 演奏 | 左／右のドン | F / J |
| 演奏 | 左／右のカッ | D / K |
| 選曲・メニュー | 移動 | D / K または ← / → |
| 選曲・メニュー | 決定 | F / J または Enter |
| メニュー | 1 つ前に戻る | Esc |
| 演奏 | 一時停止／再開 | Space または Esc |
| 演奏 | やり直し | F1 |

マウス・タッチ操作に対応し、演奏中は画面上の太鼓を使用できます。一時停止メニューは **Resume / Restart / Back to Song Select** の順です。おにと裏おにの両方を持つ楽曲では、おにで右方向へ 10 回連続入力すると切り替わります。

大音符は片側を叩くだけで通常どおり判定されます。1 フレーム内では最初の打撃入力のみを処理する仕様です。オート演奏の成績は保存・送信しません。メニューのタイマーは表示のみで、カウントダウンしません。

## 楽曲と保存データ

オンラインサーバーは `Application.persistentDataPath/servers.json` で設定します。初期設定には `https://fanmade.ourtaiko.org` と `https://ese-backend.llx.life` が含まれます。各項目の `enabled` を `false` にすると無効にできます。

ローカル楽曲は現在 Unity アセットとして設定します。TJA の内容を `.txt` としてインポートし、**Assets → Create → OurTaiko → Song** から `SongDefinition` を作成してください。譜面、音源、オフセットを設定し、SongSelect コントローラーの `songs` リストに追加します。

ユーザーデータは Unity の `Application.persistentDataPath` に保存されます。

| ファイル | 内容 |
| --- | --- |
| `settings.json` | 音量、表示、詳細な音声パラメーターなどのシステム設定 |
| `options.json` | 演奏オプション |
| `player.json` | プレイヤーのネームプレート |
| `servers.json` | オンラインサーバーとログインの設定 |
| `scores.sqlite3` | ローカル自己ベストと、サーバー・アカウント別の送信待ちキュー |

General → Language は現在、曲名とサブタイトルにのみ適用されます。翻訳がない場合は日本語を優先して表示します。メニュー全体の翻訳にはまだ対応していません。README の言語別ページはゲーム内の言語設定とは別です。

## ビルド

Unity の **OurTaiko → Build** を使用します。先に Unity Hub で対象プラットフォームのビルドモジュールをインストールしてください。

| 対象 | 出力先 | アーキテクチャ |
| --- | --- | --- |
| macOS | `Builds/macOS/OurTaikoPlay.app` | Intel + Apple Silicon |
| Windows | `Builds/Windows/OurTaikoPlay.exe` と同じフォルダーの依存ファイル | x64 |
| Android | `Builds/Android/OurTaikoPlay.apk` | ARM64 |
| iOS / iPadOS | `Builds/iOS/OurTaikoPlay.xcodeproj` | ARM64 実機 |

iOS のビルドには macOS と Xcode が必要です。自動署名は Hoshino Network LLC チームに設定済みです。Xcode には同チームの権限を持つ開発者アカウントでログインしてください。別のチームを使う場合は Unity Player Settings で変更します。モバイル版のパッケージ名は `org.ourtaiko.play` で、横画面専用です。

## 開発状況とドキュメント

現在は 1 人プレイとニジイロスキンを中心に開発しています。2 人プレイ、段位モード、検索・並べ替え、完全なリプレイ再生は未実装で、すべての TJA 拡張への対応も保証していません。実行時のロジックと Editor ツールは C# で実装し、ネイティブ音声ライブラリをプラットフォーム依存のライブラリとして使用します。インポートしたアニメーション表はデータとして読み込み、Lua は実行しません。

- [素材一覧](Documentation/ImportedAssets.json)：インポートした画像・音声の出典。
- テストは `Assets/OurTaiko/Tests/` にあります。Unity Test Runner で EditMode・PlayMode テストを実行できます。`Finished/` には完成済み機能の回帰テストを保存しています。

[Issues](https://github.com/OurTaiko/OurTaikoPlay/issues) での不具合報告や Pull Request を歓迎します。演奏・音声・タッチ操作に関する報告には、プラットフォーム、端末、再現手順、関連ログを添えてください。

## 謝辞

1. **[Sigaer](https://github.com/Sigaer)** — 本プロジェクトで使用する [DDFont.ttf](https://github.com/OurTaiko/OurTaikoPlay/blob/main/Assets/OurTaiko/Art/DDFont.ttf) フォントを提供してくれてありがとうございます！そして、私がゲーム開発に集中できるよう、代わりに Project Sekai のライブボーナスを消化してくれていることにも、特別な感謝を。
2. **[TeamMajdata / MajdataPlay](https://github.com/TeamMajdata/MajdataPlay)** — シーン遷移・音声処理の設計から得た着想と、[ManagedBass フォーク](https://github.com/TeamMajdata/ManagedBass)のメンテナンスに感謝します。
3. **サードパーティーのライブラリ・素材の作者の皆さん** — BASS、[unity-sqlite-net](https://github.com/gilzoide/unity-sqlite-net)、[Material Symbols](Documentation/ThirdParty/MaterialSymbols/README.md)、およびスキン、画像、楽曲、効果音、譜面の制作に感謝します。帰属情報は [NOTICE](NOTICE) と[素材一覧](Documentation/ImportedAssets.json)に記載しています。

## ライセンスと素材の帰属

プロジェクトのコードには [GNU GPL v3](LICENSE) を適用しています。出典とサードパーティーに関する声明は [NOTICE](NOTICE) を参照してください。外部ライブラリ、フォント、スキン、画像、楽曲、効果音、譜面の権利とライセンスは、それぞれの権利者に帰属します。コードのライセンスが、これらの素材のライセンスに代わるものではありません。
