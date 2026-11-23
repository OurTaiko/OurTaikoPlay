using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OurTaiko.Chara;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Menu OurTaiko/Stage Preset Viewer: the play backdrop a #SCENEPRESET (or a draw) gives, running,
    // with the states a play puts it in under the window's controls, and what each of its files holds.
    public sealed class StagePreviewWindow : EditorWindow
    {
        const string Random = "(no preset: every part drawn)";
        const float Side = 340;

        [SerializeField] StageMode mode = StageMode.Single;
        [SerializeField] string preset = "";
        [SerializeField] int seed;
        [SerializeField] double[] gauges = { 0.3, 0.3 };
        [SerializeField] bool[] missing = { false, false };
        [SerializeField] bool[] rolling = { false, false };
        [SerializeField] double bpm = 120;
        [SerializeField] float speed = 1;
        [SerializeField] bool paused, showLane = true;
        [SerializeField] Vector2 scroll;

        StagePreview preview;
        StageChoice choice;
        string contents = "", failure;
        double lastUpdate, sinceRoll;
        Texture shown;

        [MenuItem("OurTaiko/Stage Preset Viewer")]
        static void Open() => GetWindow<StagePreviewWindow>("Stage Presets").minSize = new Vector2(900, 480);

        void OnEnable()
        {
            lastUpdate = EditorApplication.timeSinceStartup;
            EditorApplication.update += Advance;
            Rebuild();
        }

        void OnDisable()
        {
            EditorApplication.update -= Advance;
            preview?.Dispose();
            preview = null;
            shown = null;
        }

        // Another mode is another scene.
        void Rebuild()
        {
            preview?.Dispose();
            preview = null;
            shown = null;
            failure = null;
            // A play scene cannot be opened beside itself while the game runs.
            if (EditorApplication.isPlayingOrWillChangePlaymode) { failure = "Not while the game is playing."; return; }
            try
            {
                preview = new StagePreview(mode);
                Choose();
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
        }

        void Choose() => Use(preview.Choose(preset, seed));

        void Use(StageChoice next)
        {
            choice = next;
            preview.Use(choice);
            preview.LaneShown = showLane;
            contents = Describe(preview, choice);
            Draw(0);
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
            if (paused) return;
            step *= speed;
            sinceRoll += step;
            // A drumroll at 15 hits a second.
            if (sinceRoll >= 1 / 15.0)
            {
                sinceRoll = 0;
                for (int i = 0; i < preview.Players; i++) if (rolling[i]) preview.DrumrollHit(i);
            }
            Draw(step);
            Repaint();
        }

        void Draw(double step)
        {
            preview.Step(step, bpm, gauges, missing);
            shown = preview.Render(1920, 1080);
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
                using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.Width(Side)))
                {
                    scroll = view.scrollPosition;
                    Controls();
                }
                var area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                if (shown != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(area, shown, ScaleMode.ScaleToFit, false);
            }
        }

        void Controls()
        {
            var catalog = preview.Catalog;
            EditorGUIUtility.labelWidth = 90;

            EditorGUI.BeginChangeCheck();
            mode = (StageMode)EditorGUILayout.EnumPopup("Mode", mode);
            if (EditorGUI.EndChangeCheck()) { Rebuild(); GUIUtility.ExitGUI(); }

            var presets = new[] { Random }.Concat(catalog.packs.Select(p => p.preset.Trim())).ToArray();
            int at = Math.Max(0, Array.FindIndex(presets, p => string.Equals(p, preset, StringComparison.OrdinalIgnoreCase)));
            EditorGUI.BeginChangeCheck();
            at = EditorGUILayout.Popup("#SCENEPRESET", at, presets);
            if (EditorGUI.EndChangeCheck()) { preset = at == 0 ? "" : presets[at]; Choose(); }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀")) Step(presets, -1);
                if (GUILayout.Button("▶")) Step(presets, 1);
                if (GUILayout.Button(new GUIContent("Draw again", "Another draw of the parts the preset leaves open."))) { seed++; Choose(); }
            }
            string note = Note(catalog);
            if (note != null) EditorGUILayout.HelpBox(note, MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Parts", EditorStyles.boldLabel);
            bool solo = preview.Stage.scene != null;
            Part("Header", ref choice.Header, catalog.headers.Select(s => s.name), true);
            Part("Scene", ref choice.Scene, catalog.scenes.Select(s => s.name), solo);
            // "-" and not null: null leaves the scene the clear clips of its own file.
            Part("Clear scene", ref choice.Clear, catalog.clears.Select(s => s.name), solo, StagePack.None);
            Part("Dancers", ref choice.Troupe, catalog.troupes.Select(s => s.name), preview.Stage.dancers != null);
            Part("Stand", ref choice.Footer, catalog.footers.Select(s => s.name), preview.Stage.footer != null);
            Part("Crowd", ref choice.Crowd, catalog.crowds.Select(s => s.name), preview.Stage.crowd != null);
            Part("Drumroll", ref choice.Renda, catalog.rendas.Select(s => s.name), preview.Stage.renda != null && preview.Stage.renda.Length > 0);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("State", EditorStyles.boldLabel);
            for (int i = 0; i < preview.Players; i++)
            {
                string who = (i + 1) + "P";
                gauges[i] = EditorGUILayout.Slider(new GUIContent(who + " gauge", $"Clear from {StagePreview.ClearLine:0.0}, full at 1."), (float)gauges[i], 0, 1);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Empty")) gauges[i] = 0;
                    if (GUILayout.Button("Half")) gauges[i] = StagePreview.ClearLine / 2;
                    if (GUILayout.Button("Clear")) gauges[i] = StagePreview.ClearLine;
                    if (GUILayout.Button("Full")) gauges[i] = 1;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    missing[i] = GUILayout.Toggle(missing[i], new GUIContent("Miss streak", "The header goes dark."), "Button");
                    rolling[i] = GUILayout.Toggle(rolling[i], "Drumroll", "Button");
                    if (GUILayout.Button("One hit")) preview.DrumrollHit(i);
                }
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(catalog.Pack(choice.Preset)?.chibi)))
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel("Hit characters");
                if (GUILayout.Button("Good")) preview.Judged(false, bpm);
                if (GUILayout.Button("Miss")) preview.Judged(true, bpm);
            }
            bpm = EditorGUILayout.DoubleField("BPM", bpm);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
            speed = EditorGUILayout.Slider("Speed", speed, 0.1f, 3);
            using (new EditorGUILayout.HorizontalScope())
            {
                paused = GUILayout.Toggle(paused, "Pause", "Button");
                if (GUILayout.Button("Restart")) Use(choice);
                EditorGUI.BeginChangeCheck();
                showLane = GUILayout.Toggle(showLane, new GUIContent("Lane strip", "A plain strip where the play puts its lanes."), "Button");
                if (EditorGUI.EndChangeCheck()) { preview.LaneShown = showLane; Draw(0); }
            }
            EditorGUILayout.LabelField("Time", preview.Seconds.ToString("0.00") + " s");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save picture…")) Save();
                if (GUILayout.Button(new GUIContent("Render all presets…", "Every preset in each state, and one sheet of them all."))) RenderAll();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Contents", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(contents, EditorStyles.wordWrappedMiniLabel, GUILayout.Height(EditorStyles.wordWrappedMiniLabel.CalcHeight(new GUIContent(contents), Side - 30)));
        }

        void Step(string[] presets, int by)
        {
            int at = Math.Max(0, Array.FindIndex(presets, p => string.Equals(p, preset, StringComparison.OrdinalIgnoreCase)));
            at = (at + by + presets.Length) % presets.Length;
            preset = at == 0 ? "" : presets[at];
            Choose();
        }

        // Why the preset on show is not what this mode gets, when it is not.
        string Note(StageCatalog catalog)
        {
            var asked = catalog.Pack(preset);
            if (choice.Preset != null && !string.Equals(choice.Preset, preset, StringComparison.OrdinalIgnoreCase))
                return $"This mode always uses {choice.Preset}, whatever the chart names.";
            if (asked != null && choice.Preset == null)
                return $"{asked.preset} is for solo play only; here every part is drawn as if the chart named none.";
            return null;
        }

        // none is what the part is called when it is left out.
        void Part(string label, ref string name, IEnumerable<string> sets, bool used, string none = null)
        {
            using (new EditorGUI.DisabledScope(!used))
            {
                var names = new[] { StagePack.None }.Concat(sets).ToArray();
                int at = Math.Max(0, Array.IndexOf(names, name));
                EditorGUI.BeginChangeCheck();
                at = EditorGUILayout.Popup(label, used ? at : 0, names);
                if (!EditorGUI.EndChangeCheck()) return;
                name = at == 0 ? none : names[at];
                Use(choice);
            }
        }

        void Save()
        {
            string path = EditorUtility.SaveFilePanel("Stage picture", "", (string.IsNullOrEmpty(choice.Preset) ? "stage" : choice.Preset) + ".png", "png");
            if (string.IsNullOrEmpty(path)) return;
            var picture = preview.Capture(1920, 1080);
            File.WriteAllBytes(path, picture.EncodeToPNG());
            DestroyImmediate(picture);
        }

        void RenderAll()
        {
            string dir = EditorUtility.SaveFolderPanel("Stage presets", "", "");
            if (!string.IsNullOrEmpty(dir)) StagePreview.RenderAll(dir);
            GUIUtility.ExitGUI();
        }

        // What each of the backdrop's files holds: its clips, how long each is and whether it loops.
        public static string Describe(StagePreview preview, StageChoice choice)
        {
            var catalog = preview.Catalog;
            var text = new StringBuilder();
            text.AppendLine(choice.Preset != null ? "Preset " + choice.Preset : "No preset: every part drawn at random");
            Lines(text, "Header", StageCatalog.HeaderFolder, choice.Header);
            if (preview.Players > 1 && choice.Header != null)
                text.AppendLine(StageLibrary.Load(StageCatalog.HeaderFolder, choice.Header + "_2p", quiet: true) != null
                    ? "  2P has a header of its own (" + choice.Header + "_2p)" : "  2P shares 1P's header");
            Lines(text, "Drumroll", StageCatalog.RendaFolder, choice.Renda);
            if (preview.Stage.scene != null)
            {
                Lines(text, "Scene", StageCatalog.SceneFolder, choice.Scene);
                Lines(text, "Clear scene", StageCatalog.ClearFolder, choice.Clear == StagePack.None ? null : choice.Clear);
                var troupe = catalog.Troupe(choice.Troupe);
                if (troupe == null) text.AppendLine("Dancers: none");
                else if (troupe.movie)
                {
                    text.AppendLine($"Dancers: {troupe.name}, the whole troupe as one clip, always on stage");
                    Clips(text, StageCatalog.DancerFolder, troupe.name);
                }
                else
                {
                    text.AppendLine($"Dancers: {troupe.name}, {troupe.members} members; " + (troupe.leadFirst
                        ? "the lead alone, one more at each quarter of the way to the clear line, all five from it"
                        : "three from the start, the fourth from halfway to the clear line, the fifth from it"));
                    text.AppendLine($"  dance from frame {troupe.dance}, way out from {troupe.leave} (40 frames a beat)");
                    Clips(text, StageCatalog.DancerFolder, troupe.name + "_0");
                }
                Lines(text, "Stand", StageCatalog.FooterFolder, choice.Footer);
                Lines(text, "Crowd (full gauge)", StageCatalog.CrowdFolder, choice.Crowd);
            }
            string chibi = catalog.Pack(choice.Preset)?.chibi;
            if (!string.IsNullOrEmpty(chibi)) Lines(text, "Hit characters", "chibi", chibi);
            return text.ToString().TrimEnd();
        }

        static void Lines(StringBuilder text, string label, string folder, string name)
        {
            if (string.IsNullOrEmpty(name)) { text.AppendLine(label + ": none"); return; }
            text.AppendLine($"{label}: {folder}/{name}");
            Clips(text, folder, name);
        }

        static void Clips(StringBuilder text, string folder, string name)
        {
            var asset = StageLibrary.Load(folder, name, quiet: true);
            if (asset == null) { text.AppendLine("  (file missing)"); return; }
            var texture = asset.texture;
            text.AppendLine($"  picture {(texture != null ? texture.width + "×" + texture.height : "none")}, {asset.File.Parts.Length} parts");
            foreach (var clip in asset.File.Clips)
            {
                string length = (clip.Frames.Length / clip.Fps).ToString("0.##") + " s";
                string loop = !clip.Loop ? "once" : clip.LoopStart > 0 ? $"loops from {(clip.LoopStart / clip.Fps):0.##} s" : "loops";
                text.AppendLine($"  {clip.Name}: {length}, {loop}");
            }
        }
    }
}
