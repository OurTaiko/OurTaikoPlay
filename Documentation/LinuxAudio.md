# Linux audio output

Linux plays through **BassSimple** (**Automatic** is the same output). ALSA exclusive
is a BassSimple setting, not another backend: open **Settings → Sound**, choose a card
under **ALSA Exclusive Output**, and close Settings. **Off** is the shared output and the
default. The existing Sample Rate, Output Period and Device Buffer controls apply; their
defaults remain conservative. Reopen Settings to see the current output; the negotiated
ALSA parameters are in the log.

The choice is saved as `audio.linuxAlsaDevice` (empty is off). Every platform keeps the
field in its settings file; only Linux shows or reads it.

## Reservation and recovery

Exclusive output requires ALSA hardware playback, `libdbus-1.so.3`, a session
D-Bus, and a card manager implementing `org.freedesktop.ReserveDevice1` (for
example, WirePlumber with ALSA device reservation enabled). No startup wrapper,
ALSA configuration file, global profile changes or administrator privileges are
needed. Once the native engine starts, missing capabilities, unavailable hardware
and refused reservations are reported rather than treated as exclusive success.

### Linux Player build policy

Build Linux with `OurTaiko.Editor.PlayerBuilds.BuildLinux` and target
`StandaloneLinux64`. It temporarily disables Unity's unused FMOD device output:
otherwise an occupied PCM can block Unity before managed startup, preventing the
BASS engine from reporting or recovering from the failure. BASS playback is not
disabled. AudioClip references/metadata and the original-byte catalog remain
packaged; there is no Unity decoding or DSP-clock dependency in gameplay.

`LinuxPlayerAudioOutput` restores AudioManager byte-for-byte in disposal, including
failed builds. Native-audio staging resources are also cleaned on Linux build
failure. Other platforms and ordinary Editor playback keep their authored Unity
audio setting. The audio preprocess hook rejects an unscoped Linux build with
Unity output still enabled, rather than silently producing a vulnerable Player.
Do not change the authored project setting or add a runtime `-noaudio` wrapper.

**The reservation covers the whole card, including its microphone/input.** Do
not enable it while another application needs that card. A private bus connection
owns the reservation for the lifetime of the hardware output. Switching back,
initialization failure and normal destruction close it after freeing BASS. The
OS closes both the bus and PCM descriptors on process death, including SIGKILL;
there is no helper process or saved disabled sound-card profile to clean up.
WirePlumber returns the card to shared use asynchronously.

Before requesting release, the engine checks kernel PCM owners (including capture).
PipeWire, PulseAudio and JACK servers can yield through the reservation protocol.
Other or unverifiable native owners are reported busy without changing the card's
reservation. This prevents dismantling/recreating a shared card while a raw client
still occupies it. Renamed/custom audio-server processes may therefore need to
release their PCM first; the game prefers safe refusal to disrupting an unknown owner.

Applying a device change uses the normal settings-close transition. If it fails,
the previous device configuration is restored (live volume changes are retained).
If a saved exclusive device is missing at startup, the actual shared/silent
fallback and its reason are shown, and the row shows the saved card as not connected.
The selection is kept, so exclusive output returns once the card is connected again. A shared BASS `dmix` retry is never reported
as exclusive output.

## Devices and diagnostics

Only ALSA `hw` playback devices are listed, not `default`, PipeWire, PulseAudio,
`dmix` or arbitrary ALSA plugins. Saved identity combines the physical sysfs path,
available hardware/serial attributes and PCM number. It does not use the BASS
enumeration index, ALSA card number or display name. Card-number reordering is
safe; changing a USB port or replacing hardware requires selecting it again.
Missing/ambiguous identities never silently select another output.

The audio log line distinguishes requested rate/period/buffer from the ALSA negotiated
format in `/proc/asound` for a substream owned by this process. `period_size`
and `buffer_size` are **frames**, not milliseconds. If the kernel does not expose
this information, it is marked unavailable. BASS's **reported output latency**
is not an end-to-end measurement. Smaller buffers may cause underruns and are
not guaranteed to improve audible latency on every device.

Exclusive output is still BassSimple, so its position clock, Automatic/Heard, Sent
compensation and autoplay sound lead are unchanged. It does not change judgment offsets.

## Focused verification

Use Unity Test Runner with these filters (not `dotnet test`):

- EditMode: `SoundSettingsTests`, `AudioOptionsTests`, `AudioClockTests`,
  `SongClockTests`, `AutoSoundAheadTests` in `OurTaiko.Tests`.
- PlayMode: `LinuxAlsaAudioTests`, `SoundSettingsFlowTests`,
  `AutoSoundAheadFlowTests` in `OurTaiko.Tests`.
