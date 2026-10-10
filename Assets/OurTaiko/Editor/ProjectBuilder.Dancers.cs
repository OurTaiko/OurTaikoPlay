using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    // The play backdrop's dancers, from the Nijiiro arcade rig (Scripts/background/dancer_data).
    // Animations/dancer_0.txt is that skin's dancer_0.lua as exported.
    public static partial class ProjectBuilder
    {
        const string DancerRigPath = Root + "Animations/dancer_0.txt";
        const string DancerArt = "background/dancer/dancer_0/";
        const string DancerSkin = "../../YataiDON/Skins/YataiDONNijiiro/";
        const string DancerPrefabFolder = Root + "Generated/Dancers";
        const string DancerAtlasPath = DancerPrefabFolder + "/Dancer0.spriteatlasv2";
        // Transparent pixels kept around the frames' shared bounds.
        const int DancerMargin = 2;

        struct RigDraw { public int Frame; public float X, Y, ScaleX, ScaleY, Rotation, Alpha; }

        sealed class RigVariant
        {
            public int Frames, Dance, Out;
            // Where the rig's (0,0) sits inside each source frame, from its top left.
            public Vector2Int[] Origins;
            // From each rig frame until the next key: the frames drawn, back to front.
            public List<(int frame, RigDraw[] draws)> Keys;
        }

        sealed class DancerRigData
        {
            // Stand points in spawn order: centre, left, right, far left, far right.
            public int[] X;
            public int Y;
            public RigVariant[] Variants;
        }

        static DancerRigData ReadDancerRig()
        {
            string text = File.ReadAllText(DancerRigPath);
            float Number(string value) => float.Parse(value, CultureInfo.InvariantCulture);
            var heads = Regex.Matches(text, @"\[(\d+)\] = \{w=(\d+),h=(\d+),n=(\d+),dance=(\d+),out=(\d+),");
            var variants = new RigVariant[heads.Count];
            for (int i = 0; i < heads.Count; i++)
            {
                int end = i + 1 < heads.Count ? heads[i + 1].Index : text.Length;
                string body = text.Substring(heads[i].Index, end - heads[i].Index);
                int keysAt = body.IndexOf("keys={", StringComparison.Ordinal);
                var variant = new RigVariant
                {
                    Frames = int.Parse(heads[i].Groups[4].Value), Dance = int.Parse(heads[i].Groups[5].Value), Out = int.Parse(heads[i].Groups[6].Value),
                    Origins = Regex.Matches(body.Substring(0, keysAt), @"\{(-?\d+),(-?\d+)\}")
                        .Select(m => new Vector2Int(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))).ToArray(),
                    Keys = new List<(int, RigDraw[])>(),
                };
                foreach (Match key in Regex.Matches(body.Substring(keysAt), @"\{(\d+),\{((?:\{[-\d.,e]+\},?)+)\}\}"))
                {
                    var draws = Regex.Matches(key.Groups[2].Value, @"\{([-\d.,e]+)\}").Select(m =>
                    {
                        var v = m.Groups[1].Value.Split(',');
                        return new RigDraw { Frame = int.Parse(v[0]), X = Number(v[1]), Y = Number(v[2]), ScaleX = Number(v[3]), ScaleY = Number(v[4]), Rotation = Number(v[5]), Alpha = Number(v[6]) };
                    }).ToArray();
                    if (draws.Any(d => d.Rotation != 0)) throw new NotSupportedException("The dancer rig rotates a frame.");
                    // A key on the frame count only closes the timeline.
                    int frame = int.Parse(key.Groups[1].Value);
                    if (frame < variant.Frames) variant.Keys.Add((frame, draws));
                }
                if (int.Parse(heads[i].Groups[1].Value) != i || variant.Keys.Count == 0 || variant.Keys[0].frame != 0)
                    throw new InvalidDataException("Unexpected dancer rig variant " + i);
                variants[i] = variant;
            }
            return new DancerRigData
            {
                X = Regex.Match(text, @"pos = \{([\d,]+)\}").Groups[1].Value.Split(',').Select(int.Parse).ToArray(),
                Y = int.Parse(Regex.Match(text, @"posy = (\d+)").Groups[1].Value),
                Variants = variants,
            };
        }

        static string DancerFramePath(int variant, int frame) => Root + "Art/" + DancerArt + variant + "_loop/" + frame + ".png";

        // Redraws every variant's frames from the skin onto one canvas per variant, with the rig
        // origin at the same pixel in each, so the frames line up without per-frame offsets. The
        // canvas is the frames' shared opaque bounds around that origin; the origin becomes the
        // sprite pivot. The canvases are mostly empty, so the frames are drawn from an atlas that
        // keeps only each frame's opaque part (see DancerAtlas).
        [MenuItem("OurTaiko/Import Dancer Frames")]
        public static void ImportDancerFrames()
        {
            string source = DancerSkin + "Graphics/" + DancerArt;
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException(Path.GetFullPath(source));
            File.Copy(DancerSkin + "Scripts/background/dancer_data/dancer_0.lua", DancerRigPath, true);
            AssetDatabase.ImportAsset(DancerRigPath);
            var rig = ReadDancerRig();
            var pivots = new Vector2[rig.Variants.Length];
            for (int v = 0; v < rig.Variants.Length; v++)
            {
                var origins = rig.Variants[v].Origins;
                var frames = new (Color32[] pixels, int width, int height)[origins.Length];
                int left = 0, top = 0, right = 0, bottom = 0;
                for (int i = 0; i < origins.Length; i++)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    try
                    {
                        if (!texture.LoadImage(File.ReadAllBytes($"{source}{v}_loop/{i}.png"))) throw new InvalidDataException($"{v}_loop/{i}.png");
                        frames[i] = (texture.GetPixels32(), texture.width, texture.height);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(texture); }
                    var (pixels, width, height) = frames[i];
                    // Pixel rows run bottom up; the rig counts from the top.
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            if (pixels[(height - 1 - y) * width + x].a == 0) continue;
                            left = Math.Max(left, origins[i].x - x); right = Math.Max(right, x + 1 - origins[i].x);
                            top = Math.Max(top, origins[i].y - y); bottom = Math.Max(bottom, y + 1 - origins[i].y);
                        }
                }
                left += DancerMargin; top += DancerMargin; right += DancerMargin; bottom += DancerMargin;
                int canvasWidth = left + right, canvasHeight = top + bottom;
                pivots[v] = new Vector2((float)left / canvasWidth, 1 - (float)top / canvasHeight);
                Directory.CreateDirectory(Path.GetDirectoryName(DancerFramePath(v, 0)));
                for (int i = 0; i < origins.Length; i++)
                {
                    var (pixels, width, height) = frames[i];
                    var canvas = new Color32[canvasWidth * canvasHeight];
                    for (int y = 0; y < height; y++)
                    {
                        int row = top - origins[i].y + y;
                        if (row < 0 || row >= canvasHeight) continue;
                        for (int x = 0; x < width; x++)
                        {
                            int column = left - origins[i].x + x;
                            if (column < 0 || column >= canvasWidth) continue;
                            canvas[(canvasHeight - 1 - row) * canvasWidth + column] = pixels[(height - 1 - y) * width + x];
                        }
                    }
                    var output = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
                    try
                    {
                        output.SetPixels32(canvas);
                        byte[] png = output.EncodeToPNG();
                        string path = DancerFramePath(v, i);
                        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(png)) File.WriteAllBytes(path, png);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(output); }
                }
            }
            AssetDatabase.Refresh();
            for (int v = 0; v < rig.Variants.Length; v++)
                for (int i = 0; i < rig.Variants[v].Origins.Length; i++)
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(DancerFramePath(v, i));
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    bool current = importer.textureType == TextureImporterType.Sprite && settings.spriteMode == (int)SpriteImportMode.Single
                        && settings.spriteAlignment == (int)SpriteAlignment.Custom && settings.spritePivot == pivots[v]
                        && settings.spriteMeshType == SpriteMeshType.Tight && !settings.mipmapEnabled
                        && importer.textureCompression == TextureImporterCompression.Uncompressed;
                    if (current) continue;
                    importer.textureType = TextureImporterType.Sprite;
                    importer.ReadTextureSettings(settings);
                    settings.spriteMode = (int)SpriteImportMode.Single;
                    settings.spriteAlignment = (int)SpriteAlignment.Custom;
                    settings.spritePivot = pivots[v];
                    // The atlas trims a frame to its tight mesh; a full rect would pack the whole canvas.
                    settings.spriteMeshType = SpriteMeshType.Tight;
                    settings.mipmapEnabled = false;
                    settings.alphaIsTransparency = true;
                    settings.wrapMode = TextureWrapMode.Clamp;
                    settings.filterMode = FilterMode.Bilinear;
                    importer.SetTextureSettings(settings);
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
            DancerAtlas();
        }

        // Generated/Dancers/Dancer0.spriteatlasv2: every dancer frame, each trimmed to its opaque
        // part. A sprite keeps its canvas size and pivot, and the Images draw the trimmed part
        // where it sat on the canvas, so nothing that uses the frames changes.
        static void DancerAtlas()
        {
            if (!AssetDatabase.IsValidFolder(DancerPrefabFolder)) AssetDatabase.CreateFolder(Root + "Generated", "Dancers");
            if (AssetImporter.GetAtPath(DancerAtlasPath) == null)
            {
                var asset = new SpriteAtlasAsset();
                asset.Add(new[] { AssetDatabase.LoadAssetAtPath<UnityEngine.Object>((Root + "Art/" + DancerArt).TrimEnd('/')) });
                SpriteAtlasAsset.Save(asset, DancerAtlasPath);
                AssetDatabase.ImportAsset(DancerAtlasPath);
            }
            var importer = (SpriteAtlasImporter)AssetImporter.GetAtPath(DancerAtlasPath);
            // Images draw whole quads, so frames are packed as rectangles, upright.
            var packing = new SpriteAtlasPackingSettings { enableRotation = false, enableTightPacking = false, enableAlphaDilation = false, padding = 4, blockOffset = 1 };
            var texture = new SpriteAtlasTextureSettings { generateMipMaps = false, filterMode = FilterMode.Bilinear, sRGB = true, readable = false, anisoLevel = 1 };
            var platform = importer.GetPlatformSettings("DefaultTexturePlatform");
            bool current = importer.includeInBuild && importer.packingSettings.Equals(packing) && importer.textureSettings.Equals(texture)
                && platform.maxTextureSize == 2048 && platform.textureCompression == TextureImporterCompression.Uncompressed;
            if (current) return;
            importer.includeInBuild = true;
            importer.packingSettings = packing;
            importer.textureSettings = texture;
            platform.maxTextureSize = 2048;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformSettings(platform);
            importer.SaveAndReimport();
        }

        static Sprite[] DancerSprites(int variant, int count) => Enumerable.Range(0, count).Select(i =>
            AssetDatabase.LoadAssetAtPath<Sprite>(DancerFramePath(variant, i)) ?? throw new FileNotFoundException(DancerFramePath(variant, i))).ToArray();

        static string DancerLayer(int layer) => "Layer" + layer;

        // Dancer0_<variant>.anim: the rig timeline at one rig frame per clip frame. Each layer holds
        // its key until the next one, as the rig looks its frame up.
        static AnimationClip DancerClip(int index, RigVariant variant, Sprite[] sprites)
            => SaveClip("Dancer0_" + index, DancerTroupe.BeatFrames, false, clip =>
            {
                int layers = variant.Keys.Max(k => k.draws.Length);
                for (int layer = 0; layer < layers; layer++)
                {
                    string path = DancerLayer(layer);
                    var shown = new List<ObjectReferenceKeyframe>();
                    var curves = Enumerable.Range(0, 6).Select(_ => new List<(float, float)>()).ToArray();
                    void Key(int curve, float time, float value)
                    {
                        if (curves[curve].Count == 0 || curves[curve][curves[curve].Count - 1].Item2 != value) curves[curve].Add((time, value));
                    }
                    foreach (var (frame, draws) in variant.Keys)
                    {
                        float time = (float)frame / DancerTroupe.BeatFrames;
                        Key(0, time, layer < draws.Length ? 1 : 0);
                        if (layer >= draws.Length) continue;
                        var draw = draws[layer];
                        if (shown.Count == 0 || shown[shown.Count - 1].value != sprites[draw.Frame])
                            shown.Add(new ObjectReferenceKeyframe { time = time, value = sprites[draw.Frame] });
                        Key(1, time, draw.X); Key(2, time, -draw.Y);
                        Key(3, time, draw.ScaleX); Key(4, time, draw.ScaleY);
                        Key(5, time, draw.Alpha);
                    }
                    AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(path, typeof(Image), "m_Sprite"), shown.ToArray());
                    SteppedCurve(clip, path, typeof(Image), "m_Enabled", curves[0].ToArray());
                    SteppedCurve(clip, path, typeof(RectTransform), "m_AnchoredPosition.x", curves[1].ToArray());
                    SteppedCurve(clip, path, typeof(RectTransform), "m_AnchoredPosition.y", curves[2].ToArray());
                    SteppedCurve(clip, path, typeof(Transform), "m_LocalScale.x", curves[3].ToArray());
                    SteppedCurve(clip, path, typeof(Transform), "m_LocalScale.y", curves[4].ToArray());
                    SteppedCurve(clip, path, typeof(Image), "m_Color.a", curves[5].ToArray());
                }
            });

        // Generated/Dancers/Dancer0_<variant>.prefab: a zero-sized root on the stand point and one
        // Image per rig layer, sized to the variant's canvas with the rig origin as its pivot. An
        // existing prefab is updated in place; it is saved on the first frame of its dance.
        static GameObject DancerPrefab(int index, RigVariant variant, Sprite[] sprites, AnimationClip clip)
        {
            if (!AssetDatabase.IsValidFolder(DancerPrefabFolder)) AssetDatabase.CreateFolder(Root + "Generated", "Dancers");
            string path = $"{DancerPrefabFolder}/Dancer0_{index}.prefab";
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            var root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject("Dancer0_" + index, typeof(RectTransform));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero; rect.sizeDelta = Vector2.zero;
                var pose = variant.Keys.Last(k => k.frame <= variant.Dance).draws;
                int layers = variant.Keys.Max(k => k.draws.Length);
                for (int layer = 0; layer < layers; layer++)
                {
                    var child = (RectTransform)root.transform.Find(DancerLayer(layer));
                    if (child == null)
                    {
                        child = new GameObject(DancerLayer(layer), typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                        child.SetParent(root.transform, false);
                    }
                    child.SetSiblingIndex(layer);
                    var image = child.GetComponent<Image>();
                    image.raycastTarget = false;
                    image.enabled = layer < pose.Length;
                    var draw = layer < pose.Length ? pose[layer] : new RigDraw { Frame = pose[0].Frame, ScaleX = 1, ScaleY = 1, Alpha = 1 };
                    var sprite = sprites[draw.Frame];
                    image.sprite = sprite;
                    image.color = new Color(1, 1, 1, draw.Alpha);
                    child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
                    child.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
                    child.sizeDelta = sprite.rect.size;
                    child.anchoredPosition = new Vector2(draw.X, -draw.Y);
                    child.localScale = new Vector3(draw.ScaleX, draw.ScaleY, 1);
                }
                AttachClip(root, clip);
                var view = root.GetComponent<DancerView>();
                if (view == null) view = root.AddComponent<DancerView>();
                view.frames = variant.Frames; view.dance = variant.Dance; view.leave = variant.Out;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // Rebuilds the dancer clips and prefabs, then gives both play scenes their Dancers group
        // (behind the footer, as the skin draws it) where it is missing. A saved group keeps its
        // layout; the earlier single-variant Dancer1..5 and Dancer.anim are removed.
        [MenuItem("OurTaiko/Apply Dancers")]
        public static void ApplyDancers()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var rig = ReadDancerRig();
            var prefabs = rig.Variants.Select((variant, v) =>
            {
                var sprites = DancerSprites(v, variant.Origins.Length);
                return DancerPrefab(v, variant, sprites, DancerClip(v, variant, sprites));
            }).ToArray();
            foreach (string name in new[] { "SinglePlayScene", "PracticeScene" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                if (play.dancers != null) continue;
                var footer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true))
                    .Single(r => r.name == "Footer" && r.parent.name.StartsWith("Viewport"));
                foreach (var old in footer.parent.Cast<Transform>().Where(t => Regex.IsMatch(t.name, @"^Dancer\d$")).ToArray())
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                // Hops start below the screen; the group clips them to the design area.
                var group = Rect("Dancers", footer.parent, 0, 0, 1920, 1080);
                group.SetSiblingIndex(footer.GetSiblingIndex());
                group.gameObject.AddComponent<RectMask2D>();
                var view = group.gameObject.AddComponent<DancerTroupeView>();
                view.slots = new RectTransform[rig.X.Length];
                view.dancers = new DancerView[rig.X.Length];
                // Far dancers first, so the centre one ends up in front.
                for (int i = rig.X.Length - 1; i >= 0; i--)
                {
                    view.slots[i] = Rect("Slot" + (i + 1), group, rig.X[i], rig.Y, 0, 0);
                    var dancer = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i % prefabs.Length], view.slots[i]);
                    view.dancers[i] = dancer.GetComponent<DancerView>();
                }
                play.dancers = view;
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.DeleteAsset(ClipFolder + "Dancer.anim");
            AssetDatabase.SaveAssets();
        }
    }
}
