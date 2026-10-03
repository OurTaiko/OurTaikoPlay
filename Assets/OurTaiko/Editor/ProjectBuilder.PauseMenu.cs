using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string PauseMenuArt = "dan_select/confirm_box/";

        [MenuItem("OurTaiko/Apply Pause Menu")]
        public static void ApplyPauseMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before editing the pause menu.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");

            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            ConfigurePauseMenu(UnityEngine.Object.FindFirstObjectByType<PlayScene>());
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: pause menu applied to SinglePlayScene.");
        }

        // Shared by the targeted migration and first-time scene creation. Existing controls retain
        // their object identities and PlayScene references; no gameplay hierarchy is regenerated.
        public static void ConfigurePauseMenu(PlayScene play)
        {
            if (play == null || play.pausePanel == null)
                throw new InvalidOperationException("SinglePlayScene must have its PlayScene and pause panel first.");
            var canvas = play.pausePanel.GetComponentInParent<Canvas>();
            if (canvas == null) throw new InvalidOperationException("The pause panel must belong to a Canvas.");
            ImportPauseMenuArt();

            var root = (RectTransform)play.pausePanel.transform;
            root.SetParent(canvas.transform, false);
            PauseStretch(root);
            root.SetAsLastSibling();
            var shade = root.GetComponent<UnityEngine.UI.Image>();
            if (shade == null) shade = root.gameObject.AddComponent<UnityEngine.UI.Image>();
            shade.sprite = null;
            shade.type = UnityEngine.UI.Image.Type.Simple;
            shade.color = new Color(0.025f, 0.04f, 0.065f, 0.76f);
            shade.raycastTarget = true;

            // The shade covers the entire window, including letterbox margins. The artwork remains
            // on the same centred 1920 x 1080 design region as gameplay and follows its CanvasScaler.
            var design = PauseRect(root, "Viewport1920x1080");
            design.anchorMin = design.anchorMax = design.pivot = new Vector2(0.5f, 0.5f);
            design.anchoredPosition = Vector2.zero;
            design.sizeDelta = new Vector2(1920, 1080);
            var board = PauseRect(design, "Board");
            board.anchorMin = board.anchorMax = board.pivot = new Vector2(0.5f, 0.5f);
            board.anchoredPosition = Vector2.zero;
            board.sizeDelta = new Vector2(1100, 780);
            var boardImage = PauseImage(board, Sprite(PauseMenuArt + "bg"));
            boardImage.raycastTarget = false;

            var uiFont = UiFont();
            var titleMaterial = UiOutlineMaterial();
            var buttonMaterial = uiFont.material;
            var hintMaterial = UiOutlineMaterial();
            // Reuse the old heading when upgrading the original simple overlay.
            var title = board.Find("Title") as RectTransform;
            if (title == null) title = root.Find("Title") as RectTransform;
            if (title == null) title = PauseRect(board, "Title");
            title.SetParent(board, false);
            PauseTopCenter(title, 84, 920, 100);
            PauseText(title, "PAUSED", uiFont, titleMaterial, 64, Color.white);

            play.resumeButton = PauseButton(play.resumeButton, board, "ResumeButton", "Resume", 214,
                uiFont, buttonMaterial);
            play.restartButton = PauseButton(play.restartButton, board, "RestartButton", "Restart", 350,
                uiFont, buttonMaterial);
            play.backButton = PauseButton(play.backButton, board, "BackButton", "Back to Song Select", 486,
                uiFont, buttonMaterial);
            var hint = PauseRect(board, "Hint");
            PauseTopCenter(hint, 657, 970, 50);
            PauseText(hint, "↑ / ↓ · Enter   Space / Esc · Resume", uiFont, hintMaterial, 25, Color.white);

            // Only the obsolete contents of this pause overlay are hidden. They are not destroyed,
            // and every requested control has already been moved into the new board.
            for (int i = 0; i < root.childCount; i++)
                if (root.GetChild(i) != design) root.GetChild(i).gameObject.SetActive(false);
            design.SetAsLastSibling();
            var group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = false;
            group.blocksRaycasts = false;
            var view = root.GetComponent<PauseMenuView>();
            if (view == null) view = root.gameObject.AddComponent<PauseMenuView>();
            view.group = group;
            view.buttons = new[] { play.resumeButton, play.restartButton, play.backButton };
            play.pauseMenu = view;
            if (play.pauseButton != null)
                play.pauseButton.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            root.gameObject.SetActive(false);
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(play);
        }

        static void ImportPauseMenuArt()
        {
            foreach (string name in new[] { "bg", "selection_box", "selection_box_outline", "selection_box_highlight" })
            {
                string path = Root + "Art/" + PauseMenuArt + name + ".png";
                if (!File.Exists(path))
                {
                    string source = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../../OurTaikoPlayer/Skins/YataiDONNijiiro/Graphics/", PauseMenuArt, name + ".png"));
                    if (!File.Exists(source)) throw new FileNotFoundException("Missing Nijiiro pause-menu source art.", source);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.Copy(source, path);
                }
                // Import only these new menu textures; never reimport or alter unrelated skin art.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                var border = name == "bg" ? new Vector4(360, 220, 360, 220) : new Vector4(74, 64, 74, 64);
                if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single
                    && importer.spriteBorder == border && importer.spritePixelsPerUnit == 100
                    && settings.spriteMeshType == SpriteMeshType.FullRect
                    && !importer.mipmapEnabled && importer.alphaIsTransparency
                    && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.maxTextureSize == 2048)
                    continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.spriteBorder = border;
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }

        static UnityEngine.UI.Button PauseButton(UnityEngine.UI.Button button, RectTransform parent,
            string name, string caption, float top, TMP_FontAsset uiFont, Material material)
        {
            var rect = button != null ? (RectTransform)button.transform : PauseRect(parent, name);
            rect.name = name;
            rect.SetParent(parent, false);
            PauseTopCenter(rect, top, 750, 136);
            rect.gameObject.SetActive(true);
            var image = PauseImage(rect, Sprite(PauseMenuArt + "selection_box"));
            image.raycastTarget = true;
            if (button == null) button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
            var colors = UnityEngine.UI.ColorBlock.defaultColorBlock;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, 0.95f, 0.78f);
            colors.pressedColor = new Color(0.85f, 0.72f, 0.50f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };

            var outline = PauseRect(rect, "Outline");
            PauseStretch(outline);
            PauseImage(outline, Sprite(PauseMenuArt + "selection_box_outline")).raycastTarget = false;
            var selection = PauseRect(rect, "Selection");
            PauseStretch(selection);
            PauseImage(selection, Sprite(PauseMenuArt + "selection_box_highlight")).raycastTarget = false;
            selection.gameObject.SetActive(name == "ResumeButton");
            var label = PauseRect(rect, "Label");
            PauseStretch(label);
            label.SetAsLastSibling();
            PauseText(label, caption, uiFont, material, 40, new Color32(45, 30, 14, 255));
            EditorUtility.SetDirty(button);
            return button;
        }

        static RectTransform PauseRect(Transform parent, string name)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null) rect = Rect(name, parent, 0, 0, 0, 0);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.gameObject.SetActive(true);
            return rect;
        }

        static void PauseStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        static void PauseTopCenter(RectTransform rect, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -top);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one;
        }

        static UnityEngine.UI.Image PauseImage(RectTransform rect, Sprite sprite)
        {
            var image = rect.GetComponent<UnityEngine.UI.Image>();
            if (image == null) image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = Color.white;
            image.preserveAspect = false;
            return image;
        }

        static void PauseText(RectTransform rect, string value, TMP_FontAsset uiFont, Material material, float size, Color color)
        {
            var text = rect.GetComponent<TMP_Text>();
            if (text == null) text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            SetOutlinedUiText(text, uiFont, material, color);
            text.text = value;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
        }
    }
}
