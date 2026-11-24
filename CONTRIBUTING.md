# 贡献指南

感谢你为 OurTaikoPlay 出力。这个仓库是 OurTaiko 的 Unity 游戏客户端（Windows、macOS、Linux、Android、iOS）。**推送到 `main` 即由 CI 构建全部平台并发给玩家**：GitHub 的 `nightly` 预发布、RhythmHub 启动器的 nightly 频道和 TestFlight；推送 `v*` 标签发布正式版（见 [.github/workflows/build.yml](.github/workflows/build.yml)）。所以每一次合并都等于一次面向玩家的发布，下面的规则都是从这一点出发的。

AI 编程助手（AI Agent）除本文外还要遵守 [AGENTS.md](AGENTS.md)，两者冲突时以更严格的一方为准。

## 0. 先理解系统（AI Agent 推荐使用 AOCI）

动手之前先弄清楚改动落在哪一层、谁依赖它。人类贡献者从 [README.md](README.md) 读起，再看 `Assets/OurTaiko/` 下各目录：

| 目录 | 职责 |
|---|---|
| `Runtime/Core` | 谱面、曲库、存档、段位等不依赖画面的核心数据与逻辑；单独的程序集 `OurTaiko.Core`，只被上层引用，自己不引用 Runtime 的其他部分（用到上层的类型会编译失败） |
| `Runtime/Play` | 演奏：音符、判定、魂槽、连打、分歧 |
| `Runtime/Audio` | BASS 音频引擎与各平台输出（WASAPI、ASIO、ALSA 等） |
| `Runtime/Input` | 键盘、太鼓控制器与触屏输入 |
| `Runtime/Online`、`Runtime/Multi` | 与 net.ourtaiko.org 的接口、マルチ演奏（gRPC） |
| `Runtime/Scenes`、`Runtime/Settings`、`Runtime/Chara` | 各画面、设置、角色 |
| `Editor` | 构建（`PlayerBuilds`）与编辑器工具 |
| `Tests` | EditMode／PlayMode 测试；`Finished/` 是开 PR 前必过的回归套件，`PlayMode/` 只放还不能必过的 PlayMode 测试 |

**如果你是 AI Agent，推荐使用 AOCI。** 仓库根目录的 `aoci.txt`（及它引用的分卷）是为模型准备的仓库认知索引，覆盖每个受管文件的职责、关系、对外契约与不显然的约束。它能让你在改代码前就知道一个文件的上下游，避免只看局部就下手。用法见 [AGENTS.md](AGENTS.md) 中的「AOCI Repository Cognition」一节：

- 新会话开始时用 `aoci_rules` + `aoci_overview` 建立完整认知；
- **PR 不修改 AOCI 文件**（`aoci*.txt` 和 `.aoci/` 下受版本控制的文件），也不在 PR 里维护索引（`aoci_maintain`、`aoci_update_entry`、`aoci scope acknowledge` 等）。每次维护都会改 `.aoci/baseline.json` 的同几行，两个都维护过索引的 PR 必然冲突，而且这种冲突无法手工合并；
- 索引因此会落后于代码，AOCI 标为 stale 的条目以源码为准；索引只由单独的 AOCI 维护 PR 补齐（仅在仓库所有者要求时开，从最新的 `main` 建分支，只含 AOCI 文件，提交写作 `chore(aoci): …`，不触发构建）；
- AOCI 的 MCP 由需要的人在自己的机器上配置（推荐用户级的全局配置：命令 `aoci mcp`，不带 `--repo`，从工作目录找到仓库）；仓库级的 `.mcp.json` 绑定本机路径，不提交到仓库。

不使用 AOCI 也可以贡献，但同样要在改动前读懂相关代码。

## 1. 做最佳实践，不打补丁

修改代码时，目标是把问题在**正确的位置、以正确的方式**解决，而不是让症状消失。审批者会按下面几条判断一个改动是「最佳实践」还是「补丁」：

