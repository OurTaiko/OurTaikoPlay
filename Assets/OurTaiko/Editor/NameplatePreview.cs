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
    // A nameplate outside play: the game's own Nameplate prefab in a preview scene of its own,
    // wearing what it is given (a PlayerInfo with its band, effect and icon items, NameplateItems'
    // files) at a time the caller moves on, drawn to a texture. NameplatePreviewWindow shows it;
    // batch: -executeMethod OurTaiko.Editor.NameplatePreview.RenderBatch with NAMEPLATE_PREVIEW_DIR
    // set (and NAMEPLATE_ITEMS_DIR for another items folder) writes every item and combination.
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
            Canvas.ForceUpdateCanvases();
            camera.Render();
            return target;
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
        public static PlayerInfo SampleInfo() => new PlayerInfo { plateState = "custom", name = "OurTaiko", title = "Nameplate layers", dan = 10, danCombo = 1 };

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

        // items.png: a row an item, worn alone, at Frames moments of its clip; combinations.png:
        // every band, effect and icon worn together (none among them), each half a second in.
        public static void RenderAll(string dir, int scale = 1)
        {
            scale = Math.Max(1, scale);
            Directory.CreateDirectory(dir);
            NameplatePreview preview = null;
            var pictures = new List<Texture2D>();
            try
            {
                preview = new NameplatePreview();
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
                foreach (var slot in NameplateSlots.All)
                    foreach (string name in NameplateItems.Names(slot))
                    {
                        string id = NameplateSlots.Id(slot, name);
                        var item = NameplateItems.Load(slot, id);
                        if (item == null) continue;
                        var info = SampleInfo();
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

                var catalog = PlateComposition.ReadCatalog();
                if (catalog != null)
                {
                    foreach (var state in catalog.States.Properties())
                    {
                        var info = SampleInfo();
                        info.plateState = state.Name;
                        preview.Show(info, 0);
                        preview.Seek(0.5);
                        CaptureGridCell("layouts");
                    }
                    FinishGrid("layouts");
                }

                var band = Choices(NameplateSlot.Band);
                var effect = Choices(NameplateSlot.Effect);
                var icon = Choices(NameplateSlot.Icon);
                int made = 0;
                foreach (string b in band)
                    foreach (string e in effect)
                        foreach (string i in icon)
                        {
                            if (made++ >= MostCombinations) break;
                            var info = SampleInfo();
                            info.bandItem = b;
                            info.effectItem = e;
                            info.iconItem = i;
                            preview.Show(info, 0);
                            preview.Seek(0.5);
                            CaptureGridCell("combinations");
                        }
                FinishGrid("combinations");
                Debug.Log($"Nameplate layers written to {dir}: {itemCount} items, {Math.Min(made, MostCombinations)} combinations");
            }
            finally
            {
                preview?.Dispose();
                foreach (var picture in pictures) UnityEngine.Object.DestroyImmediate(picture);
            }
        }

        // The slot's items, and "" for none.
        static List<string> Choices(NameplateSlot slot)
        {
            var list = new List<string> { "" };
            foreach (string name in NameplateItems.Names(slot)) list.Add(NameplateSlots.Id(slot, name));
            return list;
        }

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
