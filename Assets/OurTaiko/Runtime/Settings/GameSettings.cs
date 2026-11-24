using System;
using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    // The global settings saved as settings.json, one section per type of the settings menu.
    // Missing fields keep their defaults, so older files load after new settings are added.
    [Serializable]
    public sealed class GameSettings
    {
        public GeneralSettings general = new GeneralSettings();
        public PlaySettings play = new PlaySettings();
        public DisplaySettings display = new DisplaySettings();
        public AudioOptions audio = new AudioOptions();
        public AccountSettings accounts = new AccountSettings();

        public GameSettings Clone() => FromJson(ToJson());
        public string ToJson() => JsonUtility.ToJson(this, true);

        public static GameSettings FromJson(string json)
        {
            var settings = new GameSettings();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, settings);
            settings.general ??= new GeneralSettings();
            settings.general.songLibraryPaths ??= Array.Empty<string>();
            settings.general.hiddenSettingsPages ??= new List<string>();
            settings.general.keyboard ??= new KeyboardBindings();
            settings.general.keyboard.Normalize();
            settings.play ??= new PlaySettings();
            settings.play.timingDisplay = settings.play.TimingDisplay;
            settings.display ??= new DisplaySettings();
            settings.audio ??= new AudioOptions();
            settings.audio.volume ??= new SoundVolumes();
            settings.audio.backend = settings.audio.Backend;
            settings.audio.clock = settings.audio.Clock;
            settings.accounts ??= new AccountSettings();
            return settings;
        }
    }

    public static class GameSettingsCalibration
    {
        // A result's offset recommendation applied to the whole settings file: save gets a copy of
        // current with only the play section changed (TimingCalibration.TryApply).
        public static bool TryApply(this TimingCalibration calibration, GameSettings current, bool audio, Action<GameSettings> save)
            => calibration.TryApply(current.play, audio, play =>
            {
                var updated = current.Clone();
                updated.play = play;
                save(updated);
            });
    }

    [Serializable]
    public sealed class GeneralSettings
    {
        public static readonly string[] Languages = { "en", "ja", "zh" };
        // Each language's own name, the same in every language.
        public static readonly string[] LanguageNames = { "English", "日本語", "简体中文" };
        // The game's language: the interface (L10n) and the song titles and subtitles. Empty follows the
        // system (SystemLanguage). Saves from before interface localization may hold zh_tw or ko.
        public string language = "";
        // File-only option. Additional folders alongside the always-included default Songs
        // folder. Each custom folder must be an absolute path on the current platform.
        public string[] songLibraryPaths = Array.Empty<string>();
        // Stable page IDs. An empty list (including older saves) shows every category.
        public List<string> hiddenSettingsPages = new List<string>();
        public KeyboardBindings keyboard = new KeyboardBindings();
        public string Language => Array.IndexOf(Languages, language) >= 0 ? language
            : language == "zh_tw" ? "zh"
            : language == "ko" ? "en"
            : SystemLanguage();

        // The language of a player who has not picked one: the system's (Chinese of either script is
        // 简体中文), or English.
        public static string SystemLanguage()
        {
            switch (UnityEngine.Application.systemLanguage)
            {
                case UnityEngine.SystemLanguage.Chinese:
                case UnityEngine.SystemLanguage.ChineseSimplified:
                case UnityEngine.SystemLanguage.ChineseTraditional:
                    return "zh";
                case UnityEngine.SystemLanguage.Japanese:
                    return "ja";
                default:
                    return "en";
            }
        }
        public static readonly LogLevel[] LogLevels = { OurTaiko.LogLevel.Debug, OurTaiko.LogLevel.Info, OurTaiko.LogLevel.Warning, OurTaiko.LogLevel.Error };
        // The least severe messages the log keeps; empty (or unknown) is Log.DefaultLevel.
        public string logLevel = "";
        // Logs every key event, drum press and judgment (InputLog), to find lost hits. Off by default.
        public bool inputLog;
        public LogLevel LogLevel
        {
            get
            {
                LogLevel level = Log.Parse(logLevel, Log.DefaultLevel);
                return Array.IndexOf(LogLevels, level) >= 0 ? level : Log.DefaultLevel;
            }
        }
    }

    // The username of each player's OurTaiko account, filled in on the settings page. Passwords
    // are not kept: the sign-in itself lives in account.dat (OnlineManager.Accounts). The password
    // fields only carry what older versions saved, until it signs in once and is erased.
    [Serializable]
    public sealed class AccountSettings
    {
        public string p1Username = "", p1Password = "";
        public string p2Username = "", p2Password = "";
    }

    [Serializable]
    public sealed class DisplaySettings
    {
        public const int Unlimited = -1;
        public static readonly int[] FrameRates = { 120, 60, Unlimited };
        const int MobileDisplayLimit = 1000;

        // Application.targetFrameRate: 120, 60 or Unlimited (-1).
        public int targetFrameRate = 120;
        // Wait for every display refresh (vSyncCount 1). On desktop the refresh rate then replaces
        // targetFrameRate; mobile platforms ignore vSyncCount and always use targetFrameRate.
        public bool vSync;
        // The 3D Don (YataiDON's Chara3D) above the lane, in song select, Entry and the results.
        public bool chara3D = true;
        // Play the chart's background movie (BGMOVIE) behind the lanes, and download it for online
        // charts. While it plays the stage art is hidden and the lanes' backgrounds are drawn at
        // movieLaneOpacity percent (OpenTaiko's BGAlpha, 100 of 255).
        public bool backgroundMovie = true;
        public const int MovieLaneOpacityDefault = 40, MovieLaneOpacityStep = 5;
        public int movieLaneOpacity = MovieLaneOpacityDefault;
        public float MovieLaneAlpha => Mathf.Clamp(movieLaneOpacity, 0, 100) / 100f;

        // Applies these settings to the running player; render every frame (no frame skipping).
        public void Apply()
        {
            QualitySettings.vSyncCount = !Application.isMobilePlatform && vSync ? 1 : 0;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
            // Android gives a game 60 Hz until it asks for more, and the game then draws at the
            // rate of the display mode it asked for; on iOS Unlimited is the screen's fastest rate.
            // See DisplayRefreshRate.
            Application.targetFrameRate = DisplayRefreshRate.Request(targetFrameRate == Unlimited ? 0 : TargetFrameRate, TargetFrameRate);
        }

        // An unknown value in settings.json falls back to the 120 FPS default. On mobile -1 means
        // the platform default (30 or 60), so when the platform cannot say how fast its display is,
        // Unlimited asks for more than any display refreshes.
        public int TargetFrameRate
        {
            get
            {
                if (Array.IndexOf(FrameRates, targetFrameRate) < 0) return FrameRates[0];
                if (targetFrameRate == Unlimited && Application.isMobilePlatform) return MobileDisplayLimit;
                return targetFrameRate;
            }
        }
    }
}
