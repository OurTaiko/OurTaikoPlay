# 四平台构建

Unity **6000.3.25f1**。打开项目后使用 **OurTaiko → Build**，可以单独构建，也可以用 **All Platforms** 顺序构建四个平台。入口为 `Assets/OurTaiko/Editor/PlayerBuilds.cs`，队列在切换平台／脚本重新加载后继续；遇到失败停止。构建使用启用的 Build Settings 场景，并要求 Entry 排在首位，不重建场景。

| 菜单 | 产物 | 架构／脚本后端 |
| --- | --- | --- |
| macOS Universal | `Builds/macOS/OurTaikoPlay.app` | Intel x64 + Apple Silicon ARM64／Mono |
| Windows x64 | `Builds/Windows/OurTaikoPlay.exe` 及同目录依赖 | x64／Mono，可从 macOS 交叉构建 |
| Android ARM64 APK | `Builds/Android/OurTaikoPlay.apk` | ARM64／IL2CPP |
| iOS Xcode Project | `Builds/iOS/OurTaikoPlay.xcodeproj` | 设备 ARM64／IL2CPP |

两个移动平台的 application identifier 均为 **`org.ourtaiko.play`**。移动端仅允许左右横屏，Android 声明联网权限。版本沿用 Player Settings 的 Bundle Version、Android Version Code 和 iOS Build Number。`Configure Platforms` 可单独应用设置。桌面不需要 C++ 构建工具；Android／iOS 的 IL2CPP 是 Unity 构建后端，不引入手写原生玩法代码。

所有平台的产品名称和图标由 `PlayerBranding.Configure()` 统一设置，也会在 Unity 原生 Build Profiles 构建前应用。原始图标直接复制自 OurTaikoPlayer 的 `assets/branding/icon.png`，保存在 `Assets/OurTaiko/Branding/AppIcon.png`；桌面全部尺寸与 iOS App／Spotlight／Settings／Notifications／Marketing 图标共用原图。Android Legacy／Round 使用原图，Adaptive 沿用参考项目的白色背景与 20% 内缩前景，避免启动器遮罩裁掉文字。菜单 **OurTaiko → Build → Configure Name and Icons** 可重新应用。

iOS 导出后将 `.xcodeproj`、主 App Target 和共享 Scheme 改为 **OurTaikoPlay**。UnityFramework、GameAssembly、Unity 自动生成的源文件目录及测试 Target 保留引擎名称；不影响应用名称。再次导出到同一目录时，构建前暂时还原 Unity 需要的工程／Target／Scheme 名，完成后重新应用名称，以支持增量导出。工程 GUID、签名设置和音频框架引用保留；旧导出请重新从 Unity 导出才能更新图标。若目录同时存在新旧两份工程，构建停止并要求换新目录，避免覆盖已有修改。

构建使用非 Development 模式及 LZ4HC。原生音频导入规则由 `AudioBuildSettings` 在构建前检查：macOS BASS/BASSmix/FX/Opus，Windows x64 BASS/BASSmix/FX/Opus/AAC/WASAPI/ASIO，Android ARM64 BASS/BASSmix/FX/Opus/AAC，iOS 四个 xcframework（BASS/BASSmix/FX/Opus）。iOS 构建后补齐运行库搜索路径与 libc++ 链接。不支持 Windows x86。

ManagedBass 使用 TeamMajdata 的 Git 子模块；首次克隆和更新主仓库后执行 `git submodule update --init --recursive`。构建前会把场景 AudioClip 对应的原始编码文件打包为 Resources 字节资产，生成目录 `Assets/OurTaikoNativeAudioBuild` 不提交；构建完成后自动清理。BASS 直接解码这些字节，不经过 Unity PCM 转码。

## 工具链

