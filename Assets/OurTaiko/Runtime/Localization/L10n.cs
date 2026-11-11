using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace OurTaiko
{
    // The interface text, from the Unity Localization string tables built from
    // Assets/OurTaiko/Localization/Strings/<table>.csv. Every table is preloaded, so lookups are
    // synchronous; the first one waits for the package to finish initializing. A key missing from its
    // table comes back as the key itself, with a warning.
    //
    // The language is GeneralSettings.Language (en / ja / zh, which also picks song titles); the
    // locale codes are en / ja / zh-Hans. SettingManager switches the locale when the setting changes.
    public static class L10n
    {
        public static event Action LanguageChanged;

        static bool ready;
        static readonly HashSet<string> warnedMissing = new HashSet<string>();

        // The locale of a GeneralSettings language code.
        public static string LocaleOf(string language) => language == "zh" ? "zh-Hans" : language == "ja" ? "ja" : "en";

        // The selected language as a GeneralSettings code (en / ja / zh).
        public static string Language
        {
            get
            {
                EnsureReady();
                string code = LocalizationSettings.SelectedLocale != null ? LocalizationSettings.SelectedLocale.Identifier.Code : "en";
                return code == "zh-Hans" ? "zh" : code == "ja" ? "ja" : "en";
            }
        }

        public static void EnsureReady()
        {
            if (ready) return;
            ready = true;
            var initialization = LocalizationSettings.InitializationOperation;
            if (!initialization.IsDone) initialization.WaitForCompletion();
            LocalizationSettings.SelectedLocaleChanged += OnSelectedLocaleChanged;
            // The startup selectors only run in Play mode and players; in the Editor outside Play mode (edit-mode
            // tests) there is no locale until one is chosen, so choose the settings' one.
            if (LocalizationSettings.SelectedLocale == null)
                SetLanguage(SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : GeneralSettings.SystemLanguage());
            notified = LocalizationSettings.SelectedLocale;
        }

        // The text of key in table for the selected language.
        public static string Get(string table, string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            EnsureReady();
            StringTableEntry entry = LocalizationSettings.StringDatabase.GetTableEntry(table, key).Entry;
            if (entry == null || entry.Value == null)
            {
                if (warnedMissing.Add(table + "/" + key)) Debug.LogWarning($"[L10n] Missing string {table}/{key} for {Language}");
                return key;
            }
            return entry.Value;
        }

        // Get with string.Format placeholders ({0}, {1}, ...).
        public static string Format(string table, string key, params object[] args)
        {
            string format = Get(table, key);
            if (args == null || args.Length == 0) return format;
            try { return string.Format(format, args); }
            catch (FormatException e)
            {
                Debug.LogWarning($"[L10n] Bad format in {table}/{key} for {Language}: {e.Message}");
                return format;
            }
        }

        public static bool Has(string table, string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            EnsureReady();
            return LocalizationSettings.StringDatabase.GetTableEntry(table, key).Entry != null;
        }

        // Switches to a GeneralSettings language code (en / ja / zh).
        public static void SetLanguage(string language)
        {
            EnsureReady();
            Locale locale = LocalizationSettings.AvailableLocales.GetLocale(LocaleOf(language));
            if (locale == null || locale == LocalizationSettings.SelectedLocale) return;
            LocalizationSettings.SelectedLocale = locale;
            // The package may raise SelectedLocaleChanged only once the new tables are loaded, a frame later;
            // tell listeners now, so that the code that changed the language reads the new text at once.
            Notify();
        }

        static Locale notified;

        static void OnSelectedLocaleChanged(Locale locale) => Notify();

        static void Notify()
        {
            Locale locale = LocalizationSettings.SelectedLocale;
            if (locale == notified) return;
            notified = locale;
            // Wait for the new locale's preloaded tables so that listeners can read strings synchronously.
            var initialization = LocalizationSettings.InitializationOperation;
            if (!initialization.IsDone) initialization.WaitForCompletion();
            var preload = LocalizationSettings.StringDatabase.PreloadOperation;
            if (!preload.IsDone) preload.WaitForCompletion();
            LanguageChanged?.Invoke();
        }
    }
}
