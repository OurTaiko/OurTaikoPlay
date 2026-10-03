using System;
using UnityEngine;

namespace OurTaiko
{
    // The global settings saved as settings.json, one section per type of the settings menu.
    // Missing fields keep their defaults, so older files load after new settings are added.
    [Serializable]
    public sealed class GameSettings
    {
        public PlaySettings play = new PlaySettings();
        public DisplaySettings display = new DisplaySettings();

        public GameSettings Clone() => FromJson(ToJson());
        public string ToJson() => JsonUtility.ToJson(this, true);

        public static GameSettings FromJson(string json)
        {
            var settings = new GameSettings();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, settings);
            settings.play ??= new PlaySettings();
            settings.display ??= new DisplaySettings();
            return settings;
        }
    }

    [Serializable]
    public sealed class PlaySettings
    {
        // The touch drum in SinglePlayScene: enabled and visible, or disabled and hidden.
        public bool singlePlayerDrumPad = true;
    }

    [Serializable]
    public sealed class DisplaySettings
    {
        public const int Unlimited = -1;
        public static readonly int[] FrameRates = { 120, 60, Unlimited };

        // Application.targetFrameRate: 120, 60 or Unlimited (-1).
        public int targetFrameRate = 120;

        // Applies these settings to the running player; render every frame (no frame skipping).
        public void Apply()
        {
            QualitySettings.vSyncCount = 0;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
            Application.targetFrameRate = TargetFrameRate;
        }

        // An unknown value in settings.json falls back to the 120 FPS default. On mobile -1 means
        // the platform default (30 or 60), so Unlimited asks for more than any display refreshes.
        public int TargetFrameRate
        {
            get
            {
                if (Array.IndexOf(FrameRates, targetFrameRate) < 0) return FrameRates[0];
                if (targetFrameRate == Unlimited && Application.isMobilePlatform) return 1000;
                return targetFrameRate;
            }
        }
    }
}
