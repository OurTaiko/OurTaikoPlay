using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        // Six type rows (General, Play, Display, Sound, OurTaiko Accounts, Return) above the footer.
        const float AccountTypeScale = 0.82f, AccountTypePitch = 116;

        // Adds the popup's text field for typed settings and makes room for the OurTaiko Accounts
        // type. Repeated runs change nothing.
        [MenuItem("OurTaiko/Apply Account Settings")]
        public static void ApplyAccountSettings()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var scene = EditorSceneManager.OpenScene(SettingPath);
            var controller = UnityEngine.Object.FindFirstObjectByType<GlobalSettingScene>();
            if (controller == null || controller.view == null) throw new InvalidOperationException("Settings scene is missing its saved view.");
            if (ConfigureAccountSettings(controller.view))
            {
                EditorUtility.SetDirty(controller.view);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            // Batch mode starts on an untitled scene, which cannot be restored.
            if (setup.Length > 0 && Array.TrueForAll(setup, s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
        }

        static bool ConfigureAccountSettings(GlobalSettingView view)
        {
            if (view.textField != null) return false;
            var uiFont = UiFont();
            var white = UiOutlineMaterial();
            var detail = (RectTransform)view.detail.transform;
            // In the choice buttons' place, on the focused item plate like ServerLogin's fields.
            const float width = 900, height = 110;
            var rect = Rect("TextField", detail, (detail.sizeDelta.x - width) / 2, 220, width, height);
            var box = rect.gameObject.AddComponent<Image>();
            box.sprite = SettingSprite("background/title_highlight"); box.type = Image.Type.Sliced; box.raycastTarget = true;
            var area = Rect("Text Area", rect, 0, 0, 0, 0);
            Stretch(area, 36, 36);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = InputText(area, "Placeholder", uiFont, white, new Color(1, 1, 1, 0.5f));
            var text = InputText(area, "Text", uiFont, white, Color.white);

            var field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;
            field.fontAsset = uiFont;
            field.pointSize = 40;
            field.targetGraphic = box;
            field.transition = Selectable.Transition.None;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 128;
            field.customCaretColor = true;
            field.caretColor = Color.white;
            field.caretWidth = 3;
            field.selectionColor = new Color(1, 0.85f, 0.3f, 0.5f);
            rect.gameObject.SetActive(false);
            view.textField = field;

            var row = view.typeRows[0];
            row.root.localScale = new Vector3(AccountTypeScale, AccountTypeScale, 1);
            row.label.enableAutoSizing = true; row.label.fontSizeMin = 30; row.label.fontSizeMax = 56;
            row.label.margin = new Vector4(30, 0, 30, 0);
            view.typePitch = AccountTypePitch;
            view.typeSwipe.step = AccountTypePitch * 0.8f;
            return true;
        }
    }
}
