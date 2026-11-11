using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OurTaiko.Editor.Localization
{
    /// <summary>
    /// Puts <see cref="LocalizedText"/> on fixed labels in prefabs and scenes. A binding names the prefab or scene, the
    /// label's current text (every TextMeshPro text there showing exactly that text is bound) and the table entry.
    /// Bindings come from a CSV with the columns <c>asset,text,table,key</c>; <see cref="BindCsv"/> reports labels it
    /// could not find. A scene is opened additively, saved and closed again (one that was already open stays open).
    /// </summary>
    public static class LocalizedTextBinder
    {
        public readonly struct Binding
        {
            public readonly string Asset;
            public readonly string Text;
            public readonly string Table;
            public readonly string Key;

            public Binding(string asset, string text, string table, string key)
            {
                Asset = asset;
                Text = text;
                Table = table;
                Key = key;
            }
        }

        public static string BindCsv(string csvPath)
        {
            var bindings = new List<Binding>();
            string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                string[] fields = SplitCsvLine(lines[i]);
                if (fields.Length < 4)
                {
                    return $"{csvPath}:{i + 1}: expected asset,text,table,key";
                }
                bindings.Add(new Binding(fields[0].Trim(), fields[1].Replace("\\n", "\n"), fields[2].Trim(), fields[3].Trim()));
            }
            return Bind(bindings);
        }

        public static string Bind(IEnumerable<Binding> bindings)
        {
            var report = new StringBuilder();
            int bound = 0;
            foreach (IGrouping<string, Binding> group in bindings.GroupBy(b => b.Asset))
            {
                bool isScene = group.Key.EndsWith(".unity", StringComparison.OrdinalIgnoreCase);
                if (isScene ? AssetDatabase.LoadAssetAtPath<SceneAsset>(group.Key) == null : AssetDatabase.LoadAssetAtPath<GameObject>(group.Key) == null)
                {
                    report.AppendLine($"missing asset {group.Key}");
                    continue;
                }

                GameObject prefabRoot = null;
                Scene scene = default;
                bool wasOpen = false;
                if (isScene)
                {
                    scene = SceneManager.GetSceneByPath(group.Key);
                    wasOpen = scene.IsValid() && scene.isLoaded;
                    if (!wasOpen)
                    {
                        scene = EditorSceneManager.OpenScene(group.Key, OpenSceneMode.Additive);
                    }
                }
                else
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(group.Key);
                }

                try
                {
                    TMP_Text[] texts = isScene
                        ? scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)).ToArray()
                        : prefabRoot.GetComponentsInChildren<TMP_Text>(true);
                    foreach (Binding binding in group)
                    {
                        if (!HasEntry(binding.Table, binding.Key))
                        {
                            report.AppendLine($"{group.Key}: no entry {binding.Table}/{binding.Key}");
                            continue;
                        }

                        string wanted = Normalize(binding.Text);
                        TMP_Text[] matches = texts.Where(t => Normalize(t.text) == wanted).ToArray();
                        if (matches.Length == 0)
                        {
                            report.AppendLine($"{group.Key}: no label shows \"{binding.Text}\" ({binding.Key})");
                            continue;
                        }

                        foreach (TMP_Text text in matches)
                        {
                            LocalizedText localized = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>();
                            localized.Set(binding.Table, binding.Key);
                            EditorUtility.SetDirty(localized);
                            bound++;
                        }
                    }

                    if (isScene)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }
                    else
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, group.Key);
                    }
                }
                finally
                {
                    if (!isScene)
                    {
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                    }
                    else if (!wasOpen)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
            return $"bound {bound} labels" + (report.Length > 0 ? "\n" + report.ToString().TrimEnd() : string.Empty);
        }

        /// <summary>Checks the table collection itself: in the Editor the runtime database may predate a new table.</summary>
        private static bool HasEntry(string table, string key)
        {
            var collection = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollection(table);
            return collection != null && collection.SharedData.Contains(key);
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r", string.Empty).Trim();
        }

        private static string[] SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"' && field.Length == 0)
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
            fields.Add(field.ToString());
            return fields.ToArray();
        }
    }
}