- Unity Hub 安装同版本的 **Windows Build Support (Mono)**、**Android Build Support**（包含 SDK、NDK、OpenJDK）、**iOS Build Support**。macOS 的 Mono Player 随 Editor 提供。
- Android 在 Preferences → External Tools 使用 Unity 配套 SDK／NDK／JDK。首次构建需要网络下载 Gradle 依赖。
- iOS 导出与 Xcode 编译应在 macOS 上执行，需安装 Xcode 并完成首次启动配置。
- Android 没有配置自定义 keystore 时，Unity 使用开发签名，可用于本地安装；发布版本需另外配置自己的 keystore。
- iOS 已在 Unity Player Settings 开启自动签名，团队为 Hoshino Network LLC（Team ID `253AX6B3P2`）；之后导出的工程会沿用此设置，无需每次手动勾选。Xcode 需登录具有该团队权限的开发者账号，由 Xcode 管理证书和 provisioning。旧导出需重新从 Unity 导出才会应用这些设置。下面的无签名编译只验证编译／链接，不生成可直接安装的 IPA。
- macOS 构建未进行 Developer ID 签名与公证；Windows 未进行 Authenticode 签名。

## 命令行

先关闭该项目的 Editor，避免同一项目被两个 Editor 同时打开。以已安装的 Unity CLI 为例，在项目根目录执行：

```sh
unity run . -- -buildTarget StandaloneOSX -executeMethod OurTaiko.Editor.PlayerBuilds.BuildMacOS -logFile Builds/macOS-build.log
unity run . -- -buildTarget StandaloneWindows64 -executeMethod OurTaiko.Editor.PlayerBuilds.BuildWindows -logFile Builds/Windows-build.log
unity run . -- -buildTarget Android -executeMethod OurTaiko.Editor.PlayerBuilds.BuildAndroid -logFile Builds/Android-build.log
unity run . -- -buildTarget iOS -executeMethod OurTaiko.Editor.PlayerBuilds.BuildIOS -logFile Builds/iOS-build.log
```

也可以直接调用 Unity Editor 可执行文件，加上 `-batchmode -quit -projectPath <项目绝对路径>` 和上面相应的参数。**必须传入匹配的 `-buildTarget`**：在进入构建方法前完成平台切换，保证条件编译与 iOS 后处理正确。每次运行只构建一个平台；四次命令必须顺序执行。

每个平台的构建报告在 `Builds/Reports/<BuildTarget>.json`；Editor 队列当前状态在 `Builds/build-status.json`。失败会抛出异常，使批处理返回失败。`Builds` 与 Android 的 `.utmp` 原生编译缓存已被 Git 忽略。分发 Windows 时应打包整个 `Builds/Windows`，不能只复制 exe。

iOS 无签名设备编译：

```sh
xcodebuild -project Builds/iOS/OurTaikoPlay.xcodeproj \
  -scheme OurTaikoPlay -configuration Release -sdk iphoneos \
  -destination 'generic/platform=iOS' -derivedDataPath Builds/iOS-DerivedData \
  CODE_SIGNING_ALLOWED=NO build
```

产物构建成功不等于完成真机音频验证。Windows 的 WASAPI／ASIO 设备协商、手机音频路由／系统中断及端到端延迟仍需对应设备验证。

