using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Balloon Overlay Order")]
        public static void ApplyBalloonOverlayOrder()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in new[] { "Assets/Scenes/SinglePlayScene.unity", "Assets/Scenes/PracticeScene.unity" })
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    PlaceBalloonOverlay(UnityEngine.Object.FindFirstObjectByType<PlayScene>());
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static void PlaceBalloonOverlay(PlayScene play)
        {
            var lane = (RectTransform)play.noteLayer.parent.parent;
            var rig = (RectTransform)play.balloonCounter.transform;
            // Match the lane's coordinate space without inheriting its lower draw order.
            rig.SetParent(lane.parent, false);
            rig.anchorMin = lane.anchorMin;
            rig.anchorMax = lane.anchorMax;
            rig.pivot = lane.pivot;
            rig.sizeDelta = lane.sizeDelta;
            rig.anchoredPosition3D = lane.anchoredPosition3D;
            rig.localScale = lane.localScale;
            rig.localRotation = lane.localRotation;
            // Above the gauge, flying notes and other gameplay art; below controls and menus.
            PlaceBefore(rig, play.pauseButton.transform);
        }

        [MenuItem("OurTaiko/Apply Nijiiro Balloon Counter")]
        public static void ApplyBalloonCounter()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the balloon counter.");
            foreach (var path in Directory.GetFiles(Root + "Art/game/balloon", "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                // Sheets cut into frames (Multiple) keep their sub-sprites.
                if (importer.spriteImportMode != SpriteImportMode.Multiple) importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            ConfigureBalloonCounter(UnityEngine.Object.FindFirstObjectByType<PlayScene>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void ConfigureBalloonCounter(PlayScene play)
        {
            var lane = play.noteLayer.parent.parent;
            var rig = play.balloonCounter != null ? (RectTransform)play.balloonCounter.transform
                : lane.Find("BalloonCounter") as RectTransform;
            if (rig == null) rig = Rect("BalloonCounter", lane, 0, 0, 0, 0);
            var view = rig.GetComponent<BalloonCounterView>();
            if (view == null) view = rig.gameObject.AddComponent<BalloonCounterView>();
            view.visuals = rig.GetComponent<CanvasGroup>();
            if (view.visuals == null) view.visuals = rig.gameObject.AddComponent<CanvasGroup>();
            view.visuals.interactable = view.visuals.blocksRaycasts = false;
            view.bodyFrames = Enumerable.Range(0, 8).Select(i => Slice("BalloonInflation" + i,
                "game/balloon/pop", i % 3 * 384, i / 3 * 360, 384, 360)).ToArray();
            view.digitSprites = SharedCounterDigits();
            view.digitSize = new Vector2(77, 90);
            // Nijiiro's native 1920x1080 layout, scaled together by the Canvas.
            view.body = PlacePicture(rig, "Body", "game/balloon/pop", 645, -81, 384, 360);
            view.body.sprite = view.bodyFrames[0];
            view.bubble = PlacePicture(rig, "Bubble", "game/balloon/bubble", 589, -253);
            view.number = rig.Find("Number") as RectTransform;
            if (view.number == null) view.number = Rect("Number", rig, 723, -180, 0, 0);
            AttachBalloonClips(view);
            view.ResetDisplay();
            play.balloonCounter = view;
            PlaceBalloonOverlay(play);
            play.balloonPop = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/balloon_pop.ogg");
            play.balloonTailSprite = Slice("BalloonTail", "game/notes/notes_atlas", 0, 1928, 192, 192);
            EditorUtility.SetDirty(play);
        }
    }
}
