using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string PresentedPath = "Assets/Scenes/Presented.unity";

        // Creates the Presented title card only when missing (an existing layout is kept) and makes
        // it the first scene of Build Settings, before Entry. Repeated runs change nothing.
        [MenuItem("OurTaiko/Create Presented Scene")]
        public static void CreatePresentedScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            if (!File.Exists(PresentedPath))
            {
                BuildPresentedScene(NewStage());
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), PresentedPath);
            }
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Count == 0 || scenes[0].path != PresentedPath)
            {
                scenes.RemoveAll(s => s.path == PresentedPath);
                scenes.Insert(0, new EditorBuildSettingsScene(PresentedPath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: Presented is the first scene.");
        }

        static void BuildPresentedScene(RectTransform viewport)
        {
            var uiFont = UiFont();
            var plain = uiFont.material;
            var controller = new GameObject("Presented").AddComponent<PresentedScene>();

            var stage = Rect("Stage", viewport, 0, 0, 1920, 1080);
            var background = SkinUi.Image("Background", stage, null, 1920, 1080);
            Stretch(background.rectTransform);
            background.color = Color.black;
            // The whole card takes the tap that skips it.
            background.raycastTarget = true;
            controller.touch = background.gameObject.AddComponent<PointerRelay>();

            var card = Rect("Card", stage, 0, 380, 1920, 320);
            controller.card = card.gameObject.AddComponent<CanvasGroup>();
            controller.card.blocksRaycasts = false;
            var by = SettingText(card, "PresentedBy", uiFont, plain, new Color32(200, 200, 200, 255), 44, TextAlignmentOptions.Center, 1920);
            by.text = "Presented by";
            by.characterSpacing = 12;
            by.rectTransform.TopLeft(0, 0);
            var name = SettingText(card, "Name", uiFont, plain, Color.white, 150, TextAlignmentOptions.Center, 1920);
            name.text = "OurTaiko";
            name.rectTransform.TopLeft(0, 90);

            controller.progress = SettingText(stage, "Progress", uiFont, plain, new Color32(170, 170, 170, 255), 30, TextAlignmentOptions.Center, 1600);
            controller.progress.rectTransform.TopLeft(160, 930);
            controller.hint = SettingText(stage, "SkipHint", uiFont, plain, new Color32(120, 120, 120, 255), 26, TextAlignmentOptions.MidlineRight, 600);
            controller.hint.text = "Space / Tap: Skip";
            controller.hint.rectTransform.TopLeft(1920 - 600 - 60, 1000);
        }
    }
}
