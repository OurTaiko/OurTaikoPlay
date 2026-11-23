using System;
using System.Collections.Generic;
using System.IO;
using OurTaiko.Chara;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    // The play backdrop outside play: a mode's own play scene opened as a preview scene with
    // everything but the backdrop turned off, driven by hand (which sets, where each player's
    // gauge is, drumroll hits) and drawn to a texture. StagePreviewWindow shows it; batch:
    // -executeMethod OurTaiko.Editor.StagePreview.RenderBatch with STAGE_PREVIEW_DIR set writes
    // every preset in each of its states.
    public sealed class StagePreview : IDisposable
    {
        // Where the preview puts the clear line on its 0 to 1 gauge (the game's depends on the difficulty).
        public const double ClearLine = 0.8;

        public StageView Stage { get; }
        public StageMode Mode { get; }
        public StageCatalog Catalog => Stage.Catalog;
        public int Players => Stage.headers.Length;
        // Seconds the backdrop has been running since Use.
        public double Seconds { get; private set; }

        readonly Scene scene;
        readonly Camera camera;
        readonly Canvas canvas;
        readonly GameObject lane;
        readonly Text caption;
        readonly StagePlayer[] players;
        RenderTexture target;

        public static string SceneName(StageMode mode) => mode switch
        {
            StageMode.Double => SceneSwitcher.DoublePlayScene,
            StageMode.Dan => SceneSwitcher.DanPlayScene,
            StageMode.Practice => SceneSwitcher.PracticeScene,
            // No scene asks for the tutorial's backdrop yet; it is shown where a solo play's is.
            _ => SceneSwitcher.GameScene,
        };

        public StagePreview(StageMode mode)
        {
            Mode = mode;
            string name = SceneName(mode);
            string path = Array.Find(EditorBuildSettings.scenes, s => Path.GetFileNameWithoutExtension(s.path) == name)?.path
                ?? throw new InvalidOperationException($"No scene named {name} in the build settings.");
            scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var found = root.GetComponentInChildren<StageView>(true);
                    if (found != null) { Stage = found; break; }
                }
                if (Stage == null) throw new InvalidOperationException($"{name} has no StageView.");
                Stage.mode = mode;
                players = new StagePlayer[Players];
                canvas = Stage.headers[0].GetComponentInParent<Canvas>(true).rootCanvas;
                camera = canvas.worldCamera;
                if (camera == null) throw new InvalidOperationException($"{name}'s canvas has no camera to draw it.");
                camera.scene = scene;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                KeepOnlyTheBackdrop();
                lane = LaneStrip();
                caption = CaptionText();
            }
            catch
            {
                EditorSceneManager.ClosePreviewScene(scene);
                throw;
            }
        }

        IEnumerable<Transform> Parts()
        {
            foreach (var header in Stage.headers) yield return header.transform;
            if (Stage.renda != null) foreach (var view in Stage.renda) yield return view.transform;
            if (Stage.scene != null) yield return Stage.scene.transform;
            if (Stage.dancers != null) yield return Stage.dancers.transform;
            if (Stage.footer != null) yield return Stage.footer.transform;
            if (Stage.crowd != null) yield return Stage.crowd.transform;
        }

        // Everything else a play scene holds is made or filled in by the play, so here it is empty.
        void KeepOnlyTheBackdrop()
        {
            var kept = new HashSet<Transform> { camera.transform };
            foreach (var part in Parts())
                for (var t = part; t != null; t = t.parent) kept.Add(t);
            var all = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects()) all.AddRange(root.GetComponentsInChildren<Transform>(true));
            var parts = new HashSet<Transform>(Parts());
            foreach (var t in all)
            {
                if (kept.Contains(t)) continue;
                // What is under a part is the part's own.
                bool inside = false;
                for (var up = t.parent; up != null && !inside; up = up.parent) inside = parts.Contains(up);
                if (!inside) t.gameObject.SetActive(false);
            }
        }

        // A plain strip where the play puts its lanes, between 1P's header and what comes below it.
        GameObject LaneStrip()
        {
            var frame = (RectTransform)Stage.headers[0].transform.parent;
            var below = Stage.headers.Length > 1 ? Stage.headers[1].transform : Stage.scene != null ? Stage.scene.transform : null;
            if (below == null) return null;
            // Both are the frame's own children. Their place in it is read from their rects: the
            // canvas is not laid out yet, so nothing has a place in the world.
            var header = (RectTransform)Stage.headers[0].transform;
            float top = header.localPosition.y + header.rect.yMin;
            float bottom = below.localPosition.y + ((RectTransform)below).rect.yMax;
            var strip = new GameObject("PreviewLane", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            var rect = (RectTransform)strip.transform;
            rect.SetParent(frame, false);
            // Added under the canvas, so the graphic finds it.
            strip.AddComponent<Image>().color = new Color(0.16f, 0.16f, 0.16f, 1);
            rect.anchorMin = new Vector2(0, 0.5f);
            rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Those are from the frame's pivot; these anchors are from its middle.
            float middle = (0.5f - frame.pivot.y) * frame.rect.height;
            rect.anchoredPosition = new Vector2(0, (top + bottom) / 2 - middle);
            rect.sizeDelta = new Vector2(0, top - bottom);
            return strip;
        }

        Text CaptionText()
        {
            var label = new GameObject("PreviewCaption", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline)) { hideFlags = HideFlags.DontSave };
            var rect = (RectTransform)label.transform;
            rect.SetParent(Stage.headers[0].transform.parent, false);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(32, -16);
            rect.sizeDelta = new Vector2(-64, 120);
            var text = label.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 96;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.raycastTarget = false;
            var outline = label.GetComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(4, -4);
            label.SetActive(false);
            return text;
        }

        // Written over the picture's top left corner; null for none.
        public string Caption
        {
            set { caption.text = value ?? ""; caption.gameObject.SetActive(!string.IsNullOrEmpty(value)); }
        }

        public bool LaneShown
        {
            get => lane != null && lane.activeSelf;
            set { if (lane != null) lane.SetActive(value); }
        }

        // Another backdrop, from its start.
        public void Use(StageChoice choice)
        {
            Stage.Use(choice);
            Seconds = 0;
        }

        // seconds later, with the chart at `bpm` and each player's gauge at `gauges` (0 to 1, the
        // clear line at ClearLine) and in a streak of misses or not.
        public void Step(double seconds, double bpm, IReadOnlyList<double> gauges, IReadOnlyList<bool> missing)
        {
            Seconds += Math.Max(0, seconds);
            for (int i = 0; i < players.Length; i++)
            {
                double gauge = i < gauges.Count ? gauges[i] : 0;
                players[i] = new StagePlayer
                {
                    Clear = gauge >= ClearLine,
                    GaugeFull = gauge >= 1,
                    MissStreak = i < missing.Count && missing[i],
                    Gauge = gauge / ClearLine,
                };
            }
            Stage.Show(Seconds, bpm, players);
            Stage.Draw(Seconds);
        }

        public void DrumrollHit(int player)
        {
            if (Stage.renda == null) return;
            foreach (var view in Stage.renda)
                if (view.player == player) view.Hit(Seconds, UnityEngine.Random.Range(0, int.MaxValue));
        }

        // 1P's judgement, for a pack with hit characters.
        public void Judged(bool missed, double bpm)
        {
            if (Stage.Characters != null) Stage.Characters.Judged(Seconds, missed, bpm);
        }

        public RenderTexture Render(int width, int height)
        {
            if (target == null || target.width != width || target.height != height)
            {
                if (target != null) { camera.targetTexture = null; target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                target = new RenderTexture(width, height, 24) { hideFlags = HideFlags.HideAndDontSave };
            }
            camera.targetTexture = target;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.SendMessage("Handle");
            Canvas.ForceUpdateCanvases();
            camera.Render();
            return target;
        }

        public Texture2D Capture(int width, int height)
        {
            var rendered = Render(width, height);
            var previous = RenderTexture.active;
            RenderTexture.active = rendered;
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            return picture;
        }

        public void Dispose()
        {
            if (target != null)
            {
                if (camera != null) camera.targetTexture = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                target = null;
            }
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        // One state of a backdrop to picture: its name, each player's gauge, and what is thrown at it.
        public readonly struct Shot
        {
            public readonly string Name;
            public readonly double Gauge;
            public readonly bool Missing, Hits;
            public Shot(string name, double gauge, bool missing = false, bool hits = false) { Name = name; Gauge = gauge; Missing = missing; Hits = hits; }
        }

        public static readonly Shot[] Shots =
        {
            new Shot("normal", 0.3),
            new Shot("miss", 0.3, missing: true),
            new Shot("clear", 0.85),
            new Shot("full", 1, hits: true),
        };

        // The backdrop `seconds` in, having been in `shot`'s state all the while; with hits, a
        // drumroll and good judgements over the last second.
        public void Run(Shot shot, double seconds, double bpm = 120)
        {
            const double step = 1 / 30.0;
            var gauges = new double[Players];
            var missing = new bool[Players];
            for (int i = 0; i < Players; i++) { gauges[i] = shot.Gauge; missing[i] = shot.Missing; }
            int steps = (int)Math.Round(seconds / step);
            for (int i = 0; i <= steps; i++)
            {
                Step(i == 0 ? 0 : step, bpm, gauges, missing);
                if (!shot.Hits || steps - i > 30) continue;
                if (i % 3 == 0) for (int p = 0; p < Players; p++) DrumrollHit(p);
                if (i % 8 == 0) Judged(false, bpm);
            }
        }

        // The backdrop mode gives a chart naming `preset`; the parts a pack leaves open are drawn with `seed`.
        public StageChoice Choose(string preset, int seed) => Catalog.Choose(preset, Mode, new System.Random(seed));

        public static void RenderBatch()
        {
            string dir = Environment.GetEnvironmentVariable("STAGE_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Set STAGE_PREVIEW_DIR");
            RenderAll(dir);
        }

        // Every preset in every state, one picture each, and sheet.png with all of them small:
        // a row a preset, a column a state. Presets a mode fixes are shown in that mode.
        public static void RenderAll(string dir, int width = 1920, int height = 1080)
        {
            Directory.CreateDirectory(dir);
            const int small = 480, smallHeight = 270;
            var rows = new List<Texture2D[]>();
            StagePreview preview = null;
            try
            {
                preview = new StagePreview(StageMode.Single);
                var packs = preview.Catalog.packs;
                for (int index = 0; index < packs.Length; index++)
                {
                    string preset = packs[index].preset.Trim();
                    var mode = preset == StageCatalog.DanPreset ? StageMode.Dan
                        : preset == StageCatalog.PracticePreset ? StageMode.Practice
                        : preset == StageCatalog.TutorialPreset ? StageMode.Tutorial : StageMode.Single;
                    if (mode != preview.Mode) { preview.Dispose(); preview = new StagePreview(mode); }
                    EditorUtility.DisplayProgressBar("Stage presets", preset, index / (float)packs.Length);
                    var row = new Texture2D[Shots.Length];
                    for (int s = 0; s < Shots.Length; s++)
                    {
                        preview.Use(preview.Choose(preset, 0));
                        preview.Run(Shots[s], 8);
                        var picture = preview.Capture(width, height);
                        File.WriteAllBytes(Path.Combine(dir, $"{preset}-{Shots[s].Name}.png"), picture.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(picture);
                        preview.Caption = $"{preset} · {Shots[s].Name}";
                        row[s] = preview.Capture(small, smallHeight);
                        preview.Caption = null;
                    }
                    rows.Add(row);
                }
                var sheet = new Texture2D(small * Shots.Length, smallHeight * rows.Count, TextureFormat.RGB24, false);
                for (int r = 0; r < rows.Count; r++)
                    for (int s = 0; s < Shots.Length; s++)
                    {
                        // A texture's rows run upwards; the sheet's first preset is at its top.
                        sheet.SetPixels(s * small, (rows.Count - 1 - r) * smallHeight, small, smallHeight, rows[r][s].GetPixels());
                        UnityEngine.Object.DestroyImmediate(rows[r][s]);
                    }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(dir, "sheet.png"), sheet.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sheet);
                Debug.Log($"Stage presets written to {dir}: {rows.Count} presets, {string.Join(", ", Array.ConvertAll(Shots, s => s.Name))}");
            }
            finally
            {
                preview?.Dispose();
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
