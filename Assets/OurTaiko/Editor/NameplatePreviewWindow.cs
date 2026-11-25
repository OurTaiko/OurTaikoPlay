using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Menu OurTaiko/Nameplate Preview: a nameplate wearing the layers picked here (the player's
    // Nameplate/<slot>/<name>.otanim files), running, over the plate states a player can be in,
    // with what each worn file holds. "Wear as 1P" writes the picks into player.json.
    public sealed class NameplatePreviewWindow : EditorWindow
    {
        const float SideWidth = 340;
        const string None = "(none)";

        [SerializeField] string playerName = "OurTaiko", title = "Nameplate layers";
        [SerializeField] int dan = 10, combo = 1, side;
        [SerializeField] bool gold;
        [SerializeField] string bandItem = "", effectItem = "", iconItem = "";
        [SerializeField] string plateState = "auto", plateTitle = "auto", plateAiTitle = "wood", plateBlank = "off", plateBadge = "auto";
        [SerializeField] int scale = 2;
        [SerializeField] float speed = 1;
        [SerializeField] bool paused;
        [SerializeField] Color background = new Color(0.22f, 0.22f, 0.25f, 1);
        [SerializeField] Vector2 scroll;

        [SerializeField] string profileBand = "wood";
        NameplatePreview preview;
        string failure, contents = "";
        double lastUpdate;
        Texture shown;
        FileSystemWatcher watcher;
        volatile bool filesChanged;

        [MenuItem("OurTaiko/Nameplate Preview")]
        static void Open() => GetWindow<NameplatePreviewWindow>("Nameplate").minSize = new Vector2(880, 360);

        void OnEnable()
        {
            lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Advance;
            Watch();
            Rebuild();
        }

        void OnDisable()
        {
            EditorApplication.update -= Advance;
            watcher?.Dispose();
            watcher = null;
            preview?.Dispose();
            preview = null;
            shown = null;
        }

        // The items folder is watched, so a file saved again is drawn again at once.
        void Watch()
        {
            watcher?.Dispose();
            watcher = null;
            try
            {
                Directory.CreateDirectory(NameplateItems.Folder);
                watcher = new FileSystemWatcher(NameplateItems.Folder, "*" + NameplateItems.Extension) { IncludeSubdirectories = true };
                FileSystemEventHandler changed = (_, _) => filesChanged = true;
                watcher.Changed += changed;
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Renamed += (_, _) => filesChanged = true;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            {
                Debug.LogWarning("Nameplate preview: cannot watch " + NameplateItems.Folder + ": " + e.Message);
            }
        }

        void Rebuild()
        {
            preview?.Dispose();
            preview = null;
            shown = null;
            failure = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode) { failure = "Not while the game is playing."; return; }
            try
            {
                preview = new NameplatePreview();
                Apply();
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
        }

        PlayerInfo Info() => new PlayerInfo
        {
            name = playerName,
            title = title,
            dan = dan,
            gold = gold,
            danCombo = combo,
            bandItem = bandItem,
            effectItem = effectItem,
            iconItem = iconItem,
            profileBand = profileBand,
            plateState = plateState, plateTitle = plateTitle, plateAiTitle = plateAiTitle, plateBlank = plateBlank, plateBadge = plateBadge,
        };

        void Apply()
        {
            preview.Background = background;
            preview.Show(Info(), side);
            contents = Describe(preview.Info);
            Draw();
        }

        void Advance()
        {
            double now = EditorApplication.timeSinceStartup;
            double step = Math.Min(0.1, now - lastUpdate);
            lastUpdate = now;
            if (preview == null)
            {
                if (failure != null && !EditorApplication.isPlayingOrWillChangePlaymode && failure.StartsWith("Not while", StringComparison.Ordinal)) Rebuild();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Rebuild(); Repaint(); return; }
            if (filesChanged)
            {
                filesChanged = false;
                preview.Refresh();
                contents = Describe(preview.Info);
            }
            if (paused) return;
            preview.Step(step * speed);
            Draw();
            Repaint();
        }

        void Draw() => shown = preview.Render(scale);

        void OnGUI()
        {
            if (preview == null)
            {
                EditorGUILayout.HelpBox(failure ?? "No preview.", MessageType.Warning);
                if (GUILayout.Button("Retry", GUILayout.Width(120))) Rebuild();
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Width(SideWidth)))
                {
                    scroll = view.scrollPosition;
                    Controls();
                }
                var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                if (shown != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(area, shown, ScaleMode.ScaleToFit, true);
            }
        }

        void Controls()
        {
            EditorGUIUtility.labelWidth = 100;
            var catalog = PlateComposition.ReadCatalog();
            bool layersChanged = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Items folder", NameplateItems.Folder)))
                {
                    foreach (var slot in NameplateSlots.All) Directory.CreateDirectory(Path.Combine(NameplateItems.Folder, NameplateSlots.Folder(slot)));
                    EditorUtility.RevealInFinder(Path.Combine(NameplateItems.Folder, NameplateSlots.Folder(NameplateSlot.Band)));
                }
                if (GUILayout.Button("Read again")) { NameplateItems.Forget(); layersChanged = true; }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Plate", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            if (catalog != null)
            {
                plateState = Select("Layout", plateState, new[] { "auto", "custom" }.Concat(catalog.States.Properties().Select(p => p.Name)));
            }
            playerName = EditorGUILayout.TextField("Name", playerName);
            title = EditorGUILayout.TextField(new GUIContent("Title", "Empty, with no 段位 either, is the coin plate."), title);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("PlayerBadge", EditorStyles.boldLabel);
            side = EditorGUILayout.Popup("Player side", side, new[] { "1P", "2P" });

            var rows = catalog?.States[PlateComposition.StateFor(Info(), side) ?? ""];
            bool custom = plateState == "custom";
            bool Has(string role) => rows != null && rows.Any(row => (string)row["role"] == role);
            if (Has("plate_player_no"))
                plateBadge = Select("Badge", plateBadge, new[] { "auto", "1p", "2p" });

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            if (custom)
            {
                bandItem = Slot("Band", NameplateSlot.Band, bandItem);
                effectItem = Slot("Effect", NameplateSlot.Effect, effectItem);
                iconItem = Slot("Icon", NameplateSlot.Icon, iconItem);
            }
            else
            {
                if (Has("title_instance"))
                    plateTitle = Select("Title band", plateTitle, new[] { "auto" }.Concat(catalog.Titles.Properties().Select(p => p.Name)));
                if (Has("ai_title_instance"))
                    plateAiTitle = Select("AI title band", plateAiTitle, catalog.AiTitles.Properties().Select(p => p.Name));
                if (Has("blank_instance"))
                    plateBlank = Select("Blank", plateBlank, catalog.Blank);
                if ((Has("text_name") || Has("text_title")) && GUILayout.Button("Load source text"))
                {
                    foreach (var row in rows)
                    {
                        if ((string)row["role"] == "text_name") playerName = (string)row["sample"];
                        if ((string)row["role"] == "text_title") title = (string)row["sample"];
                    }
                    layersChanged = true;
                }
            }
            if (custom || plateState == "auto" || Has("dani") || Has("rankup_instance"))
            {
                var dans = new[] { "none" }.Concat(Enumerable.Range(PlayerInfo.DanFirst, PlayerInfo.DanCount - PlayerInfo.DanFirst).Select(i => i.ToString())).ToArray();
                int danAt = dan >= PlayerInfo.DanFirst && dan < PlayerInfo.DanCount ? dan - PlayerInfo.DanFirst + 1 : 0;
                danAt = EditorGUILayout.Popup(new GUIContent("段位", "PlayerInfo.dan: 6 五級 … 24 達人."), danAt, dans);
                dan = danAt == 0 ? -1 : danAt - 1 + PlayerInfo.DanFirst;
                using (new EditorGUI.DisabledScope(dan < 0))
                {
                    combo = EditorGUILayout.Popup("Ring", combo, new[] { "銀 (silver)", "金 (full combo)", "虹 (all 良)" });
                    gold = EditorGUILayout.Toggle("Gold pass", gold);
                }
            }
            if (EditorGUI.EndChangeCheck() || layersChanged) Apply();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            foreach (var slot in NameplateSlots.All)
            {
                var layers = preview.Plate.Layers;
                var view = slot switch { NameplateSlot.Band => layers?.BandView, NameplateSlot.Effect => layers?.EffectView, _ => layers?.IconView };
                var clips = view?.File?.Clips;
                if (clips == null || clips.Length < 2) continue;
                var names = Array.ConvertAll(clips, c => c.Name);
                int current = Array.IndexOf(names, view.clip);
                int pick = EditorGUILayout.Popup(slot + " clip", current, names);
                if (pick != current)
                {
                    preview.Seek(0);
                    layers.Play(slot, names[pick], 0, foreground: slot == NameplateSlot.Icon && names[pick] != NameplateItems.ClipName);
                    Draw();
                }
            }
            if (Has("rankup_instance"))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    foreach (string phase in new[] { "init", "start", "wait", "end" })
                        if (GUILayout.Button(phase))
                        {
                            string rank = PlateComposition.RankLabel(dan) ?? "5kyuu";
                            preview.Seek(0);
                            preview.Plate.PlayLayerClipAt(NameplateSlot.Icon, phase == "init" ? "idle" : rank + "/rankup_" + phase, 0);
                            Draw();
                        }
                }
            }
            speed = EditorGUILayout.Slider("Speed", speed, 0, 3);
            using (new EditorGUILayout.HorizontalScope())
            {
                paused = GUILayout.Toggle(paused, "Pause", "Button");
                if (GUILayout.Button("Restart")) { preview.Seek(0); Draw(); }
                if (GUILayout.Button("-1 frame")) { preview.Seek(preview.Seconds - 1 / 60.0); Draw(); }
                if (GUILayout.Button("+1 frame")) { preview.Seek(preview.Seconds + 1 / 60.0); Draw(); }
            }
            EditorGUILayout.LabelField("Time", preview.Seconds.ToString("0.00") + " s");
            EditorGUI.BeginChangeCheck();
            scale = EditorGUILayout.IntSlider("Zoom", scale, 1, 4);
            background = EditorGUILayout.ColorField(new GUIContent("Background", "Alpha 0 saves see-through pictures."), background);
            if (EditorGUI.EndChangeCheck()) { preview.Background = background; Draw(); }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Wear as 1P", "Saves the selected layout, appearance and layers for player 1."))) WearAs1P();
                if (GUILayout.Button(new GUIContent("Load 1P", "Takes the plate player.json holds."))) Load1P();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save picture…")) Save();
                if (GUILayout.Button(new GUIContent("Render all…", "Items over their clips and up to 64 layer combinations, in image sheets."))) RenderAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Contents", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(contents, EditorStyles.wordWrappedMiniLabel,
                GUILayout.Height(EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(contents), SideWidth - 30)));
        }

        static string Slot(string label, NameplateSlot slot, string id)
        {
            var names = NameplateItems.Names(slot);
            var choices = new List<string> { None };
            choices.AddRange(names);
            NameplateSlots.TryParse(id, out var named, out string name);
            int at = name != null && named == slot ? choices.IndexOf(name) : 0;
            if (at < 0)
            {
                // Worn but gone from the folder: kept, and said.
                choices.Add(name + " (missing)");
                at = choices.Count - 1;
            }
            if (names.Count == 0 && at == 0)
            {
                EditorGUILayout.LabelField(label, "No " + label.ToLowerInvariant() + " items installed");
                return id;
            }
            int picked = EditorGUILayout.Popup(label, at, choices.ToArray());
            if (picked == at) return id;
            return picked == 0 || picked > names.Count ? "" : NameplateSlots.Id(slot, choices[picked]);
        }

        static string Select(string label, string value, IEnumerable<string> choices)
        {
            var names = choices.ToArray();
            int current = Math.Max(0, Array.IndexOf(names, value));
            return names[EditorGUILayout.Popup(label, current, names)];
        }

        static string PlayerFile => Path.Combine(GamePaths.Root, PlayerInfoController.FileName);

        static PlayerInfo Read1P()
        {
            if (EditorApplication.isPlaying) return PlayerInfoController.EnsureInstance().Info.Clone();
            return File.Exists(PlayerFile) ? PlayerInfo.ReadSaved(File.ReadAllText(PlayerFile), out _) : new PlayerInfo();
        }

        void WearAs1P()
        {
            var info = Read1P();
            info.bandItem = bandItem;
            info.effectItem = effectItem;
            info.iconItem = iconItem;
            info.plateState = plateState;
            info.plateTitle = plateTitle;
            info.plateAiTitle = plateAiTitle;
            info.plateBlank = plateBlank;
            info.plateBadge = plateBadge;
            if (EditorApplication.isPlaying) PlayerInfoController.EnsureInstance().Set(info);
            else File.WriteAllText(PlayerFile, info.ToJson());
            ShowNotification(new GUIContent("1P now wears these layers"));
        }

        void Load1P()
        {
            var info = Read1P();
            playerName = info.name;
            title = info.title == PlayerInfo.DefaultTitle ? "" : info.title;
            dan = info.HasDan ? info.dan : -1;
            gold = info.gold;
            combo = info.DanComboTier;
            bandItem = info.bandItem;
            effectItem = info.effectItem;
            iconItem = info.iconItem;
            profileBand = info.profileBand;
            plateState = info.plateState;
            plateTitle = info.plateTitle;
            plateAiTitle = info.plateAiTitle;
            plateBlank = info.plateBlank;
            plateBadge = info.plateBadge;
            Apply();
        }

        void Save()
        {
            string path = EditorUtility.SaveFilePanel("Nameplate picture", "", "nameplate.png", "png");
            if (string.IsNullOrEmpty(path)) return;
            var picture = preview.Capture(scale);
            File.WriteAllBytes(path, picture.EncodeToPNG());
            DestroyImmediate(picture);
        }

        void RenderAll()
        {
            string dir = EditorUtility.SaveFolderPanel("Nameplate layers", "", "");
            if (!string.IsNullOrEmpty(dir))
            {
                // RenderAll opens a preview of its own; this one's plate is shown again after.
                NameplatePreview.RenderAll(dir, scale);
                Apply();
            }
            GUIUtility.ExitGUI();
        }

        // What each worn file holds: its picture, parts and the clip it plays.
        public static string Describe(PlayerInfo info)
        {
            var text = new StringBuilder();
            text.AppendLine("Items folder: " + NameplateItems.Folder);
            if (info.plateState != "custom")
            {
                text.AppendLine("Layout: " + info.plateState);
                text.AppendLine("Title: " + info.plateTitle);
                text.AppendLine("AI title: " + info.plateAiTitle);
                text.AppendLine("Blank: " + info.plateBlank);
                return text.ToString();
            }
            foreach (var slot in NameplateSlots.All)
            {
                string id = info.ItemIn(slot);
                string label = NameplateSlots.Folder(slot);
                if (string.IsNullOrEmpty(id)) { text.AppendLine(label + ": none"); continue; }
                var item = NameplateItems.Load(slot, id);
                if (item == null) { text.AppendLine($"{label}: {id} cannot be worn (missing, unreadable or not drawn on the 408×96 plate; see the console)"); continue; }
                var file = item.File;
                var clip = NameplateItems.ClipOf(file);
                string length = clip == null ? "-" : clip.Loop ? (clip.Frames.Length / clip.Fps).ToString("0.##") + " s, loops" : clip.Duration.ToString("0.##") + " s, once";
                text.AppendLine($"{label}: {id}");
                text.AppendLine($"  picture {item.texture.width}×{item.texture.height}, {file.Parts.Length} parts, clip {clip?.Name} ({clip?.Frames.Length} frames, {length})");
                if (clip != null && clip.Name != NameplateItems.ClipName) text.AppendLine($"  no \"{NameplateItems.ClipName}\" clip: its first is played");
            }
            if (info.IsCoin) text.AppendLine("No title and no 段位: the coin plate wears no layers.");
            return text.ToString().TrimEnd();
        }
    }
}
