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

        public GameSettings Clone() => FromJson(ToJson());
        public string ToJson() => JsonUtility.ToJson(this, true);

        public static GameSettings FromJson(string json)
        {
            var settings = new GameSettings();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, settings);
            settings.play ??= new PlaySettings();
            return settings;
        }
    }

    [Serializable]
    public sealed class PlaySettings
    {
        // The touch drum in SinglePlayScene: enabled and visible, or disabled and hidden.
        public bool singlePlayerDrumPad = true;
    }
}
