namespace OurTaiko
{
    public static class SongDisplay
    {
        // A song's titles in the player's language (GeneralSettings.Language), English before the
        // settings are loaded.
        public static SongInfo ReadDisplayInfo(this SongDefinition song)
            => song.ReadDisplayInfo(SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "en");
    }
}
