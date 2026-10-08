using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string DrumrollCounterArt = "game/drumroll_counter/";

        // One high-resolution sheet serves the fan and the smaller balloon counter.
        static Sprite[] SharedCounterDigits()
        {
            string path = Root + "Art/" + DrumrollCounterArt + "counter.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            // Discard Unity's automatic tight cuts on first import; retain authored IDs on reruns.
            if (!AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Any(sprite => sprite.name == "CounterDigit0"))
                importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.spritePixelsPerUnit = 100;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return SliceSheet(DrumrollCounterArt + "counter", Enumerable.Range(0, 10)
                .Select(i => ("CounterDigit" + i, i * 96, 0, 96, 112)).ToArray());
        }

        [MenuItem("OurTaiko/Apply Nijiiro Drumroll Counter")]
        public static void ApplyDrumrollCounter()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            // The fan keeps its original transparent margins.
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/" + DrumrollCounterArt + "bubble.png");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
            var digits = SharedCounterDigits();
            // Nijiiro animation IDs 8/9: stretch on every hit, hold 2532 ms, fade for 166 ms.
            var clip = SaveClip("DrumrollCounter", 1000, false, animation =>
            {
                TextStretchKeys(animation);
                LinearCurve(animation, "", typeof(CanvasGroup), "m_Alpha", (0, 1), (2.532f, 1), (2.698f, 0));
            });
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in new[] { "Assets/Scenes/SinglePlayScene.unity", "Assets/Scenes/PracticeScene.unity" })
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                    var lane = play.noteLayer.parent.parent;
                    var rig = lane.Find("DrumrollCounter") as RectTransform;
                    bool created = rig == null;
                    if (created) rig = Rect("DrumrollCounter", lane, 0, 0, 0, 0);
                    rig.anchorMin = Vector2.zero; rig.anchorMax = Vector2.one;
                    rig.offsetMin = rig.offsetMax = Vector2.zero;
                    PlaceBefore(rig, play.balloonCounter.transform);
                    var view = rig.GetComponent<DrumrollCounterView>();
                    if (view == null) view = rig.gameObject.AddComponent<DrumrollCounterView>();
                    view.visuals = rig.GetComponent<CanvasGroup>();
                    if (view.visuals == null) view.visuals = rig.gameObject.AddComponent<CanvasGroup>();
                    view.visuals.interactable = view.visuals.blocksRaycasts = false;
                    // Original skin coordinates, relative to the lane on the 1920 x 1080 stage.
                    view.bubble = rig.Find("Bubble")?.GetComponent<UnityEngine.UI.Image>();
                    if (view.bubble == null) view.bubble = Picture(rig, "Bubble", DrumrollCounterArt + "bubble", 296, -271);
                    view.bubble.sprite = Sprite(DrumrollCounterArt + "bubble");
                    view.bubble.rectTransform.sizeDelta = view.bubble.sprite.rect.size;
                    view.number = rig.Find("Number") as RectTransform;
                    if (view.number == null) view.number = Rect("Number", rig, 520, -215, 0, 0);
                    view.digitSprites = digits;
                    view.digitSize = new Vector2(96, 112);
                    play.balloonCounter.digitSprites = digits;
                    play.balloonCounter.digitSize = new Vector2(77, 90);
                    EditorUtility.SetDirty(play.balloonCounter);
                    AttachClip(rig.gameObject, clip);
                    view.ResetDisplay();
                    play.drumrollCounter = view;
                    EditorUtility.SetDirty(play);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
    }
}