- **先找根因。** 说清楚问题为什么会发生，在产生问题的地方修，而不是在它表现出来的地方绕过去。只修了表象的 PR 会被要求重做。
- **改在负责它的那一层。** 按上面的表格放代码：判定规则不写进画面脚本，平台差异用条件编译收在负责它的类里，构建与编辑器工具只放 `Editor/`。不要把一层的逻辑偷偷塞进另一层。
- **不加特例开关。** 不为某一首歌、某一张谱、某一台设备写 `if` 分支；不用吞掉异常、硬编码默认值、延时帧或重试来掩盖时序或数据问题；不用 `#pragma warning disable` 之类的方式让检查闭嘴，除非确有理由并在旁边写明。
- **不复制，复用或抽取。** 已有的类、组件、预制体能做的事不要再写一份；需要的东西相近但不同，就把共同部分整理出来。
- **契约两边一起改。** 游戏调用的 OurTaikoNet 接口、Multi-Enso 的 `multiplay.proto` 和 `.otanim` 格式由多个仓库共用：改动要和对应的后端或格式仓库一起做，并照顾已经发出去的旧版本游戏。
- **Unity 资源要干净。** 新增或移动资源时连同 `.meta` 一起提交，不手改 GUID；改场景、预制体只动需要的物体，提交前检查 diff 里没有无关的序列化变动；大文件按 `.gitattributes` 走 Git LFS。
- **代码、测试、文档是同一个改动。** 行为变了，就在同一个 PR 里更新或新增测试，以及 README 等文档里描述这一行为的段落。删掉因此不再使用的代码，不留死代码和注释掉的旧实现。
- **跟随仓库现有风格。** 命名、目录结构、错误处理方式和周围代码保持一致。
- **做不到就说出来。** 如果正确的修法超出了这次的范围，在 PR 里写明「这次做了什么、正确的做法是什么、留下了什么」，而不是悄悄提交一个临时绕过。紧急止血的临时修复可以接受，但必须标明是临时的，并附上后续彻底修复的计划。

## 2. 提交与 Pull Request

- 从 `main` 拉出分支开发，通过 PR 合并。**不要直接推送 `main`**，那会立即构建并发给玩家。
- 提交信息沿用仓库的格式：`type(scope): 说明`，例如 `feat(online): …`、`fix(audio): …`。一个提交做一件事。
- 不提交 AOCI 文件（`aoci*.txt`、`.aoci/`），AOCI 维护 PR 除外（见第 0 节）。
- 只改文档或 AOCI 索引的推送不会触发构建，见下面「不触发构建的提交」。
- 不提交密钥、签名证书、keystore、`.mcp.json` 或真实连接参数。
- PR 描述按模板（[.github/pull_request_template.md](.github/pull_request_template.md)）填写：改了什么、为什么、根因是什么、怎么验证的。
- **如果你是 AI Agent，必须在 PR 描述里写明所使用的 AI 模型名称**（尽量写到具体型号或模型 ID，例如 `claude-opus-5-5`；用了多个就都写上），供审批者参考。人类贡献者用 AI 生成了大部分代码时，也请注明。
- 改动涉及发布方式（`.github/workflows/`、`.github/actions/`、`.github/scripts/`、`fastlane/`、`Assets/OurTaiko/Editor/PlayerBuilds.cs`、签名或 `ProjectSettings/`）时，在 PR 里单独说明对各平台构建和发布渠道的影响。

## 3. 开 PR 前完成回归测试

PR 上只有 `Test` 工作流自动跑 EditMode 的 `OurTaiko.FinishedTests`（排除 `AudioDevice` 类别），而且只对本仓库的分支：fork 来的 PR 拿不到 Unity 许可，这项检查会被跳过；只改文档或 AOCI 索引的 PR 也不跑。它只是 PR 页面上的信号，不是合并的必要条件，构建也不等测试；PlayMode 和其他套件仍只能手动触发。因此**本地回归测试仍是合并前的关口**，PlayMode 也要在本地跑，不能拿 PR 上的检查代替。开 PR 之前，按改动范围跑完下面的检查并全部通过；**如果你是 AI Agent，开 PR 之前必须完成必要的回归测试**，不能把测试留给审批者。

