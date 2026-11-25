using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OurTaiko.Chara;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    // A nameplate outside play: the game's own Nameplate prefab in a preview scene of its own,
    // wearing what it is given (a PlayerInfo: its layout and title, or its own band, effect and icon
    // items) at a time the caller moves on, drawn to a texture. NameplatePreviewWindow shows it;
    // batch: -executeMethod OurTaiko.Editor.NameplatePreview.RenderBatch with NAMEPLATE_PREVIEW_DIR
    // set (and NAMEPLATE_ITEMS_DIR for another items folder) writes the sheets RenderAll makes.
    public sealed class NameplatePreview : IDisposable
    {
        public const string PrefabPath = "Assets/OurTaiko/Generated/Nameplate.prefab";
        // Space around the plate, in plate pixels: an icon may stand out of it.
        public const int Margin = 16;
        const int UiLayer = 5;

        public NameplateView Plate { get; }
        public PlayerInfo Info { get; private set; } = new PlayerInfo();
        public int Side { get; private set; }
        // Seconds the layers have been running since Show.
        public double Seconds { get; private set; }

        public static int Width => (int)NameplateLayout.Width + 2 * Margin;
        public static int Height => (int)NameplateLayout.Height + 2 * Margin;

        readonly Scene scene;
        readonly Camera camera;
        readonly Canvas canvas;
        readonly CanvasScaler scaler;
        RenderTexture target;

        // Slots left out of the picture, and whether the rest of the plate (base, badge, rank and
        // text) is too: to look at a title's layers one by one. Only the picture changes; the
        // plate wears the same, so its clips run on.
        public readonly HashSet<NameplateSlot> Hidden = new HashSet<NameplateSlot>();
        public bool LayersOnly { get; set; }

        public Color Background
        {
            get => camera.backgroundColor;
            set => camera.backgroundColor = value;
        }

        public NameplatePreview()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
                ?? throw new InvalidOperationException("No nameplate prefab at " + PrefabPath);
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraObject = new GameObject("PreviewCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.22f, 0.22f, 0.25f, 1);
                camera.cullingMask = 1 << UiLayer;

                var canvasObject = new GameObject("PreviewCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)) { layer = UiLayer };
                SceneManager.MoveGameObjectToScene(canvasObject, scene);
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 10;
                scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

                var plate = (GameObject)UnityEngine.Object.Instantiate(prefab, canvas.transform, false);
                plate.name = prefab.name;
                foreach (var t in plate.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;
                Plate = plate.GetComponent<NameplateView>();
                Plate.Place(Margin, Margin);
            }
            catch
            {
                EditorSceneManager.ClosePreviewScene(scene);
                throw;
            }
        }

        public void Show(PlayerInfo info, int side)
        {
            Info = info.Clone();
            Side = side;
            Seconds = 0;
            Plate.Pin(Info, side);
            Plate.ShowAt(0);
        }

        // Shows the same again, its files read again if they changed.
        public void Refresh() { Plate.Pin(Info, Side); Plate.ShowAt(Seconds); }

        public void Step(double seconds)
        {
            Seconds += Math.Max(0, seconds);
            Plate.ShowAt(Seconds);
        }

        public void Seek(double seconds)
        {
            Seconds = Math.Max(0, seconds);
            Plate.ShowAt(Seconds);
        }

        // The plate and its margin, `scale` pixels to a plate pixel.
        public RenderTexture Render(int scale)
        {
            scale = Math.Max(1, scale);
            int width = Width * scale, height = Height * scale;
            if (target == null || target.width != width || target.height != height)
            {
                ReleaseTarget();
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            }
            camera.targetTexture = target;
            scaler.scaleFactor = scale;
            scaler.SendMessage("Handle");
            var hidden = HideForPicture();
            try
            {
                Canvas.ForceUpdateCanvases();
                camera.Render();
            }
            finally
            {
                foreach (var shown in hidden) shown.SetActive(true);
            }
            return target;
        }

        // Turns off what the picture leaves out, and says what to turn on again.
        List<GameObject> HideForPicture()
        {
            var hidden = new List<GameObject>();
            void Hide(Component part)
            {
                if (part == null || !part.gameObject.activeSelf) return;
                part.gameObject.SetActive(false);
                hidden.Add(part.gameObject);
            }
            var layers = Plate.Layers;
            if (layers != null)
                foreach (var slot in Hidden) Hide(layers.View(slot));
            if (LayersOnly)
            {
                var keep = new HashSet<Transform>();
                if (layers?.Mask != null)
                {
                    keep.Add(layers.Mask.parent);
                    foreach (var slot in NameplateSlots.All) keep.Add(layers.View(slot).transform);
                }
                var root = Plate.transform.Find(PlateComposition.RootName);
                if (root != null)
                    foreach (Transform part in root)
                        if (!keep.Contains(part)) Hide(part);
            }
            return hidden;
        }

        public Texture2D Capture(int scale)
        {
            var rendered = Render(scale);
            var previous = RenderTexture.active;
            RenderTexture.active = rendered;
            var picture = new Texture2D(rendered.width, rendered.height, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(0, 0, rendered.width, rendered.height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            return picture;
        }

        void ReleaseTarget()
        {
            if (target == null) return;
            if (camera != null) camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            target = null;
        }

        public void Dispose()
        {
            ReleaseTarget();
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        // The plate every rendered sheet wears: a title and a 段位, so the band and the chip show.
        // A sheet writes what each picture is in its title text.
        public static PlayerInfo SampleInfo(string title = "Nameplate layers") => new PlayerInfo { plateState = "custom", name = "OurTaiko", title = title, dan = 10, danCombo = 1 };

        // A built-in title as a player's plate wears it, its label in the title text. An AI title
        // is shown on the AI plate that wears one.
        public static PlayerInfo TitleInfo(string label, bool ai) => ai
            ? new PlayerInfo { plateState = "ai_data_shougou", plateAiTitle = label, name = "OurTaiko", title = "AI " + label }
            : new PlayerInfo { plateTitle = label, name = "OurTaiko", title = label, dan = 10, danCombo = 1 };

        // The titles made with an item ("AI " before an AI title's label), in the catalog's order.
        public static List<string> UsedBy(PlateComposition.Catalog catalog, NameplateSlot slot, string id)
        {
            var titles = new List<string>();
            foreach (var title in catalog.Titles)
                if (title.Value.In(slot) == id) titles.Add(title.Key);
            foreach (var title in catalog.AiTitles)
                if (title.Value.In(slot) == id) titles.Add("AI " + title.Key);
            return titles;
        }

        // The family a title belongs to, for grouping long lists: what comes before its first "_"
        // ("toho_y22_3" is a toho title), or the label itself.
        public static string Series(string label)
        {
            int cut = label.IndexOf('_');
            return cut > 0 ? label.Substring(0, cut) : label;
        }

        // How long an item's clip takes to come round (its loop, or a one-shot's length).
        public static double Length(Otanim file)
        {
            var clip = NameplateItems.ClipOf(file);
            if (clip == null) return 0;
            return clip.Loop ? (clip.Frames.Length - clip.LoopStart) / clip.Fps : clip.Duration;
        }

        public static void RenderBatch()
        {
            string dir = Environment.GetEnvironmentVariable("NAMEPLATE_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) throw new InvalidOperationException("Set NAMEPLATE_PREVIEW_DIR");
            string items = Environment.GetEnvironmentVariable("NAMEPLATE_ITEMS_DIR");
            if (!string.IsNullOrEmpty(items)) NameplateItems.UseFolder(items);
            RenderAll(dir);
        }

        public const int Frames = 6, MostCombinations = 64;
        // When a title sheet catches its titles: late enough for the first glints.
        public const double TitleMoment = 1;

        // titles.png: every built-in title (88, then the 8 AI ones) as a plate wears it, its label
        // in its title text; items.png: a row an item, worn alone and named in the title text, at
        // Frames moments of its clip; layouts.png: every layout; combinations.png: every band,
        // effect and icon of the items folder worn together (none among them), each half a second
        // in. A sheet too big for one picture goes on in name-2.png and so on.
        public static void RenderAll(string dir, int scale = 1)
        {
            scale = Math.Max(1, scale);
            Directory.CreateDirectory(dir);
            NameplatePreview preview = null;
            var pictures = new List<Texture2D>();
            try
            {
                preview = new NameplatePreview();
                var catalog = PlateComposition.ReadCatalog();
                var rows = new List<Texture2D[]>();
                int itemCount = 0, page = 0;
                // Keep the sheet and its captures bounded even with the full title catalog.
                int maxRows = Math.Max(1, Math.Min(SystemInfo.maxTextureSize / (Height * scale),
                    4_000_000 / (Frames * Width * Height * scale * scale)));
                void SavePage(string name)
                {
                    if (rows.Count == 0) return;
                    string file = ++page == 1 ? name + ".png" : $"{name}-{page}.png";
                    Sheet(rows, Path.Combine(dir, file));
                    foreach (var picture in pictures) UnityEngine.Object.DestroyImmediate(picture);
                    pictures.Clear();
                    rows.Clear();
                }
                var currentRow = new List<Texture2D>();
                void CaptureGridCell(string name)
                {
                    var picture = preview.Capture(scale);
                    pictures.Add(picture);
                    currentRow.Add(picture);
                    if (currentRow.Count < 4) return;
                    rows.Add(currentRow.ToArray());
                    currentRow.Clear();
                    if (rows.Count == maxRows) SavePage(name);
                }
                void FinishGrid(string name)
                {
                    if (currentRow.Count > 0) rows.Add(currentRow.ToArray());
                    currentRow.Clear();
                    SavePage(name);
                    page = 0;
                }

                foreach (var (titles, ai) in new[] { (catalog.Titles, false), (catalog.AiTitles, true) })
                    foreach (string label in titles.Keys)
                    {
                        preview.Show(TitleInfo(label, ai), 0);
                        preview.Seek(TitleMoment);
                        CaptureGridCell("titles");
                    }
                FinishGrid("titles");

                foreach (var slot in NameplateSlots.All)
                    foreach (string name in NameplateItems.Names(slot))
                    {
                        string id = NameplateSlots.Id(slot, name);
                        var item = NameplateItems.Load(slot, id);
                        if (item == null) continue;
                        var info = SampleInfo(id);
                        info.Wear(slot, id);
                        preview.Show(info, 0);
                        double length = Length(item.File);
                        var row = new Texture2D[Frames];
                        for (int f = 0; f < Frames; f++)
                        {
                            preview.Seek(length * f / Frames);
                            row[f] = preview.Capture(scale);
                            pictures.Add(row[f]);
                        }
                        rows.Add(row);
                        itemCount++;
                        if (rows.Count == maxRows) SavePage("items");
                    }
                SavePage("items");
                page = 0;

                foreach (var state in catalog.States.Properties())
                {
                    var info = SampleInfo(state.Name);
                    info.plateState = state.Name;
                    preview.Show(info, 0);
                    preview.Seek(0.5);
                    CaptureGridCell("layouts");
                }
                FinishGrid("layouts");

                // The built-in items come in titles, so what is combined is the folder's own.
                var band = Choices(NameplateSlot.Band, catalog);
                var effect = Choices(NameplateSlot.Effect, catalog);
                var icon = Choices(NameplateSlot.Icon, catalog);
                int made = 0;
                if (band.Count + effect.Count + icon.Count > 3)
                    foreach (string b in band)
                        foreach (string e in effect)
                            foreach (string i in icon)
                            {
                                if (made >= MostCombinations || b + e + i == "") continue;
                                made++;
                                var info = SampleInfo(string.Join(" + ", new[] { b, e, i }.Where(id => id != "")));
                                info.bandItem = b;
                                info.effectItem = e;
                                info.iconItem = i;
                                preview.Show(info, 0);
                                preview.Seek(0.5);
                                CaptureGridCell("combinations");
                            }
                FinishGrid("combinations");
                Debug.Log($"Nameplate sheets written to {dir}: {catalog.Titles.Count + catalog.AiTitles.Count} titles, {itemCount} items, {made} combinations of the items folder");
            }
            finally
            {
                preview?.Dispose();
                foreach (var picture in pictures) UnityEngine.Object.DestroyImmediate(picture);
            }
        }

        // The slot's items in the items folder (not built in), and "" for none.
        static List<string> Choices(NameplateSlot slot, PlateComposition.Catalog catalog)
        {
            var list = new List<string> { "" };
            foreach (string name in NameplateItems.Names(slot))
            {
                string id = NameplateSlots.Id(slot, name);
                if (!IsBuiltIn(catalog, slot, id)) list.Add(id);
            }
            return list;
        }

        // An item the game carries (a part of a title, or the rank-up effect), not a file of the items folder.
        public static bool IsBuiltIn(PlateComposition.Catalog catalog, NameplateSlot slot, string id) =>
            catalog.Contains(slot, id) || id == PlateComposition.RankUpItem;

        // Rows of same-sized pictures, the first at the top.
        static void Sheet(List<Texture2D[]> rows, string path)
        {
            if (rows.Count == 0) return;
            int w = rows[0][0].width, h = rows[0][0].height, columns = 0;
            foreach (var row in rows) columns = Math.Max(columns, row.Length);
            var sheet = new Texture2D(w * columns, h * rows.Count, TextureFormat.RGBA32, false);
            var clear = new Color32[sheet.width * sheet.height];
            sheet.SetPixels32(clear);
            for (int r = 0; r < rows.Count; r++)
                for (int c = 0; c < rows[r].Length; c++)
                    sheet.SetPixels(c * w, (rows.Count - 1 - r) * h, w, h, rows[r][c].GetPixels());
            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }
    }
}