- Build: `OurTaiko.Editor.PlayerBuilds.BuildLinux`, target `StandaloneLinux64`,
  with `-customBuildPath` pointing to an isolated, writable output directory.
- Build policy EditMode: `OurTaiko.Tests.LinuxAudioBuildTests`. Actual failed-build
  restoration: execute `OurTaiko.Tests.LinuxAudioBuildTests.VerifyFailedBuild`
  in a separate batch Editor, with `-buildTarget StandaloneLinux64` and an isolated
  `-customBuildPath`. It intentionally fails a late preprocessor and verifies
  that settings and generated audio assets are restored.
- Opt-in asset/native/video Player: execute `OurTaiko.Editor.LinuxAudioPlayerChecks.Build`
  with the same build target and isolated output arguments. This validation-only
  build defines `OURTAIKO_AUDIO_PROBE`; ordinary builds do not include its probe.
  Copy `Assets/OurTaiko/Tests/Fixtures/Movie~/testsrc.mp4` beside that Player. It validates every
  packaged clip's reference, length/sample metadata and original-byte SHA-256,
  then nonzero BASS PCM, menu/effect/voice gains, scheduled song playback and an
  advancing FFmpeg texture. It uses no online servers and exits 0 on success, 2
  on assertion failure. Use an isolated settings file with nonzero master gain.

`LinuxAlsaAudioTests.SelectedHardwareOpensExclusivelyAndReturnsToShared` is
categorized `AudioDevice` and requires explicit opt-in through the test-only
`OURTAIKO_TEST_ALSA_DEVICE` environment variable containing an enumerated stable
ID. Before running it, check that both output and capture streams are idle and
no real game is using the device. The test uses unsaved settings and plays a
zero-gain sample; it also captures the diagnostics and hardware-selection UI in
`TestResults/`. Do not use real player data or an installed game as the test copy.

For native Player checks, prepare settings only beside a newly built isolated
Player. Verify the ReserveDevice1 owner and `/dev/snd` PCM descriptor holder
while exclusive, then check manager ownership and actual shared playback after
switching back, normal exit and killing **only that test process**. Also exercise
an unavailable device, denied/missing reservation manager and a deliberately
busy PCM held by another owned test process. Assertions and compilation alone
do not establish these recovery guarantees or latency performance.

## CI build and release archive

The Build workflow includes `StandaloneLinux64` through game-ci/unity-builder and
`PlayerBuilds.BuildLinux`, with identical first-attempt/retry inputs. game-ci supplies
`-customBuildPath build/StandaloneLinux64/OurTaikoPlay`; this is a bare executable
name, not the local default `Builds/Linux/OurTaikoPlay.x86_64`.

`.github/scripts/package-linux.sh` is the production packaging entry. It archives
the complete clean Player output as `OurTaikoPlay-<display-version>-Linux-x64.tar.gz`,
preserving modes and symlinks and excluding Unity's `*_BurstDebugInformation_DoNotShip`
and `*_BackUpThisFolder_ButDontShipItWithYourGame` directories at any depth. It rejects
unexpected root entries instead of bundling settings, scores, songs, logs or caches;
never package an installed/running game directory. Root payload is the executable,
Data directory, shared libraries and optional UnityCrashHandler64.

`out-StandaloneLinux64` joins the existing `out-*` artifact collection for main
nightly and `v*` tagged GitHub releases. Linux does not enter RhythmHub or TestFlight.
Changing this wiring is not authorization to push, run CI or publish a release.

Offline packaging regression (Python 3 + PyYAML, GNU tar, zip and bash):

```sh
python3 .github/scripts/tests/check-build-packages.py \
  --linux-player /path/to/isolated/ordinary-linux-player \
  --work-dir /path/to/new-empty-validation-directory
```

This **test-only** script takes only build payload into clean staging, runs the
workflow's actual Package block, and checks extracted hashes, modes, symlinks,
native dependencies and exclusions. Other-platform files are routing fixtures,
not native build validation. All git/gh release commands and sudo ownership changes
are strict local recorders: no tag, network, upload or release operation occurs.
Outputs/fixtures are retained in the chosen scratch directory, never the checkout.
Validation archives deliberately contain preservation fixtures; they are not release packages.
The unchanged jobs/steps are compared with `--baseline-ref` (default `HEAD^`, the
reviewed parent when validating this standalone CI change).
Use actionlint separately for workflow schema/expressions; the script also checks
all inline shell syntax with `bash -n`. Local checks do not establish hosted-image,
Unity activation or actual GitHub publication success.
