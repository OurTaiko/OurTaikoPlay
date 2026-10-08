using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string EndingArt = Root + "Art/game/ending/";
        const string EndingPrefab = Root + "Generated/Ending.prefab";
        static readonly string[] EndingKinds = { "fail", "clear", "fullcombo", "donderful" };
        static readonly string[] EndingNames = { "EndingFail", "EndingClear", "EndingFullCombo", "EndingDonderful" };

        [Serializable] public sealed class EndingShape { public int id; public float w, h, ox, oy; public string sprite; }
        [Serializable] public sealed class EndingRow { public float[] values; }
        [Serializable] public sealed class EndingFrame { public EndingRow[] rows; }
        [Serializable] public sealed class EndingTimeline
        {
            public string source;
            public int fps;
            public EndingShape[] shapes;
            public EndingFrame[] frames;
        }

        public static EndingTimeline ReadEnding(string kind) => JsonUtility.FromJson<EndingTimeline>(
            File.ReadAllText(Root + "Editor/EndingTimelines/" + kind + ".json"));

        [MenuItem("OurTaiko/Apply Ending Animations")]
        public static void ApplyEndingAnimations()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (var path in Directory.GetFiles(EndingArt, "*.png"))
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    var settings = new TextureImporterSettings();
                    importer.ReadTextureSettings(settings);
                    if (importer.textureType == TextureImporterType.Sprite
                        && importer.spriteImportMode == SpriteImportMode.Single
                        && settings.spriteMeshType == SpriteMeshType.FullRect
                        && importer.npotScale == TextureImporterNPOTScale.None
                        && importer.textureCompression == TextureImporterCompression.CompressedHQ) continue;
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    // Exported w/h and origins include transparent margins. Automatic trimming
                    // stretches the remaining pixels when the clip restores the original size.
                    importer.ReadTextureSettings(settings);
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.spritePixelsPerUnit = 100;
                    importer.mipmapEnabled = false;
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.maxTextureSize = 4096;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
                var data = EndingKinds.Select(ReadEnding).ToArray();
                int slots = data.Max(d => d.frames.Max(f => f.rows.Length));
                var clips = data.Select((d, i) => BuildEndingClip(EndingNames[i], d, slots)).ToArray();
                var root = new GameObject("Ending", typeof(RectTransform));
                try
                {
                    var view = root.AddComponent<EndingView>();
                    var rect = (RectTransform)root.transform;
                    // Reference movie origin (1209,109.5) relative to the 1920x264 NoteLane.
                    rect.anchorMin = rect.anchorMax = new Vector2(1209f / 1920, 1 - 109.5f / 264);
                    rect.pivot = Vector2.one * .5f;
                    rect.sizeDelta = Vector2.zero;
                    rect.anchoredPosition = Vector2.zero;
                    for (int i = 0; i < slots; i++)
                    {
                        var image = SkinUi.Image("Layer" + i, rect, null);
                        image.rectTransform.anchorMin = image.rectTransform.anchorMax = Vector2.one * .5f;
                        image.raycastTarget = false;
                        image.enabled = false;
                    }
                    AttachClip(root, clips[0]);
                    view.fail = clips[0]; view.clear = clips[1]; view.fullCombo = clips[2]; view.donderful = clips[3];
                    view.audioSource = root.AddComponent<AudioSource>();
                    view.audioSource.playOnAwake = false;
                    view.failSound = Clip("game/fail"); view.clearSound = Clip("game/clear");
                    view.fullComboSound = Clip("game/full_combo"); view.donderfulSound = Clip("game/donderful_combo");
                    root.SetActive(false);
                    PrefabUtility.SaveAsPrefabAsset(root, EndingPrefab);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                if (play.ending == null)
                {
                    var lane = play.noteLayer.parent.parent;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(EndingPrefab), lane);
                    instance.name = "Ending";
                    play.ending = instance.GetComponent<EndingView>();
                    EditorUtility.SetDirty(play);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static AnimationClip BuildEndingClip(string name, EndingTimeline data, int slots)
        {
            var shapes = data.shapes.ToDictionary(s => s.id);
            var sprites = data.shapes.ToDictionary(s => s.id, s => Sprite("game/ending/" + s.sprite)
                ?? throw new FileNotFoundException(s.sprite));
            var additive = AdditiveUiMaterial();
            return SaveClip(name, data.fps, false, clip =>
            {
                for (int slot = 0; slot < slots; slot++)
                {
                    string path = "Layer" + slot;
                    var curves = new Dictionary<string, List<(float, float)>>();
                    var spriteKeys = new List<ObjectReferenceKeyframe>();
                    var materialKeys = new List<ObjectReferenceKeyframe>();
                    void Key(string property, float time, float value)
                    {
                        if (!curves.TryGetValue(property, out var keys)) curves[property] = keys = new List<(float, float)>();
                        if (keys.Count == 0 || keys[keys.Count - 1].Item2 != value) keys.Add((time, value));
                    }
                    void ObjectKey(List<ObjectReferenceKeyframe> keys, float time, UnityEngine.Object value)
                    {
                        if (keys.Count == 0 || keys[keys.Count - 1].value != value)
                            keys.Add(new ObjectReferenceKeyframe { time = time, value = value });
                    }
                    for (int frame = 0; frame <= data.frames.Length; frame++)
                    {
                        float t = (float)frame / data.fps;
                        var rows = data.frames[Math.Min(frame, data.frames.Length - 1)].rows;
                        bool visible = slot < rows.Length;
                        Key("m_Enabled", t, visible ? 1 : 0);
                        float[] r = visible ? rows[slot].values : null;
                        var shape = visible ? shapes[(int)r[0]] : null;
                        ObjectKey(spriteKeys, t, visible ? sprites[shape.id] : null);
                        ObjectKey(materialKeys, t, visible && r.Length > 7 && r[7] == 8 ? additive : null);
                        Key("m_Color.a", t, visible ? r[6] : 0);
                        Key("m_AnchoredPosition.x", t, visible ? r[1] : 0);
                        Key("m_AnchoredPosition.y", t, visible ? -r[2] : 0);
                        Key("m_SizeDelta.x", t, visible ? shape.w : 0);
                        Key("m_SizeDelta.y", t, visible ? shape.h : 0);
                        Key("m_Pivot.x", t, visible ? shape.ox / shape.w : .5f);
                        Key("m_Pivot.y", t, visible ? 1 - shape.oy / shape.h : .5f);
                        Key("m_LocalScale.x", t, visible ? r[3] : 1);
                        Key("m_LocalScale.y", t, visible ? r[4] : 1);
                        Key("localEulerAnglesRaw.z", t, visible ? -r[5] : 0);
                    }
                    // Pin the exact duration, including the final 60 Hz frame, without looping.
                    curves["m_Enabled"].Add(((float)data.frames.Length / data.fps, slot < data.frames.Last().rows.Length ? 1 : 0));
                    foreach (var curve in curves)
                        SteppedCurve(clip, path, curve.Key == "m_Enabled" || curve.Key == "m_Color.a" ? typeof(Image) : typeof(RectTransform), curve.Key, curve.Value.ToArray());
                    AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(path, typeof(Image), "m_Sprite"), spriteKeys.ToArray());
                    AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(path, typeof(Image), "m_Material"), materialKeys.ToArray());
                }
            });
        }
    }
}
