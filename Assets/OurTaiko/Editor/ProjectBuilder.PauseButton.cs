using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string PauseIconPath = Root + "Art/ui/material_symbols/pause_circle.png";

        [MenuItem("OurTaiko/Apply Circular Pause Button")]
        public static void ApplyCircularPauseButton()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene first.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            var button = play.pauseButton;
            var rect = (RectTransform)button.transform;
            var fps = rect.parent.Find("FpsPanel") as RectTransform;
            if (fps == null) throw new InvalidOperationException("Missing saved FpsPanel.");
            var importer = (TextureImporter)AssetImporter.GetAtPath(PauseIconPath);
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.SaveAndReimport();
            }
            ImportBoardArt(PauseIconPath, Vector4.zero);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PauseIconPath);
            if (button.image.sprite == sprite && button.GetComponent<CircularHitArea>() != null) return;

            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(48, 48);
            rect.anchoredPosition = new Vector2(24, 0);
            // Same 1920x1080 viewport as the FPS and counter: no physical screen pixel offsets.
            fps.anchoredPosition = new Vector2(82, fps.anchoredPosition.y);
            foreach (var label in button.GetComponentsInChildren<TMPro.TMP_Text>(true))
                UnityEngine.Object.DestroyImmediate(label.gameObject);
            button.image.sprite = sprite;
            button.image.type = UnityEngine.UI.Image.Type.Simple;
            button.image.color = Color.white;
            button.image.preserveAspect = true;
            button.image.raycastTarget = true;
            button.targetGraphic = button.image;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
            colors.highlightedColor = new Color(1, .86f, .55f);
            colors.pressedColor = new Color(1, .65f, .2f);
            button.colors = colors;
            var outline = button.GetComponent<UnityEngine.UI.Outline>() ?? button.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.25f, -1.25f);
            if (button.GetComponent<CircularHitArea>() == null) button.gameObject.AddComponent<CircularHitArea>();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
        }
    }
}
