using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Menu OurTaiko/Nameplate Preview: a nameplate running over the plate states a player can be
    // in, wearing a title of the set (its band, effect and icon) or the layers picked here (the
    // set's parts and the player's Nameplate/<slot>/<name>.otanim files), with what each worn layer is,
    // which titles it comes from, and switches to look at each layer on its own. "Wear as 1P"
    // writes the picks into player.json.
    public sealed class NameplatePreviewWindow : EditorWindow
    {
        const float SideWidth = 340;
        const string None = "(none)";

        [SerializeField] string playerName = "OurTaiko", title = "Nameplate layers";
        [SerializeField] int dan = 10, combo = 1, side;
        [SerializeField] bool gold;
        [SerializeField] string bandItem = "", effectItem = "", iconItem = "";
        [SerializeField] string plateState = "auto", plateTitle = "auto", plateAiTitle = "wood", plateBlank = "off", plateBadge = "auto", takeTitle = "wood";
        [SerializeField] int scale = 2;
        [SerializeField] float speed = 1;
        [SerializeField] bool paused;
        // Left out of the picture only (NameplatePreview.Hidden, LayersOnly).
        [SerializeField] bool hideBand, hideEffect, hideIcon, layersOnly;
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
            menus.Clear();
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
            contents = Describe(preview.Info, preview.Side);
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
                menus.Clear();
                preview.Refresh();
                contents = Describe(preview.Info, preview.Side);
            }
            if (paused) return;
            preview.Step(step * speed);
            Draw();
            Repaint();
        }

        void Draw()
        {
            preview.Hidden.Clear();
            if (hideBand) preview.Hidden.Add(NameplateSlot.Band);
            if (hideEffect) preview.Hidden.Add(NameplateSlot.Effect);
            if (hideIcon) preview.Hidden.Add(NameplateSlot.Icon);
            preview.LayersOnly = layersOnly;
            shown = preview.Render(scale);
        }

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
                if (GUILayout.Button(new GUIContent("Read again", "The items folder and the baked set (" + NameplateSet.BakedFolder + ")")))
                {
                    NameplateItems.Forget();
                    NameplatePreview.UseBakedSet(again: true);
                    menus.Clear();
                    layersChanged = true;
                }
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
                foreach (var slot in NameplateSlots.All)
                {
                    string current = Info().ItemIn(slot);
                    string worn = ItemPicker(catalog, slot, current);
                    if (worn != current) { SetItem(slot, worn); layersChanged = true; }
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    takeTitle = TitlePicker(new GUIContent("From title", "A title to fill the three slots with."), takeTitle, catalog.Titles.Keys, null);
                    if (GUILayout.Button(new GUIContent("Wear", "Fills the three slots with the items this title is made of."), GUILayout.Width(50)))
                    {
                        WearParts(catalog.Title(takeTitle));
                        layersChanged = true;
                    }
                }
            }
            else
            {
                if (Has("title_instance"))
                    plateTitle = TitlePicker(new GUIContent("Title", "auto: the title the player was given (" + profileBand + ")."), plateTitle, new[] { "auto" }.Concat(catalog.Titles.Keys), "auto");
                if (Has("ai_title_instance"))
                    plateAiTitle = TitlePicker(new GUIContent("AI title"), plateAiTitle, catalog.AiTitles.Keys, null);
                if (Has("title_instance") || Has("ai_title_instance"))
                {
                    // What the plate wears for it, and the way to change one part of it.
                    var parts = PlateComposition.Worn(catalog, (Newtonsoft.Json.Linq.JArray)rows, Info());
                    foreach (var slot in NameplateSlots.All) PartLine(catalog, slot, parts.In(slot));
                    if (GUILayout.Button(new GUIContent("Customize", "The custom layout wearing these three parts, to swap any of them.")))
                    {
                        WearParts(parts);
                        if (Has("title_instance") && plateTitle == "auto") plateTitle = profileBand;
                        plateState = "custom";
                        layersChanged = true;
                    }
                }
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
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Show", "Leaves layers out of the picture (and of saved pictures) to look at each on its own. The plate wears the same."));
                hideBand = !GUILayout.Toggle(!hideBand, "Band", EditorStyles.miniButtonLeft);
                hideEffect = !GUILayout.Toggle(!hideEffect, "Effect", EditorStyles.miniButtonMid);
                hideIcon = !GUILayout.Toggle(!hideIcon, "Icon", EditorStyles.miniButtonMid);
                layersOnly = !GUILayout.Toggle(!layersOnly, new GUIContent("Plate", "The rest of the plate: its base, badge, rank and text."), EditorStyles.miniButtonRight);
            }
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
                if (GUILayout.Button(new GUIContent("Render all…", "Image sheets: every title, every item over its clip, every layout, and up to 64 combinations of the items folder's own files."))) RenderAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Contents", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(contents, EditorStyles.wordWrappedMiniLabel,
                GUILayout.Height(EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(contents), SideWidth - 30)));
        }

        void SetItem(NameplateSlot slot, string id)
        {
            if (slot == NameplateSlot.Band) bandItem = id;
            else if (slot == NameplateSlot.Effect) effectItem = id;
            else iconItem = id;
        }

        void WearParts(TitleParts parts)
        {
            foreach (var slot in NameplateSlots.All) SetItem(slot, parts.In(slot) ?? "");
        }

        // A title, its family as a submenu (toho ▸ toho_y22_3), with buttons to step through them.
        string TitlePicker(GUIContent label, string value, IEnumerable<string> labels, string first)
        {
            var menu = Menu("titles:" + first + ":" + label.text, () =>
            {
                var ids = labels.ToList();
                var families = ids.GroupBy(NameplatePreview.Series).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                return (ids, ids.Select(id => new GUIContent(id == first || !families.Contains(NameplatePreview.Series(id)) ? id : NameplatePreview.Series(id) + "/" + id)).ToArray());
            });
            return Picker(label, value, menu.ids, menu.options);
        }

        // A slot's items: none, the set's by the title family they come from, and the items
        // folder's own; one worn but gone from the folder is kept, and said.
        string ItemPicker(PlateComposition.Catalog catalog, NameplateSlot slot, string id)
        {
            var menu = Menu("items:" + slot, () =>
            {
                var ids = new List<string> { "" };
                var options = new List<GUIContent> { new GUIContent(None) };
                foreach (string name in NameplateItems.Names(slot))
                {
                    string item = NameplateSlots.Id(slot, name);
                    ids.Add(item);
                    if (!NameplatePreview.InSet(catalog, slot, item)) { options.Add(new GUIContent("Items folder/" + name)); continue; }
                    var titles = NameplatePreview.UsedBy(catalog, slot, item);
                    options.Add(new GUIContent(titles.Count == 0 ? "Set/" + name : "Set/" + NameplatePreview.Series(titles[0].Replace("AI ", "")) + "/" + name));
                }
                return (ids, options.ToArray());
            });
            var (choices, labels) = menu;
            if (!string.IsNullOrEmpty(id) && !choices.Contains(id))
            {
                NameplateSlots.TryParse(id, out _, out string gone);
                choices = new List<string>(choices) { id };
                labels = labels.Append(new GUIContent((gone ?? id) + " (missing)")).ToArray();
            }
            string label = slot switch { NameplateSlot.Band => "Band", NameplateSlot.Effect => "Effect", _ => "Icon" };
            string worn = Picker(new GUIContent(label), id ?? "", choices, labels);
            PartLine(catalog, slot, worn, inPicker: true);
            return worn;
        }

        // The pickers' lists, made again when the items folder or the catalog may have changed.
        readonly Dictionary<string, (List<string> ids, GUIContent[] options)> menus = new Dictionary<string, (List<string>, GUIContent[])>();

        (List<string> ids, GUIContent[] options) Menu(string key, Func<(List<string>, GUIContent[])> make)
        {
            if (!menus.TryGetValue(key, out var menu)) menus[key] = menu = make();
            return menu;
        }

        // One worn part: its name (unless the picker above shows it), where it is from, and whether
        // it is cut to the band.
        static void PartLine(PlateComposition.Catalog catalog, NameplateSlot slot, string id, bool inPicker = false)
        {
            if (string.IsNullOrEmpty(id))
            {
                if (!inPicker) EditorGUILayout.LabelField(NameplateSlots.Folder(slot), "none", EditorStyles.miniLabel);
                return;
            }
            var notes = new List<string>();
            string tip = id;
            if (NameplatePreview.InSet(catalog, slot, id))
            {
                var titles = NameplatePreview.UsedBy(catalog, slot, id);
                if (titles.Count > 0)
                {
                    notes.Add("in " + string.Join(", ", titles.Take(3)) + (titles.Count > 3 ? $" +{titles.Count - 3}" : ""));
                    tip += "\nPart of " + string.Join(", ", titles);
                }
            }
            else notes.Add("items folder");
            if (slot == NameplateSlot.Effect) notes.Add(catalog.ClipsEffect(id) ? "cut to the band" : "runs outside the band");
            NameplateSlots.TryParse(id, out _, out string name);
            string text = (inPicker ? "" : name + "  ") + string.Join("; ", notes);
            EditorGUILayout.LabelField(new GUIContent(inPicker ? " " : NameplateSlots.Folder(slot)), new GUIContent(text, tip), EditorStyles.miniLabel);
        }

        // A menu's pick, which arrives after the repaint that opened it: its picker takes it next time.
        string pickedFor, picked;

        // A dropdown showing the picked id's name, its menu grouped by the paths ("a/b" opens a
        // submenu), with ◀ ▶ to step through the ids.
        string Picker(GUIContent label, string value, IReadOnlyList<string> ids, GUIContent[] paths)
        {
            string key = label.text;
            if (pickedFor == key)
            {
                value = picked;
                pickedFor = null;
                GUI.changed = true;
            }
            int at = -1;
            for (int i = 0; i < ids.Count; i++) if (ids[i] == value) at = i;
            using (new EditorGUILayout.HorizontalScope())
            {
                var rect = EditorGUI.PrefixLabel(EditorGUILayout.GetControlRect(true), label);
                string path = at < 0 ? value : paths[at].text;
                if (EditorGUI.DropdownButton(rect, new GUIContent(path.Substring(path.LastIndexOf('/') + 1), path), FocusType.Keyboard))
                {
                    var menu = new GenericMenu();
                    for (int i = 0; i < ids.Count; i++)
                    {
                        string id = ids[i];
                        menu.AddItem(paths[i], i == at, () => { pickedFor = key; picked = id; Repaint(); });
                    }
                    menu.DropDown(rect);
                }
                int step = 0;
                if (GUILayout.Button("◀", EditorStyles.miniButtonLeft, GUILayout.Width(22))) step = -1;
                if (GUILayout.Button("▶", EditorStyles.miniButtonRight, GUILayout.Width(22))) step = 1;
                if (step != 0 && ids.Count > 0)
                {
                    value = ids[((at < 0 ? 0 : at) + step + ids.Count) % ids.Count];
                    GUI.changed = true;
                }
            }
            return value;
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

        // What the plate wears: its layout and title, and for each layer the item, where it comes
        // from, whether it is cut to the band, and its picture, parts and the clip it plays.
        public static string Describe(PlayerInfo info, int side)
        {
            var text = new StringBuilder();
            var catalog = PlateComposition.ReadCatalog();
            string state = PlateComposition.StateFor(info, side);
            var rows = (Newtonsoft.Json.Linq.JArray)catalog.States[state];
            text.AppendLine(info.plateState == state ? "Layout: " + state : $"Layout: {info.plateState} → {state}");
            string label = PlateComposition.TitleOf(catalog, rows, info, out bool ai);
            if (label == null) text.AppendLine("No title on this layout.");
            else if (info.plateState == "custom") text.AppendLine($"Custom layers; an empty band slot wears the band of {label}.");
            else text.AppendLine((ai ? "AI title: " : "Title: ") + label);
            if (info.IsCoin) text.AppendLine("No title and no 段位: the coin plate, which wears no layers.");
            var worn = PlateComposition.Worn(catalog, rows, info);
            foreach (var slot in NameplateSlots.All)
            {
                string id = worn.In(slot);
                string name = NameplateSlots.Folder(slot);
                if (string.IsNullOrEmpty(id)) { text.AppendLine(name + ": none"); continue; }
                var item = NameplateItems.Load(slot, id);
                if (item == null) { text.AppendLine($"{name}: {id} cannot be worn (missing, unreadable or not drawn on the 408×96 plate; see the console)"); continue; }
                var file = item.File;
                var clip = NameplateItems.ClipOf(file);
                string length = clip == null ? "-" : clip.Frames.Length == 1 ? "still" : clip.Loop ? (clip.Frames.Length / clip.Fps).ToString("0.##") + " s, loops" : clip.Duration.ToString("0.##") + " s, once";
                text.AppendLine($"{name}: {id}");
                if (NameplatePreview.InSet(catalog, slot, id))
                {
                    var titles = NameplatePreview.UsedBy(catalog, slot, id);
                    text.AppendLine("  in the set" + (titles.Count > 0 ? ", part of " + string.Join(", ", titles) : ""));
                }
                else text.AppendLine("  from the items folder" + (slot == NameplateSlot.Band ? ", cut to the band" : ""));
                if (slot == NameplateSlot.Effect) text.AppendLine(catalog.ClipsEffect(id) ? "  cut to the band" : "  runs outside the band");
                text.AppendLine($"  picture {item.texture.width}×{item.texture.height}, {file.Parts.Length} parts, clip {clip?.Name} ({clip?.Frames.Length} frames, {length})");
                if (clip != null && clip.Name != NameplateItems.ClipName) text.AppendLine($"  no \"{NameplateItems.ClipName}\" clip: its first is played");
            }
            text.AppendLine("Items folder: " + NameplateItems.Folder);
            return text.ToString().TrimEnd();
        }
    }
}
