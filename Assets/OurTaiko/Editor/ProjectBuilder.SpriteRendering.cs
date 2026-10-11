using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const int PlayHudOrder = 100;
        const int PlayFeedbackOrder = 110;
        const int PlayRankOrder = 120;
        const int PlayPauseOrder = 200;

        // Run after authoring gameplay UI. Existing serialized references and local animation paths
        // are retained; the new full-size containers preserve their children's design coordinates.
        [MenuItem("OurTaiko/Apply Sprite Note Rendering")]
        public static void ApplySpriteNoteRendering()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                const string materialPath = Root + "Resources/LaneSprite.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(Root + "Resources/LaneSprite.shader"));
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                foreach (string name in new[] { "SinglePlayScene", "PracticeScene" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                    var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                    UseRectangularNoteMeshes(play);
                    var canvas = play.noteLayer.GetComponentInParent<Canvas>().rootCanvas;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = Camera.main;
                    canvas.planeDistance = 1;
                    canvas.sortingOrder = -100;
                    var viewport = canvas.transform.Find("Viewport1920x1080");
                    var lane = play.noteLayer.parent.parent;
                    Layer(viewport.Find("Dancers"), -90);
                    Layer(viewport.Find("Footer"), -80);
                    Layer(viewport.Find("SongTitle"), -70);
                    Layer(viewport.Find("SongSubtitle"), -70);
                    Layer(lane, -50);

                    var foreground = Group(lane, "LaneForeground");
                    // Everything after the text clip originally painted over notes and their text.
                    int clipIndex = play.mojiLayer.parent.GetSiblingIndex();
                    foreach (var child in lane.Cast<Transform>().ToArray())
                        if (child != foreground && child.GetSiblingIndex() > clipIndex)
                            child.SetParent(foreground, false);
                    Layer(foreground, 50);

                    var hud = Group(viewport, "Hud");
                    int laneIndex = lane.GetSiblingIndex();
                    foreach (var child in viewport.Cast<Transform>().ToArray())
                        if (child != hud && child.GetSiblingIndex() > laneIndex)
                            child.SetParent(hud, false);
                    Layer(hud, PlayHudOrder);
                    Layer(play.pausePanel.transform, PlayPauseOrder);
                    // Global transition canvases remain ScreenSpaceOverlay, above this camera.
                    foreach (var nested in foreground.GetComponentsInChildren<Canvas>(true))
                    {
                        if (nested == foreground.GetComponent<Canvas>()) continue;
                        nested.overrideSorting = false;
                    }
                    // Originally these canvases used orders 1/1/2 above the root HUD at 0.
                    // Preserve that relation after splitting the HUD, with pause above all three.
                    Layer(play.scoreCounter.additionTemplate.transform, PlayFeedbackOrder);
                    Layer(play.scoreCounter.scoreRank.transform, PlayRankOrder);
                    Layer(play.timingFeedback.transform, PlayFeedbackOrder);
                    SpritePass(play.barLayer, -1, material);
                    SpritePass(play.noteLayer, 0, material);
                    SpritePass(play.mojiLayer, 10, material);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static RectTransform PlayForeground(PlayScene play)
        {
            var lane = (RectTransform)play.noteLayer.parent.parent;
            return lane.Find("LaneForeground") as RectTransform ?? lane;
        }
        static Transform PlayHud(Transform viewport) => viewport.Find("Hud") ?? viewport;

        static RectTransform Group(Transform parent, string name)
        {
            var found = parent.Find(name) as RectTransform;
            if (found != null) return found;
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.pivot = ((RectTransform)parent).pivot;
            return rect;
        }

        static void UseRectangularNoteMeshes(PlayScene play)
        {
            var sprites = play.noteSprites.Concat(play.alternateNoteSprites).Concat(play.rollBodySprites)
                .Concat(play.rollTailSprites).Concat(play.mojiSprites).Concat(new[] { play.balloonTailSprite, play.mojiRollSprite });
            foreach (string path in sprites.Where(s => s != null).Select(AssetDatabase.GetAssetPath).Distinct())
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteMeshType == SpriteMeshType.FullRect) continue;
                // Image previously sampled a rectangle; tight sprite meshes triangulate the crop
                // differently and can cut translucent padding or change point-sampling at joins.
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }
        static void Layer(Transform root, int order)
        {
            if (root == null) return;
            var canvas = root.GetComponent<Canvas>();
            if (canvas == null) canvas = root.gameObject.AddComponent<Canvas>();
            // The Canvas setter can ignore overrideSorting while its GameObject is inactive
            // (the pause menu is saved hidden). Persist the flag through the Editor serializer.
            var serialized = new SerializedObject(canvas);
            serialized.FindProperty("m_OverrideSorting").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            canvas.sortingOrder = order;
            if (root.GetComponentsInChildren<UnityEngine.UI.Selectable>(true).Length > 0
                && root.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }
        static void SpritePass(RectTransform root, int order, Material material)
        {
            var layer = root.GetComponent<LaneSpriteLayer>();
            if (layer == null) layer = root.gameObject.AddComponent<LaneSpriteLayer>();
            layer.sourceMaterial = material;
            layer.clipRect = (RectTransform)root.parent;
            root.GetComponent<SortingGroup>().sortingOrder = order;
        }
    }
}
