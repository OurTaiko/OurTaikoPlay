using System;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace OurTaiko
{
    // The Localization Settings' first startup locale selector: GeneralSettings.Language, which is the
    // language picked in Settings > General, or the system's when none was picked.
    [Serializable]
    public sealed class SavedLanguageSelector : IStartupLocaleSelector
    {
        public Locale GetStartupLocale(ILocalesProvider availableLocales)
        {
            var settings = SettingManager.Instance;
            string language = settings != null ? settings.Settings.general.Language : GeneralSettings.SystemLanguage();
            return availableLocales.GetLocale(L10n.LocaleOf(language));
        }
    }
}