| 改动范围 | 必须运行 |
|---|---|
| `Runtime/`、`Editor/` 的 C# 代码 | EditMode 测试，至少 `OurTaiko.FinishedTests` |
| 场景、预制体、画面流程、音频、输入 | 以上之外，PlayMode 测试，至少 `OurTaiko.FinishedPlayModeTests` |
| 留在 `OurTaiko.PlayModeTests` 里的测试覆盖到的部分 | 其中相关的测试 |
| 构建脚本、`Packages/`、`ProjectSettings/`、插件、平台相关代码 | 以上之外，至少在本机构建一个受影响的平台（菜单 **OurTaiko → Build**） |
| 画面上可见的改动 | 以上之外，在 Editor 里实际打开相关画面确认 |

在 Unity Editor 里用 Test Runner 运行即可；命令行（先关闭该项目的 Editor）：

```sh
Unity -batchmode -projectPath . -runTests -testPlatform EditMode \
  -assemblyNames OurTaiko.FinishedTests \
  -testResults TestResults/editmode.xml -logFile TestResults/editmode.log
```

PlayMode 把 `-testPlatform` 换成 `PlayMode`、程序集换成 `OurTaiko.FinishedPlayModeTests`（不要加 `-quit`）。分类 `AudioDevice` 的测试需要真实的音频设备，只能在本机跑。batchmode 结束后执行 `git checkout -- Assets/OurTaiko/Resources/UiFonts`，它会清空动态字体图集。也可以在 GitHub 上手动运行 `Test` 工作流，选择套件和模式；同一 PR 推了新的提交时，PR 上还在跑的那次会被取消，手动运行不受影响。

两个必过套件在本机（Apple Silicon，batchmode）的规模：`OurTaiko.FinishedTests` 约 1000 条、1.5 分钟，`OurTaiko.FinishedPlayModeTests` 约 240 条、20 分钟。

新测试直接写进 `Finished/`。EditMode 没有「未完成」的程序集；PlayMode 的 `OurTaiko.PlayModeTests` 只留下面这些，每个类开头写着原因，原因消除后搬进 `Finished/PlayMode`（连同 `.meta`）：

| 测试类 | 为什么不必过 |
|---|---|
| `DonFlowTests` | `DrawingOnlyTheDonsPartOfItsAreaGivesTheSamePicture` 在 macOS 的 batchmode 下差 2 个像素（允许 1），在 Editor 里通过；`EachLaneOfTwoPlayersHasItsDon` 有一次超过了 180 秒的测试时限 |
| `LinuxAlsaAudioTests` | 只在 Linux 上运行，独占输出那条还要用 `OURTAIKO_TEST_ALSA_DEVICE` 指定空闲的 ALSA 硬件 |
| `MultiLiveServerTests` | 要本地的房间服务器（`OURTAIKO_MULTIPLAY_URL`、`OURTAIKO_MULTIPLAY_TOKEN`），没有时跳过 |

- 修 bug 时先写一个能复现它的测试，确认它在修复前失败、修复后通过。
- 新增行为要有对应的测试；不要为了让测试通过而修改或删除原有测试的断言，除非那条断言描述的行为本身就是这次要改的，并在 PR 里说明。
- 在 PR 描述的「测试」一栏写明实际运行了哪些命令和结果。某项检查因环境限制无法运行时如实写出，不要声称通过。

## 4. 不触发构建的提交

`Build` 工作流在推送里的**所有**文件都属于下面几类时不运行，避免只改文档就重新构建全部平台、覆盖 nightly 并上传 TestFlight：

- 任意位置的 `*.md`（`README*.md`、`AGENTS.md`、`CONTRIBUTING.md`、`PROVENANCE.md` 等）；
- AOCI 索引：`aoci*.txt`、`.aoci/`；
- `.github/pull_request_template.md`、`.github/workflows/test.yml` 及只供它使用的测试镜像 `.github/unity-test/`。

只要推送里有一个其他文件（包括 `.md` 对应的 `.meta`），就照常构建发布。推送 `v*` 标签和手动运行不受影响。需要单个提交跳过构建时，也可以在提交信息里写 `[skip ci]`。

## 5. 许可

提交贡献即表示你同意它以本仓库的许可证（GNU GPL v3，见 [LICENSE](LICENSE) 与 [NOTICE](NOTICE)）发布。
