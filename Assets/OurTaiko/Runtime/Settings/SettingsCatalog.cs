using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    // How a settings row shows and edits its value. Toggle flips in place; Choice (a few short
    // options) and Number/Slider are adjusted in the row; List opens the side sheet; Keys opens a
    // sheet of binding actions; Action runs; Text and Password are typed.
    public enum SettingKind { Toggle, Choice, List, Number, Slider, Keys, Action, Text, Password }

    // One row of the settings overlay. Toggle, Choice, List, Number and Slider rows read and write an
    // integer (a toggle is 0/1, a choice or list is the option's index).
    public sealed class SettingRow
    {
        public string Id { get; }
        public SettingKind Kind { get; }
        public string Label { get; }
        public string Description { get; }
        // The device settings are applied when the overlay closes.
        public bool AppliesOnClose { get; private set; }
        public IReadOnlyList<string> Options { get; private set; } = Array.Empty<string>();
        // One line under each option in the list sheet; empty when the options explain themselves.
        public IReadOnlyList<string> OptionNotes { get; private set; } = Array.Empty<string>();
        public int Minimum { get; private set; } = int.MinValue;
        public int Maximum { get; private set; } = int.MaxValue;
        public int Step { get; private set; } = 1;
        public int Default { get; private set; }
        public InputKey Key { get; private set; }
        public string ActionId { get; private set; }
        public string ActionLabel { get; private set; }
        // An action row's progress ("Working…", who is signed in); set by the overlay.
        public string Status { get; set; } = "";
        // The sound group a change of this row previews (the drum and voice volumes); null for none.
        public AudioGroup? Preview { get; private set; }
        Func<GameSettings, int> get;
        Action<GameSettings, int> set;
        Func<int, string> format;
        Func<GameSettings, string> getText;
        Action<GameSettings, string> setText;

        SettingRow(string id, SettingKind kind, string label, string description)
        {
            Id = id; Kind = kind; Label = label; Description = description;
        }

        public bool IsAdjustable => Kind == SettingKind.Choice || Kind == SettingKind.Number || Kind == SettingKind.Slider;
        public bool HasValue => Kind <= SettingKind.Slider;

        public int Get(GameSettings settings) => get(settings);
        public void Set(GameSettings settings, int value) => set(settings, Clamp(value));
        public int Clamp(int value) => Math.Max(Minimum, Math.Min(Maximum, value));
        // A step of `delta` from value; choices stop at either end like the numbers.
        public int Move(int value, int delta) => Clamp((int)Math.Max(int.MinValue, Math.Min(int.MaxValue, (long)value + (long)delta * Step)));
        public string Format(int value) => Kind switch
        {
            SettingKind.Toggle => value != 0 ? "On" : "Off",
            SettingKind.Choice or SettingKind.List => value >= 0 && value < Options.Count ? Options[value] : "",
            _ => format != null ? format(value) : value.ToString(),
        };
        public string GetText(GameSettings settings) => getText?.Invoke(settings) ?? "";
        public void SetText(GameSettings settings, string text) => setText?.Invoke(settings, text ?? "");

        // The value as the row shows it.
        public string Display(GameSettings settings) => Kind switch
        {
            SettingKind.Keys => settings.general.keyboard.Get(Key).Count == 0 ? "Unbound" : string.Join("  ", settings.general.keyboard.Get(Key)),
            SettingKind.Action => Status,
            SettingKind.Text => GetText(settings).Length == 0 ? "Not set" : GetText(settings),
            SettingKind.Password => "",
            _ => Format(Get(settings)),
        };

        // The footer line for the focused row.
        public string Hint => Kind switch
        {
            SettingKind.Number or SettingKind.Slider => $"Default {Format(Default)}  ·  Step {stepText ?? Step.ToString()}",
            SettingKind.Keys => "Mouse and touch work even when unbound",
            _ => "",
        };
        string stepText;

        public static SettingRow Toggle(string id, string label, string description, Func<GameSettings, bool> get, Action<GameSettings, bool> set)
            => new SettingRow(id, SettingKind.Toggle, label, description)
                { get = s => get(s) ? 1 : 0, set = (s, v) => set(s, v != 0), Minimum = 0, Maximum = 1 };

        public static SettingRow Choice(string id, string label, string description, IReadOnlyList<string> options,
            Func<GameSettings, int> get, Action<GameSettings, int> set, bool list = false, IReadOnlyList<string> notes = null)
            => new SettingRow(id, list ? SettingKind.List : SettingKind.Choice, label, description)
                { Options = options, OptionNotes = notes ?? Array.Empty<string>(), get = s => Math.Max(0, get(s)), set = set, Minimum = 0, Maximum = options.Count - 1 };

        public static SettingRow Number(string id, string label, string description, int defaultValue, int step,
            Func<GameSettings, int> get, Action<GameSettings, int> set, Func<int, string> format,
            int minimum = int.MinValue, int maximum = int.MaxValue, bool slider = false, string stepText = null)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
            return new SettingRow(id, slider ? SettingKind.Slider : SettingKind.Number, label, description)
                { Default = defaultValue, Step = step, get = get, set = set, format = format, Minimum = minimum, Maximum = maximum, stepText = stepText };
        }

        public static SettingRow Keys(InputKey key) => new SettingRow("key-" + key, SettingKind.Keys, KeyboardBindings.Label(key),
            "A key moves here from any other action it was bound to.") { Key = key };

        public static SettingRow Action(string id, string label, string description, string actionLabel)
            => new SettingRow(id, SettingKind.Action, label, description) { ActionId = id, ActionLabel = actionLabel };

        public static SettingRow Text(string id, string label, string description, Func<GameSettings, string> get, Action<GameSettings, string> set)
            => new SettingRow(id, SettingKind.Text, label, description) { getText = get, setText = set };

        // Typed and handed to the action at once, never kept (a password).
        public static SettingRow Password(string id, string label, string description)
            => new SettingRow(id, SettingKind.Password, label, description) { ActionId = id };

        public SettingRow OnClose() { AppliesOnClose = true; return this; }
        public SettingRow Previews(AudioGroup group) { Preview = group; return this; }
    }

    // A picture drawn under a group's rows, showing what they set.
    public enum SettingDiagram { None, JudgeWindow }

    public sealed class SettingGroup
    {
        public string Title { get; }
        public IReadOnlyList<SettingRow> Rows { get; }
        public SettingDiagram Diagram { get; set; }
        public SettingGroup(string title, params SettingRow[] rows) { Title = title; Rows = rows; }
    }

    // A category of the rail. Its groups are rebuilt from the settings, since some rows depend on
    // others (the output backend chooses which device rows exist).
    public sealed class SettingPage
    {
        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        // The page shows the audio engine's output banner (the current device, pending changes).
        public bool ShowsAudioOutput { get; }
        public IReadOnlyList<SettingGroup> Groups { get; private set; } = Array.Empty<SettingGroup>();
        public IReadOnlyList<SettingRow> Rows { get; private set; } = Array.Empty<SettingRow>();
        readonly Func<GameSettings, IReadOnlyList<SettingGroup>> build;

        public SettingPage(string id, string title, string subtitle, Func<GameSettings, IReadOnlyList<SettingGroup>> build, bool audioOutput = false)
        {
            Id = id; Title = title; Subtitle = subtitle; ShowsAudioOutput = audioOutput; this.build = build;
            Refresh(new GameSettings());
        }

        // Rebuilds every row from the settings (a description can depend on another row's value);
        // the status of a row whose id is unchanged survives. Rows are therefore found by Id, never
        // by reference, across a refresh.
        public void Refresh(GameSettings settings)
        {
            var old = Rows.ToDictionary(r => r.Id);
            Groups = build(settings);
            Rows = Groups.SelectMany(g => g.Rows).ToArray();
            foreach (var row in Rows)
                if (old.TryGetValue(row.Id, out var was)) row.Status = was.Status;
        }

        public int GroupOf(int row)
        {
            for (int g = 0, first = 0; g < Groups.Count; first += Groups[g].Rows.Count, g++)
                if (row < first + Groups[g].Rows.Count) return g;
            return -1;
        }
    }

    // The rows of the settings overlay, page by page.
    public static class SettingsCatalog
    {
        public const string RefreshSongList = "refresh-song-list", RefreshLogin = "refresh-login";
        // Followed by the seat: sign-in-0 is 1P's.
        public const string SignIn = "sign-in-", SignOut = "sign-out-";

        static string Signed(int ms) => (ms > 0 ? "+" : "") + ms + " ms";

        public static IReadOnlyList<SettingPage> Pages(SoundPlatform platform) => new[]
        {
            new SettingPage("general", "General", "Song titles and the log", _ => new[]
            {
                new SettingGroup("Language",
                    SettingRow.Choice("language", "Song Title Language",
                        "Song titles and subtitles. Missing translations use the original title. Menus are unchanged.",
                        GeneralSettings.LanguageNames,
                        s => Array.IndexOf(GeneralSettings.Languages, s.general.Language),
                        (s, i) => s.general.language = GeneralSettings.Languages[i], list: true)),
                new SettingGroup("Diagnostics",
                    SettingRow.Choice("log-level", "Log Level",
                        "The least severe messages written to the log (the Logs folder and the console).",
                        GeneralSettings.LogLevels.Select(l => l.ToString()).ToArray(),
                        s => Array.IndexOf(GeneralSettings.LogLevels, s.general.LogLevel),
                        (s, i) => s.general.logLevel = GeneralSettings.LogLevels[i].ToString(), list: true,
                        notes: new[] { "Everything", "Normal activity and problems", "Problems only", "Failures only" })),
            }),
            new SettingPage("controls", "Controls", "Keyboard keys for both drums and the menus", _ => new[]
            {
                new SettingGroup("1P Drum", Seat(0)),
                new SettingGroup("2P Drum", Seat(1)),
                new SettingGroup("Menus", KeyboardBindings.SettingsOrder.Where(k => !k.IsDrum()).Select(SettingRow.Keys).ToArray()),
            }),
            new SettingPage("play", "Play", "Timing, judgment and touch", settings => new[]
            {
                new SettingGroup("Timing", Timing(settings)) { Diagram = SettingDiagram.JudgeWindow },
                new SettingGroup("Input",
                    SettingRow.Number("input-delay", "Input Delay", "Delay drum hits before judgment, sound and effects, like the arcade.",
                        PlaySettings.InputDelayDefaultMs, PlaySettings.OffsetStepMs, s => s.play.inputDelayMs, (s, v) => s.play.inputDelayMs = v,
                        v => v == 0 ? "Off" : v + " ms", 0, PlaySettings.InputDelayMaxMs, stepText: "1 ms")),
                new SettingGroup("Touch",
                    SettingRow.Toggle("drum-pad", "Drumpad in Single Player", "Show the touch drum in single player mode and let touches and clicks hit it.",
                        s => s.play.singlePlayerDrumPad, (s, on) => s.play.singlePlayerDrumPad = on)),
            }),
            new SettingPage("display", "Display", "Frame rate and background movies", _ => new[]
            {
                new SettingGroup("Rendering",
                    SettingRow.Choice("frame-rate", "Frame Rate", "The highest frame rate the game renders at.",
                        new[] { "120", "60", "Unlimited" },
                        s => Array.IndexOf(DisplaySettings.FrameRates, s.display.targetFrameRate),
                        (s, i) => s.display.targetFrameRate = DisplaySettings.FrameRates[i]),
                    SettingRow.Toggle("vsync", "VSync", "Wait for the display refresh before each frame. While on, the refresh rate replaces Frame Rate.",
                        s => s.display.vSync, (s, on) => s.display.vSync = on)),
                new SettingGroup("Background Movie",
                    SettingRow.Toggle("movie", "Background Movie", "Play the chart's movie behind the lanes, and download it with online charts.",
                        s => s.display.backgroundMovie, (s, on) => s.display.backgroundMovie = on),
                    SettingRow.Number("lane-opacity", "Lane Opacity", "How opaque the lanes are while a movie plays. 100%: as without a movie.",
                        DisplaySettings.MovieLaneOpacityDefault, DisplaySettings.MovieLaneOpacityStep,
                        s => s.display.movieLaneOpacity, (s, v) => s.display.movieLaneOpacity = v, v => v + "%", 0, 100, slider: true, stepText: DisplaySettings.MovieLaneOpacityStep + "%")),
            }),
            new SettingPage("sound", "Sound", "Volumes and the output device", s => Sound(platform, s.audio.Backend), audioOutput: true),
            new SettingPage("accounts", "Accounts", "OurTaiko accounts for 1P and 2P", _ => new[]
            {
                new SettingGroup("1P", Account(0)),
                new SettingGroup("2P", Account(1)),
                new SettingGroup("Servers",
                    SettingRow.Action(RefreshSongList, "Refresh Song List", "Load the built-in servers' song lists again.", "Refresh"),
                    SettingRow.Action(RefreshLogin, "Refresh Login", "Check both accounts and sign them in to the built-in servers again.", "Refresh")),
            }),
        };

        // Offset A, Offset B and the judgment windows, which the diagram under them draws. The three
        // window rows exist only while the custom windows are on.
        static SettingRow[] Timing(GameSettings settings)
        {
            var rows = new List<SettingRow>
            {
                SettingRow.Number("audio-offset", "Offset A (Audio)", "Move the chart relative to the music. Negative: earlier; positive: later.",
                    PlaySettings.OffsetDefaultMs, PlaySettings.OffsetStepMs, s => s.play.audioOffsetMs, (s, v) => s.play.audioOffsetMs = v, Signed, stepText: "1 ms"),
                SettingRow.Number("judge-offset", "Offset B (Judgment)", "Move the judgment window only. Negative: earlier; positive: later.",
                    PlaySettings.OffsetDefaultMs, PlaySettings.OffsetStepMs, s => s.play.judgeOffsetMs, (s, v) => s.play.judgeOffsetMs = v, Signed, stepText: "1 ms"),
                SettingRow.Toggle(JudgeCustom, "Custom Judgment Window",
                    "Judge every difficulty and mode with your own GOOD/OK/BAD windows. Off: each difficulty's standard windows. While on, no score is saved or uploaded.",
                    s => s.play.customJudgeWindow, (s, on) => s.play.customJudgeWindow = on),
            };
            if (settings.play.customJudgeWindow)
                for (int window = 0; window < 3; window++)
                {
                    int w = window;
                    string name = JudgeNames[w];
                    rows.Add(SettingRow.Number(JudgeRows[w], name + " Window",
                        "A hit this many ms or less from the note, early or late, is " + name + ". " + JudgeOrder[w],
                        PlaySettings.StandardJudgeMs[w], 1, s => s.play.JudgeMs(w), (s, v) => s.play.SetJudgeMs(w, v),
                        v => v + " ms", PlaySettings.JudgeWindowMinMs, PlaySettings.JudgeWindowMaxMs, stepText: "1 ms"));
                }
            return rows.ToArray();
        }

        public const string JudgeCustom = "judge-custom";
        public static readonly string[] JudgeRows = { "judge-good", "judge-ok", "judge-bad" };
        static readonly string[] JudgeNames = { "GOOD", "OK", "BAD" };
        static readonly string[] JudgeOrder =
        {
            "Raising it past OK raises OK too.",
            "It stays between GOOD and BAD, moving them along.",
            "Beyond it a hit counts for nothing. Lowering it below OK lowers OK too.",
        };

        static SettingRow[] Seat(int seat) => KeyboardBindings.SettingsOrder.Where(k => k.IsDrum() && k.Seat() == seat).Select(SettingRow.Keys).ToArray();

        // The username is saved; the password signs in as soon as it is entered and is not saved.
        static SettingRow[] Account(int seat)
        {
            string player = (seat + 1) + "P";
            return new[]
            {
                SettingRow.Text("username-" + seat, "Username", player + "'s OurTaiko account (sso.ourtaiko.org). Enter the password below to sign in.",
                    s => seat == 0 ? s.accounts.p1Username : s.accounts.p2Username,
                    (s, text) => { if (seat == 0) s.accounts.p1Username = text.Trim(); else s.accounts.p2Username = text.Trim(); }),
                SettingRow.Password(SignIn + seat, "Password",
                    "Signs in as soon as it is entered. The password is not saved; the sign-in lasts while " + player + " plays at least once every 30 days."),
                SettingRow.Action(SignOut + seat, "Sign Out", "Sign " + player + " out of its OurTaiko account on this device.", "Sign Out"),
            };
        }

        static SettingRow Volume(string id, string label, string description, Func<SoundVolumes, float> get, Action<SoundVolumes, float> set)
            => SettingRow.Number(id, label, description + " 0% mutes this group.", 100, 5,
                s => Mathf.Clamp(Mathf.RoundToInt(get(s.audio.volume) * 100 / 5f) * 5, 0, 200), (s, v) => set(s.audio.volume, v / 100f),
                v => v + "%", 0, 200, slider: true, stepText: "5%");

        static SettingRow Device(string id, string label, string description, int defaultValue, int min, int max, int step,
            Func<AudioOptions, int> get, Action<AudioOptions, int> set, Func<int, string> format, string unit = "")
            => SettingRow.Number(id, label, description, defaultValue, step,
                s => Math.Clamp(get(s.audio), min, max), (s, v) => set(s.audio, Math.Clamp(v, min, max)), format, min, max,
                stepText: (step + " " + unit).TrimEnd()).OnClose();

        static Func<int, string> Unit(string unit, string zero = null) => v => v == 0 && zero != null ? zero : (v + " " + unit).TrimEnd();

        // Seconds are stored; they are edited in 0.1 ms steps.
        static SettingRow Milliseconds(string id, string label, string description, int defaultValue, int min, int max,
            Func<AudioOptions, float> get, Action<AudioOptions, float> set)
            => SettingRow.Number(id, label, description, defaultValue, 1,
                s => Math.Clamp(Mathf.RoundToInt(get(s.audio) * 10000), min, max), (s, v) => set(s.audio, Math.Clamp(v, min, max) / 10000f),
                v => v == 0 ? "System Default" : (v / 10m).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms", min, max,
                stepText: "0.1 ms").OnClose();

        static SettingRow DeviceToggle(string id, string label, string description, Func<AudioOptions, bool> get, Action<AudioOptions, bool> set)
            => SettingRow.Toggle(id, label, description, s => get(s.audio), (s, v) => set(s.audio, v)).OnClose();

        public static IReadOnlyList<SettingGroup> Sound(SoundPlatform platform, AudioBackend backend)
        {
            var backends = platform == SoundPlatform.Windows
                // The cross-platform BassSimple output comes last.
                ? new[] { AudioBackend.Automatic, AudioBackend.BassWASAPI, AudioBackend.BassASIO, AudioBackend.BassSimple }
                : new[] { AudioBackend.Automatic, AudioBackend.BassSimple };
            string Note(AudioBackend b) => b switch
            {
                AudioBackend.Automatic => platform == SoundPlatform.Windows ? "BassWASAPI on Windows" : "BassSimple on this device",
                AudioBackend.BassWASAPI => "Windows audio, shared or exclusive",
                AudioBackend.BassASIO => "Requires an installed ASIO driver",
                _ => "Cross-platform output",
            };
            var volumes = new SettingGroup("Volume",
                Volume("master-volume", "Master", "Overall volume.", v => v.master, (v, n) => v.master = n),
                Volume("bgm-volume", "BGM", "Music in menus and the result screen.", v => v.bgm, (v, n) => v.bgm = n),
                Volume("track-volume", "Track", "Song playback and song previews.", v => v.track, (v, n) => v.track = n),
                Volume("drum-volume", "Drum", "Don and ka during gameplay. Changing it plays a don.", v => v.drum, (v, n) => v.drum = n).Previews(AudioGroup.Drum),
                Volume("effects-volume", "Effects", "Menu feedback, balloon pops and result effects.", v => v.effects, (v, n) => v.effects = n),
                Volume("voice-volume", "Voice", "Entry, song selection, combo and result voices.", v => v.voice, (v, n) => v.voice = n).Previews(AudioGroup.Voice));

            var output = new List<SettingRow>
            {
                SettingRow.Choice("backend", "Output Backend", "How the game talks to the audio device. ASIO requires a driver.",
                    Array.ConvertAll(backends, b => b.ToString()),
                    s => Array.IndexOf(backends, s.audio.backend), (s, i) => s.audio.backend = backends[i],
                    list: true, notes: Array.ConvertAll(backends, Note)).OnClose(),
            };
            // A backend this platform does not offer (a Windows settings file opened on a Mac) is shown
            // as Automatic by the row above, and AudioEngine falls back the same way; show its rows.
            if (Array.IndexOf(backends, backend) < 0) backend = AudioBackend.Automatic;
            var selected = backend == AudioBackend.Automatic
                ? (platform == SoundPlatform.Windows ? AudioBackend.BassWASAPI : AudioBackend.BassSimple) : backend;
            if (selected == AudioBackend.BassSimple)
            {
                output.Add(Device("device-period", "Output Period", "Shorter periods reduce delay but may crackle. "
                    + (platform == SoundPlatform.Windows ? "Windows controls the actual period." : "The device may choose a different period."),
                    0, 0, 100, 1, a => a.devicePeriodMs, (a, v) => a.devicePeriodMs = v, Unit("ms", $"Default ({new AudioOptions().Period(platform)} ms)"), "ms"));
                output.Add(Device("device-buffer", "Device Buffer", "0: default; other values are raised to at least twice the output period. "
                    + (platform == SoundPlatform.IOS || platform == SoundPlatform.Desktop ? "macOS and iOS ignore this request." : "The system may choose a different buffer."),
                    0, 0, 1000, 1, a => a.deviceBufferMs, (a, v) => a.deviceBufferMs = v, Unit("ms", "Default"), "ms"));
                if (platform == SoundPlatform.Android)
                    output.Add(DeviceToggle("android-aaudio", "Android AAudio", "Use AAudio output when available.", a => a.androidAAudio, (a, v) => a.androidAAudio = v));
            }
            else if (selected == AudioBackend.BassWASAPI)
            {
                output.Add(DeviceToggle("wasapi-exclusive", "WASAPI Exclusive", "Request exclusive access to the output device. Falls back to shared mode if unavailable.", a => a.wasapiExclusive, (a, v) => a.wasapiExclusive = v));
                output.Add(DeviceToggle("wasapi-raw", "WASAPI Raw Mode", "Request output without Windows audio enhancements. Falls back if unsupported.", a => a.wasapiRaw, (a, v) => a.wasapiRaw = v));
                output.Add(DeviceToggle("wasapi-async", "WASAPI Async Mode", "Prefill audio to reduce dropouts at the cost of extra delay. Exclusive mode only.", a => a.wasapiAsync, (a, v) => a.wasapiAsync = v));
                output.Add(Milliseconds("wasapi-buffer", "WASAPI Buffer", "Buffer per block in exclusive mode (two blocks). Shared mode uses its own buffer.",
                    60, 50, 5000, a => a.wasapiBufferSeconds, (a, v) => a.wasapiBufferSeconds = v));
                output.Add(Milliseconds("wasapi-period", "WASAPI Period", "Shared-mode period; 0: system default. Ignored in exclusive mode.",
                    0, 0, 1000, a => a.wasapiPeriodSeconds, (a, v) => a.wasapiPeriodSeconds = v));
            }
            else if (selected == AudioBackend.BassASIO)
            {
                output.Add(Device("asio-device", "ASIO Device Index", "Driver index, starting at 0. Requires an installed ASIO driver.",
                    0, 0, int.MaxValue, 1, a => a.asioDevice, (a, v) => a.asioDevice = v, v => v.ToString()));
                output.Add(Device("asio-buffer", "ASIO Buffer", "Buffer length in samples; 0: driver default. Driver-supported sizes only.",
                    0, 0, 65536, 1, a => a.asioBufferSamples, (a, v) => a.asioBufferSamples = v, Unit("samples", "Driver Default"), "samples"));
            }
            output.Add(Device("sample-rate", "Sample Rate", selected == AudioBackend.BassWASAPI
                ? "BASS initialization rate. WASAPI output uses the device's negotiated format."
                : "Requested sample rate. The output device may use a different rate.",
                44100, 8000, 192000, 100, a => a.Rate, (a, v) => a.sampleRate = v, Unit("Hz"), "Hz"));
            output.Add(Device("stream-update", "BASS Stream Update Period", "Stream buffering is disabled during playback; this does not control the output-device period.",
                100, 5, 100, 1, a => a.updatePeriodMs, (a, v) => a.updatePeriodMs = v, Unit("ms"), "ms"));
            output.Add(Device("stream-buffer", "BASS Stream Buffer", "At least 1 ms above the stream update period. Playback streams disable this buffer.",
                1000, 6, 5000, 1, a => a.playbackBufferMs, (a, v) => a.playbackBufferMs = v, Unit("ms"), "ms"));
            return new[] { volumes, new SettingGroup("Output", output.ToArray()) };
        }
    }

    // What the Timing group's diagram draws: the GOOD/OK/BAD half widths around the note in real
    // ms, shifted by Offset B, on an axis of ±Range ms. Standard shows 鬼's windows.
    public readonly struct JudgeDiagramState
    {
        public const double MinimumRange = 150, MaximumRange = 1000, RangeStep = 50;
        public readonly double Good, Ok, Bad, Offset, Range;
        public readonly bool Custom;

        JudgeDiagramState(JudgeWindows windows, double offset, bool custom)
        {
            Good = windows.Good * 1000; Ok = windows.Ok * 1000; Bad = windows.Bad * 1000; Offset = offset; Custom = custom;
            double reach = Math.Abs(offset) + Bad;
            Range = Math.Min(MaximumRange, Math.Max(MinimumRange, Math.Ceiling(reach * 1.1 / RangeStep) * RangeStep));
        }

        public static JudgeDiagramState Of(PlaySettings play) => new JudgeDiagramState(
            play.CustomJudgeWindows ?? JudgeWindows.Standard("Oni"), play.judgeOffsetMs, play.customJudgeWindow);

        // The settings as the next play would use them, with the value being adjusted (not kept yet)
        // already applied, so the picture follows the arrows before Don keeps the value.
        public static JudgeDiagramState Previewed(SettingsNavigator nav)
        {
            var p = nav.Settings.play;
            var play = new PlaySettings
            {
                customJudgeWindow = p.customJudgeWindow, judgeOffsetMs = p.judgeOffsetMs,
                judgeGoodMs = p.judgeGoodMs, judgeOkMs = p.judgeOkMs, judgeBadMs = p.judgeBadMs,
            };
            if (nav.Zone == SettingsZone.Adjust && nav.Row != null)
            {
                int window = Array.IndexOf(SettingsCatalog.JudgeRows, nav.Row.Id);
                if (window >= 0) play.SetJudgeMs(window, nav.AdjustValue);
                else if (nav.Row.Id == "judge-offset") play.judgeOffsetMs = nav.AdjustValue;
            }
            return Of(play);
        }

        // 0 at the axis's early end, 1 at its late end.
        public float X(double ms) => (float)Math.Max(0, Math.Min(1, (ms + Range) / (2 * Range)));
    }
}
