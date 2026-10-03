using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Sound Settings")]
        public static void ApplySoundSettings()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i=0; i<EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(SettingPath);
            var controller = UnityEngine.Object.FindFirstObjectByType<GlobalSettingScene>();
            if (controller == null || controller.view == null) throw new InvalidOperationException("Settings scene is missing its saved view.");
            if (ConfigureSoundSettings(controller))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            EditorSceneManager.RestoreSceneManagerSetup(setup);
        }
        static bool ConfigureSoundSettings(GlobalSettingScene controller)
        {
            bool changed = false;
            var view = controller.view;
            if (view.previousItems == null)
            {
                view.previousItems = SoundButton(view.itemSwipe.transform, "PreviousItems", "‹", 20, 650, 130, 64, view.choiceOff);
                view.nextItems = SoundButton(view.itemSwipe.transform, "NextItems", "›", 1060, 650, 130, 64, view.choiceOff);
                view.itemPage = SoundLabel(view.itemSwipe.transform, "ItemPage", 170, 650, 840, 64, 30);
                var detail = (RectTransform)view.detail.transform;
                detail.sizeDelta = new Vector2(detail.sizeDelta.x, 460);
                view.previousChoice = SoundButton(detail, "PreviousChoice", "‹", 430, 355, 130, 64, view.choiceOff);
                view.nextChoice = SoundButton(detail, "NextChoice", "›", 730, 355, 130, 64, view.choiceOff);
                view.outputStatus = SoundLabel(view.transform, "OutputStatus", 710, 72, 1170, 70, 30);
                view.outputStatus.alignment = TextAlignmentOptions.MidlineLeft;
                view.visibleItems = 4;
                foreach (var click in new[] { view.previousItems, view.nextItems, view.previousChoice, view.nextChoice }) click.gameObject.SetActive(false);
                changed = true;
            }
            var voice = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/entry/select_mode.ogg");
            if (controller.previewVoice != voice)
            {
                controller.previewVoice = voice;
                changed = true;
            }
            // Reserve the cursor's authored width instead of letting it fall outside the stage.
            var row = view.itemRows[0];
            float width = ((RectTransform)view.itemSwipe.transform).rect.width - 2 * row.root.anchoredPosition.x - view.cursor.rect.width - view.cursorGap;
            if (row.root.sizeDelta.x > width + .01f)
            {
                row.root.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                row.value.rectTransform.offsetMin = new Vector2(width - 280, 0);
                changed = true;
            }
            return changed;
        }
        static PointerRelay SoundButton(Transform parent, string name, string text, float x, float y, float width, float height, Sprite sprite)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.type = UnityEngine.UI.Image.Type.Sliced;
            var label = SoundLabel(rect, "Label", 0, 0, width, height, 42); label.text = text;
            return rect.gameObject.AddComponent<PointerRelay>();
        }
        static TMP_Text SoundLabel(Transform parent, string name, float x, float y, float width, float height, float size)
        {
            var label = SkinUi.Text(name, parent, size);
            label.rectTransform.sizeDelta = new Vector2(width, height);
            label.rectTransform.TopLeft(x,y); label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; label.text = "";
            return label;
        }
    }
}
