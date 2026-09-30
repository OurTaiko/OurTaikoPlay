using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string SwitcherPrefab = Root + "Resources/SceneSwitcher.prefab";
        const string DefaultScenePath = "Assets/Scenes/Test_DefaultScene.unity";
        const string LegacyMenuPath = "Assets/Scenes/SceneSwitcher.unity";

        // Targeted migration: keep the authored menu and all gameplay scene objects intact.
        [MenuItem("OurTaiko/Migrate Global SceneSwitcher")]
        public static void MigrateGlobalSceneSwitcher()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before migrating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits before migrating.");

            CreateSceneSwitcherPrefab();
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LegacyMenuPath) != null)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DefaultScenePath) != null)
                    throw new InvalidOperationException("Both the old and new entry scene exist; cannot rename safely.");
                string error = AssetDatabase.MoveAsset(LegacyMenuPath, DefaultScenePath);
                if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            }
            var scene = EditorSceneManager.OpenScene(DefaultScenePath);
            foreach (var root in scene.GetRootGameObjects())
            {
                var legacy = root.GetComponent<SceneSwitcher>();
                if (legacy == null) continue;
                if (root.GetComponents<Component>().Length == 2 && root.transform.childCount == 0)
                    UnityEngine.Object.DestroyImmediate(root);
                else UnityEngine.Object.DestroyImmediate(legacy);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            var buildScenes = EditorBuildSettings.scenes;
            foreach (var entry in buildScenes)
                if (entry.path == LegacyMenuPath) entry.path = DefaultScenePath;
            EditorBuildSettings.scenes = buildScenes;
            Debug.Log("OurTaiko: Test_DefaultScene now uses the global Resources/SceneSwitcher prefab.");
        }

        static void CreateSceneSwitcherPrefab()
        {
            // Re-running the migration preserves subsequent Inspector edits to the prefab.
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SwitcherPrefab) != null) return;
            if (!AssetDatabase.IsValidFolder(Root + "Resources")) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "Resources");
            var control = new GameObject("SceneSwitcher", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(SceneSwitcher));
            try
            {
                var canvas = control.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = short.MaxValue;
                var scaler = control.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;

                var cover = new GameObject("Transition", typeof(RectTransform), typeof(CanvasGroup));
                var rect = (RectTransform)cover.transform;
                rect.SetParent(control.transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var group = cover.GetComponent<CanvasGroup>();
                group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;

                var shade = new GameObject("Cover", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                var shadeRect = (RectTransform)shade.transform;
                shadeRect.SetParent(rect, false);
                shadeRect.anchorMin = Vector2.zero; shadeRect.anchorMax = Vector2.one;
                shadeRect.offsetMin = shadeRect.offsetMax = Vector2.zero;
                shade.GetComponent<UnityEngine.UI.Image>().color = new Color32(28, 29, 32, 255);
                shade.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;

                var viewport = new GameObject("Viewport1920x1080", typeof(RectTransform)).GetComponent<RectTransform>();
                viewport.SetParent(rect, false);
                viewport.anchorMin = viewport.anchorMax = viewport.pivot = Vector2.one * 0.5f;
                viewport.sizeDelta = new Vector2(1920, 1080);
                var label = new GameObject("LoadingText", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.rectTransform.SetParent(viewport, false);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 0.1f);
                label.rectTransform.sizeDelta = new Vector2(1600, 80);
                label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "Generated/Nijiiro SDF.asset");
                label.fontSize = 36; label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white; label.raycastTarget = false; label.text = "";
                var serialized = new SerializedObject(control.GetComponent<SceneSwitcher>());
                serialized.FindProperty("transition").objectReferenceValue = group;
                serialized.FindProperty("loadingText").objectReferenceValue = label;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                cover.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(control, SwitcherPrefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(control); }
        }
    }
}
