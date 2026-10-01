using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string TouchDrumPath = "global/overlay/touch_drum";

        // Replaces SinglePlayScene's four rectangular touch buttons with the original's half-drum overlay.
        [MenuItem("OurTaiko/Apply Touch Drum")]
        public static void ApplyTouchDrum()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing the touch drum.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            ConfigureTouchDrum(UnityEngine.Object.FindFirstObjectByType<PlayScene>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }

        static void ImportTouchDrumArt()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "Art/" + TouchDrumPath + ".png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }

        static void ConfigureTouchDrum(PlayScene play)
        {
            ImportTouchDrumArt();
            var viewport = play.pausePanel.transform.parent;
            for (int i = 0; i < 4; i++)
            {
                var old = viewport.Find("TouchPad" + i);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            // Full design area: the hit zones are fractions of it, so they follow the Canvas scale.
            var zone = viewport.Find("TouchDrum") as RectTransform;
            if (zone == null) zone = Rect("TouchDrum", viewport, 0, 0, 0, 0);
            zone.anchorMin = Vector2.zero; zone.anchorMax = Vector2.one; zone.pivot = new Vector2(0.5f, 0.5f);
            zone.offsetMin = zone.offsetMax = Vector2.zero;
            // Over the gameplay like the original's global overlay, under the pause / result panels.
            zone.SetSiblingIndex(play.pausePanel.transform.GetSiblingIndex());
            var pad = zone.GetComponent<DrumPad>();
            if (pad == null) pad = zone.gameObject.AddComponent<DrumPad>();
            var image = PlacePicture(zone, "Drum", TouchDrumPath, 0, 0);
            var drum = image.rectTransform;
            drum.anchorMin = Vector2.zero; drum.anchorMax = Vector2.one;
            // The original squeezes about the centre and then moves down by h/2*(1-scale): a bottom pivot.
            drum.pivot = new Vector2(0.5f, 0);
            drum.offsetMin = drum.offsetMax = Vector2.zero;
            drum.localScale = Vector3.one;
            image.color = new Color(1, 1, 1, 0.5f);
            image.raycastTarget = false;
            pad.drum = drum;
        }
    }
}
