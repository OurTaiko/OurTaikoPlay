# MajdataPlay FFmpeg video player (vendored)

Source: https://github.com/TeamMajdata/MajdataPlay, `Assets/Plugins/MajdataPlay/FFmpeg`
at revision `b03db444420d761147b06cb6d7731adde4cd1c61` (plugin last changed in `0471b1b`, 2026-10-26).
GPLv3, as OurTaikoPlay. The native FFmpeg n9.0.1 builds are GPLv3-or-later; each
`Native/<platform>` folder carries their licenses, `configure.txt` and build manifests.

The managed binding is the `net.majdata.ffmpeg-autogen` package (MIT), referenced in
`Packages/manifest.json` as `TeamMajdata/FFmpeg.AutoGen` `?path=Unity` at
`51afb07b99dfb2ffea5b8ff2112f7e3434cb7e74` (the commit MajdataPlay pins).

Only playback is kept. Left out:

- `Runtime/Encoding/`, `FFmpegCameraCapturer.cs`, `Internal/Capture*`, `Internal/VideoEncodeSession.cs`,
  `Internal/MediaCodecEncoderSelector.cs`, `Internal/AudioSampleKernels.cs`,
  `Interop/AndroidMediaCodecEncoderQuery.cs`, `Resources/FFmpegCaptureNv12.shader`,
  `Editor/FFmpegCameraCapturerEditor.cs` (recording).
- Native builds for Linux, Windows x86/arm64 and Android armeabi-v7a (not OurTaikoPlay targets).

Changes:

- `Runtime/MajdataPlay.FFmpeg.asmdef`: no MajdataPlay.IO, MajRenderPipe or Platform.Android
  references; no Linux or Windows x86 platforms.
- `Runtime/AssemblyInfo.cs`: internals visible to `OurTaiko.Tests` only.
- `Runtime/Interop/AndroidContentInterop.cs`: the Android application context through Unity's
  `AndroidJavaClass` instead of MajdataPlay's generated Java bindings.
- `Native/macOS`: the arm64 and x86_64 dylibs merged with `lipo` into universal ones (ad-hoc
  re-signed, `AnyCPU`), since the macOS player is a universal build and Unity leaves per-CPU
  plugins out of it; per-architecture build records keep an `.arm64`/`.x86_64` suffix.
- `../Diagnostics/`: a small `MajdataPlay.Diagnostics` with the `MajDebug` and `UnityProfiler`
  calls the player makes, forwarded to Unity (debug-level messages need `OURTAIKO_FFMPEG_DEBUG`).

To update: copy the plugin again with the same exclusions, keep the changes above, and move the
AutoGen package to the commit MajdataPlay's `ThirdParty/FFmpeg.AutoGen` submodule pins.
