using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace OurTaiko.Editor.Localization
{
    /// <summary>
    /// Builds the Unity Localization string tables from the CSV files in <see cref="StringsFolder"/>, which are the source
    /// of every translation: one file per table, columns <c>key,en,ja,zh-Hans,comment</c>, <c>\n</c> for a line break.
    /// A CSV change re-imports its table; <c>OurTaiko/Localization/Import String Tables</c> re-imports all of them and
    /// also makes sure the Localization Settings, the three locales and the Addressables build setting are in place.
    /// </summary>
    public static class StringTableImporter
    {
        public const string Root = "Assets/OurTaiko/Localization";
        public const string StringsFolder = Root + "/Strings";
        public const string TablesFolder = Root + "/Tables";
        private const string LocalesFolder = Root + "/Locales";
        private const string SettingsPath = Root + "/LocalizationSettings.asset";

        private static readonly string[] Columns = { "key", "en", "ja", "zh-Hans", "comment" };

        /// <summary>The locales, as GeneralSettings.Languages maps them (L10n.LocaleOf).</summary>
        private static readonly string[] LocaleCodes = { "en", "ja", "zh-Hans" };


        [MenuItem("OurTaiko/Localization/Import String Tables")]
        public static void ImportAllMenu()
        {
            Debug.Log(ImportAll());
        }

        /// <summary>Imports every CSV and returns a report (empty values and errors included).</summary>
        public static string ImportAll()
        {
            EnsureSetup();
            var report = new StringBuilder();
            foreach (string path in Directory.GetFiles(StringsFolder, "*.csv").OrderBy(p => p, StringComparer.Ordinal))
            {
                report.AppendLine(Import(path.Replace('\\', '/')));
            }
            AssetDatabase.SaveAssets();
            return report.ToString().TrimEnd();
        }

        /// <summary>Creates or updates the table named after the CSV file. Keys not in the file are removed.</summary>
        public static string Import(string csvPath)
        {
            string tableName = Path.GetFileNameWithoutExtension(csvPath);
            List<string[]> rows;
            try
            {
                rows = Parse(File.ReadAllText(csvPath, Encoding.UTF8));
            }
            catch (FormatException exception)
            {
                return $"{tableName}: not imported, {exception.Message}";
            }

            if (rows.Count == 0 || !rows[0].Take(Columns.Length).Select(c => c.Trim()).SequenceEqual(Columns))
            {
                return $"{tableName}: not imported, the header must be {string.Join(",", Columns)}";
            }

            EnsureSetup();
            StringTableCollection collection = GetOrCreateCollection(tableName);
            var locales = LocalizationEditorSettings.GetLocales();
            var tables = new Dictionary<string, StringTable>();
            foreach (Locale locale in locales)
            {
                var table = (StringTable)collection.GetTable(locale.Identifier);
                if (table == null)
                {
                    table = (StringTable)collection.AddNewTable(locale.Identifier);
                }
                tables[locale.Identifier.Code] = table;
                LocalizationEditorSettings.SetPreloadTableFlag(table, true);
            }

            var problems = new List<string>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < rows.Count; i++)
            {
                string[] row = rows[i];
                if (row.Length == 0 || row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                string key = row[0].Trim();
                if (key.Length == 0)
                {
                    problems.Add($"line {i + 1}: no key");
                    continue;
                }
                if (!keys.Add(key))
                {
                    problems.Add($"line {i + 1}: duplicate key {key}");
                    continue;
                }

                for (int column = 1; column <= 3; column++)
                {
                    string code = Columns[column];
                    string value = column < row.Length ? Unescape(row[column]) : string.Empty;
                    bool empty = string.IsNullOrWhiteSpace(value);
                    if (empty)
                    {
                        problems.Add($"{key}: no {code}");
                    }
                    if (tables.TryGetValue(code, out StringTable table))
                    {
                        StringTableEntry entry = table.GetEntry(key);
                        if (empty)
                        {
                            // No entry: L10n shows the key and warns.
                            if (entry != null)
                            {
                                table.RemoveEntry(key);
                            }
                        }
                        else if (entry == null)
                        {
                            table.AddEntry(key, value);
                        }
                        else if (entry.Value != value)
                        {
                            entry.Value = value;
                        }
                    }
                }
            }

            foreach (SharedTableData.SharedTableEntry stale in collection.SharedData.Entries.Where(e => !keys.Contains(e.Key)).ToList())
            {
                collection.RemoveEntry(stale.Key);
            }

            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(collection.SharedData);
            foreach (StringTable table in tables.Values)
            {
                EditorUtility.SetDirty(table);
            }
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(null, collection);

            string result = $"{tableName}: {keys.Count} keys";
            return problems.Count == 0 ? result : result + $", {problems.Count} problems:\n  " + string.Join("\n  ", problems);
        }

        /// <summary>The Localization Settings, the en / ja / zh-Hans locales and Addressables building with the player.</summary>
        public static void EnsureSetup()
        {
            Directory.CreateDirectory(LocalesFolder);
            Directory.CreateDirectory(TablesFolder);

            LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (settings == null)
            {
                settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(SettingsPath);
                if (settings == null)
                {
                    settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                    settings.name = "LocalizationSettings";
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }

            foreach (string code in LocaleCodes)
            {
                var identifier = new LocaleIdentifier(code);
                if (LocalizationEditorSettings.GetLocale(identifier) != null)
                {
                    continue;
                }

                string path = $"{LocalesFolder}/{code}.asset";
                Locale locale = AssetDatabase.LoadAssetAtPath<Locale>(path);
                if (locale == null)
                {
                    locale = Locale.CreateLocale(identifier);
                    AssetDatabase.CreateAsset(locale, path);
                }
                LocalizationEditorSettings.AddLocale(locale, false);
            }


            var selectors = settings.GetStartupLocaleSelectors();
            if (selectors.Count != 2 || !(selectors[0] is OurTaiko.SavedLanguageSelector) || !(selectors[1] is SpecificLocaleSelector))
            {
                selectors.Clear();
                selectors.Add(new OurTaiko.SavedLanguageSelector());
                selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("en") });
                EditorUtility.SetDirty(settings);
            }

            AddressableAssetSettings addressables = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (addressables.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            {
                addressables.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
                EditorUtility.SetDirty(addressables);
            }
        }

        private static StringTableCollection GetOrCreateCollection(string tableName)
        {
            StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(tableName);
            if (collection != null)
            {
                return collection;
            }

            string folder = $"{TablesFolder}/{tableName}";
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            return LocalizationEditorSettings.CreateStringTableCollection(tableName, folder);
        }

        private static string Unescape(string value)
        {
            return value.Replace("\\n", "\n");
        }

        /// <summary>RFC 4180: commas separate fields, a quoted field may hold commas, quotes ("") and line breaks.</summary>
        private static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            int line = 1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        if (c == '\n')
                        {
                            line++;
                        }
                        field.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"' when field.Length == 0:
                        quoted = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row.ToArray());
                        row.Clear();
                        line++;
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            if (quoted)
            {
                throw new FormatException($"a quoted field is not closed (line {line})");
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }
            return rows;
        }
    }

    /// <summary>Re-imports a table when its CSV changes.</summary>
    internal sealed class StringTableCsvPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            string[] changed = imported.Where(IsStringsCsv).ToArray();
            if (changed.Length == 0)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                foreach (string path in changed)
                {
                    if (File.Exists(path))
                    {
                        Debug.Log("[L10n] " + StringTableImporter.Import(path));
                    }
                }
                AssetDatabase.SaveAssets();
            };
        }

        private static bool IsStringsCsv(string path)
        {
            return path.StartsWith(StringTableImporter.StringsFolder + "/", StringComparison.Ordinal)
                && path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
        }
    }
}
