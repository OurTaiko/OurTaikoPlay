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
        const string ServerLoginPath = "Assets/Scenes/ServerLogin.unity";
        // The server panel: the settings popup plate (overlay.png, transparent margin inside) centred
        // on the stage above the footer. Positions inside it are panel pixels from its top-left.
        static readonly Vector2 LoginPanel = new Vector2(315, 140), LoginPanelSize = new Vector2(1290, 740);
        const float LoginInset = 90, LoginFieldX = 400, LoginFieldWidth = 800, LoginFieldHeight = 110;
        static readonly Color LoginDark = new Color32(40, 30, 20, 255), LoginGrey = new Color32(90, 80, 70, 255);

        // Creates ServerLogin only when missing (an existing layout is kept), rebinds its art and
        // sounds, puts it in Build Settings after GlobalSettingScene and sends Entry's 演奏ゲーム board
        // to it instead of SongSelect. Repeated runs change nothing.
        [MenuItem("OurTaiko/Create Server Login Scene")]
        public static void CreateServerLoginScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportSettingArt();
            font = UiFont();
            if (!File.Exists(ServerLoginPath))
            {
                var root = NewStage();
                BuildServerLoginScene(root);
                AddStageFps(root);
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ServerLoginPath);
            }
            var scene = EditorSceneManager.OpenScene(ServerLoginPath);
            ConfigureServerLoginScene(UnityEngine.Object.FindFirstObjectByType<ServerLoginScene>());
            EditorSceneManager.SaveScene(scene);

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == ServerLoginPath))
            {
                scenes.Insert(scenes.FindIndex(s => s.path == SettingPath) + 1, new EditorBuildSettingsScene(ServerLoginPath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            var entry = EditorSceneManager.OpenScene(EntryPath);
            var view = UnityEngine.Object.FindFirstObjectByType<EntryView>();
            bool changed = false;
            foreach (var board in view.boards)
                if (board.scene == SceneSwitcher.SongSelectScene) { board.scene = SceneSwitcher.ServerLoginScene; changed = true; }
            if (changed)
            {
                EditorUtility.SetDirty(view);
                EditorSceneManager.MarkSceneDirty(entry);
                EditorSceneManager.SaveScene(entry);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: ServerLogin is ready and Entry's play board leads to it.");
        }

        static void BuildServerLoginScene(RectTransform viewport)
        {
            var uiFont = UiFont();
            var white = UiOutlineMaterial();
            var plain = uiFont.material;
            var controller = new GameObject("ServerLogin").AddComponent<ServerLoginScene>();
            controller.bgm = Source(controller.transform, "Bgm", 0.8f);
            controller.sfx = Source(controller.transform, "Sounds");

            var stage = Rect("Stage", viewport, 0, 0, 1920, 1080);
            Stretch(SkinUi.Image("Background", stage, SettingSprite("background/background")).rectTransform);
            var view = stage.gameObject.AddComponent<ServerLoginView>();
            view.header = SettingText(stage, "Header", uiFont, white, Color.white, 64, TextAlignmentOptions.MidlineLeft, 900);
            view.header.text = "サーバーログイン";
            view.header.rectTransform.TopLeft(60, 40);

            var panel = Rect("Panel", stage, LoginPanel.x, LoginPanel.y, LoginPanelSize.x, LoginPanelSize.y);
            var plate = panel.gameObject.AddComponent<Image>();
            plate.sprite = SettingSprite("background/overlay"); plate.type = Image.Type.Sliced; plate.raycastTarget = false;
            float textWidth = LoginPanelSize.x - 2 * LoginInset;
            view.serverName = SettingText(panel, "ServerName", uiFont, plain, LoginDark, 48, TextAlignmentOptions.MidlineLeft, textWidth - 200);
            view.serverName.rectTransform.TopLeft(LoginInset, 50);
            view.progress = SettingText(panel, "Progress", uiFont, plain, LoginGrey, 34, TextAlignmentOptions.MidlineRight, 200);
            view.progress.rectTransform.TopLeft(LoginPanelSize.x - LoginInset - 200, 56);
            view.serverUrl = SettingText(panel, "ServerUrl", uiFont, plain, LoginGrey, 30, TextAlignmentOptions.MidlineLeft, textWidth);
            view.serverUrl.rectTransform.TopLeft(LoginInset, 120);

            (view.username, view.usernameBox) = LoginField(panel, "Username", "ユーザー名", 190, false, uiFont, white, plain);
            (view.password, view.passwordBox) = LoginField(panel, "Password", "パスワード", 320, true, uiFont, white, plain);

            view.message = SettingText(panel, "Message", uiFont, plain, Color.black, 30, TextAlignmentOptions.TopLeft, textWidth);
            view.message.textWrappingMode = TextWrappingModes.Normal;
            view.message.rectTransform.sizeDelta = new Vector2(textWidth, 90);
            view.message.rectTransform.TopLeft(LoginInset, 455);
            view.messageColor = Color.black;
            view.errorColor = new Color32(190, 30, 30, 255);
            view.successColor = new Color32(10, 95, 20, 255);

            // ログイン / ゲスト / スキップ / もどる: the settings choice buttons at 1.2x, centred in one row.
            var size = new Vector2(212, 102) * ChoiceScale;
            const float gap = 30;
            float x = (LoginPanelSize.x - (4 * size.x + 3 * gap)) / 2, y = LoginPanelSize.y - LoginInset - size.y + 10;
            var captions = new[] { (ServerLoginView.Item.Login, "ログイン"), (ServerLoginView.Item.Guest, "ゲスト"), (ServerLoginView.Item.Skip, "スキップ"), (ServerLoginView.Item.Back, "もどる") };
            view.buttons = captions.Select((caption, i) =>
            {
                var rect = Rect(caption.Item1 + "Button", panel, x + i * (size.x + gap), y, size.x, size.y);
                var box = rect.gameObject.AddComponent<Image>();
                box.sprite = SettingSprite("option/button_off"); box.type = Image.Type.Sliced; box.raycastTarget = true;
                var label = SettingText(rect, "Label", uiFont, white, Color.white, 40, TextAlignmentOptions.Center, size.x - 20);
                Stretch(label.rectTransform);
                label.text = caption.Item2;
                return new ServerLoginView.Button { item = caption.Item1, box = box, label = label, click = rect.gameObject.AddComponent<PointerRelay>() };
            }).ToArray();

            var footer = SkinUi.Image("Footer", stage, SettingSprite("background/footer"), 1920, 128 * SettingScale);
            footer.rectTransform.TopLeft(0, 1080 - 128 * SettingScale);
            controller.view = view;
        }

        // A label on the panel and a TMP_InputField on the settings item plate (title.png).
        static (TMP_InputField, Image) LoginField(Transform panel, string name, string caption, float y, bool secret,
            TMP_FontAsset uiFont, Material white, Material plain)
        {
            var label = SettingText(panel, name + "Label", uiFont, plain, LoginDark, 40, TextAlignmentOptions.MidlineLeft, LoginFieldX - LoginInset - 20);
            label.text = caption;
            label.rectTransform.TopLeft(LoginInset, y + (LoginFieldHeight - label.rectTransform.sizeDelta.y) / 2);

            var rect = Rect(name, panel, LoginFieldX, y, LoginFieldWidth, LoginFieldHeight);
            var box = rect.gameObject.AddComponent<Image>();
            box.sprite = SettingSprite("background/title"); box.type = Image.Type.Sliced; box.raycastTarget = true;
            var area = Rect("Text Area", rect, 0, 0, 0, 0);
            Stretch(area, 36, 36);
            area.gameObject.AddComponent<RectMask2D>();
            var placeholder = InputText(area, "Placeholder", uiFont, white, new Color(1, 1, 1, 0.5f));
            placeholder.text = secret ? "パスワード" : "ユーザー名";
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
            field.contentType = secret ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            field.characterLimit = 128;
            field.restoreOriginalTextOnEscape = false;
            field.customCaretColor = true;
            field.caretColor = Color.white;
            field.caretWidth = 3;
            field.selectionColor = new Color(1, 0.85f, 0.3f, 0.5f);
            return (field, box);
        }

        static TextMeshProUGUI InputText(Transform parent, string name, TMP_FontAsset uiFont, Material material, Color color)
        {
            var rect = Rect(name, parent, 0, 0, 0, 0);
            Stretch(rect);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            SetOutlinedUiText(text, uiFont, material, color);
            text.fontSize = 40;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        static void ConfigureServerLoginScene(ServerLoginScene controller)
        {
            var view = controller.view;
            view.fieldOff = SettingSprite("background/title");
            view.fieldOn = SettingSprite("background/title_highlight");
            view.buttonOff = SettingSprite("option/button_off");
            view.buttonOn = SettingSprite("option/button_on");
            controller.don = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/don.ogg");
            controller.ka = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/ka.ogg");
            controller.bgm.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/settings/bgm.ogg");
            foreach (var clip in new[] { controller.don, controller.ka, controller.bgm.clip })
                if (clip == null) throw new FileNotFoundException("A server login sound is missing.");
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(controller);
        }
    }
}