Unity 平台切换与批处理规则参考：[Build a player from the command line](https://docs.unity.com/en-us/engine/6000.0/manual/building-and-publishing/build-customize-build-pipeline/build-command-line)。

## 2026-10-03 首次构建验证（音频重做前）

- 四个平台 Unity BuildReport 均为 Succeeded、0 errors。每个报告的 1 条警告来自开发用 Pipeline 插件缺少 RuntimePipelineConfig；没有音频库构建错误。
- macOS：可执行文件确认为 x64 + ARM64，启动新 Player 后日志确认 BASS 后端，未出现启动异常；日志 `TestResults/build-macos-player.log`。
- Windows：exe 和 BASS／BASSmix／WASAPI／ASIO DLL 均确认为 x64；本机未运行 Windows Player。
- Android：APK 约 87 MiB，包名 `org.ourtaiko.play`，min SDK 25、target SDK 36、ARM64；APK v2 签名、16 KB ZIP 对齐通过，包内全部 9 个原生库的 ELF LOAD 段均符合 16 KB 对齐。记录 `TestResults/build-android-artifact.json`。
- iOS：Xcode 27、iPhoneOS 27 SDK，Release 设备 ARM64 无签名编译通过；产物 `Builds/iOS-DerivedData/Build/Products/Release-iphoneos/OurTaikoPlayerUnity.app`，包名 `org.ourtaiko.play`，最低 iOS 15.0，嵌入 UnityFramework／BASS／BASSmix。最终日志 `TestResults/build-ios-xcode-final.log`；Unity 生成代码有 SDK 弃用警告，App Store 图标仍使用项目默认配置。
- 桌面完整压缩包为 `Builds/OurTaikoPlayerUnity-macOS.zip`、`Builds/OurTaikoPlayerUnity-Windows.zip`。Android／iOS 尚未做真机安装、触控和音频延迟验证；iOS 产物未签名，不是可安装 IPA。

## 2026-10-03 音频重做后的构建验证

- ManagedBass 为 TeamMajdata 子模块 `5944aad3842484d78f588d63d5bdc8a85569495b`；四个平台最终 Unity BuildReport 均为 Succeeded、0 errors，每个报告保留 1 条 Pipeline 缺少 RuntimePipelineConfig 的开发插件警告。
- macOS 独立 Player 启动与 Entry 入场交互通过，日志确认 Bass 直接输出，没有资源映射、解码或启动异常；`TestResults/audio-redo-macos-player.log`。没有以日志代替实际延迟测量。
- Windows 产物包含 x64 的 BASS/BASSmix/FX/Opus/AAC/WASAPI/ASIO 七个 DLL；本机未运行 Windows Player 或连接 ASIO 设备。
- Android ARM64 APK 约 102 MiB，包含 BASS/BASSmix/FX/Opus/AAC；包内 12 个原生库 ELF LOAD 均为 16 KB 对齐，16 KB ZIP 对齐与 APK v2 签名校验通过。`TestResults/audio-redo-android-artifact.json`。
- iOS Xcode Release 设备 ARM64 无签名编译、链接通过，最终 app 嵌入 UnityFramework 与 bass/bassmix/bass_fx/bassopus 四个框架；`TestResults/audio-redo-ios-xcode.log`。Unity 生成代码仍有 SDK 弃用和构建脚本输出声明警告。
- 全部 EditMode 189/189、PlayMode 59/59；Android/iOS 真机安装、系统音频路由与端到端延迟尚未验证。编辑器恢复为 macOS 目标。上节桌面压缩包是旧音频版本，当前版本请使用各平台构建目录。

## 项目更名（2026-10-03）

产品名称改为 `OurTaikoPlay`，后续 macOS/Windows/Android 构建输出分别为 `OurTaikoPlay.app`、`OurTaikoPlay.exe`、`OurTaikoPlay.apk`，iOS 显示名称同步采用新名称。移动包名仍为 `org.ourtaiko.play`。本地项目目录仍为 `OurTaikoPlayerUnity`，上面的旧产物路径记录当时的实际构建名称。桌面端存档目录随产品名称变化；沿用旧存档时，将旧名称目录的设置、成绩及缓存复制到新名称目录，保留原文件。移动端包名不变。

## 2026-10-04 名称与图标验证

- 原始图标与 OurTaikoPlayer 源文件 SHA-256 一致；桌面全部尺寸、iOS 19 个槽位和 Android 18 个槽位（含 Adaptive 双层）均有有效贴图；重复执行配置，Player Settings 内容不变。
- 独立目录 `Builds/BrandingVerification/iOS/OurTaikoPlay.xcodeproj` 实际导出并同目录增量导出成功。Xcode 列出的工程、主 Target 和 Scheme 均为 OurTaikoPlay，实际 Products 引用为 OurTaikoPlay.app；名称还原／重设后工程内容稳定，主 Target／UnityFramework GUID 与自定义构建字段保持不变。
- iOS 图标已检查，商店图为无 alpha 的 1024×1024 PNG。Xcode Release ARM64 无签名设备构建 `BUILD SUCCEEDED`，产物 OurTaikoPlay.app，显示名／可执行文件名正确、包名仍为 org.ourtaiko.play，并嵌入原四个 BASS 框架。
- 报告与日志在 `TestResults/branding-*`。Unity 导出报告的唯一 Error 是 Pipeline 调用等待超过 5 秒，后台导出正常完成；警告为现有 Pipeline Runtime 配置提示。Xcode 保留 Unity 自动生成代码的 SDK 弃用及脚本输出声明警告。本次未重新构建 macOS／Windows／Android，也未签名或上传 iOS 应用。
