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
        const string SettingPath = "Assets/Scenes/GlobalSettingScene.unity", SettingArt = "settings/";
        // PyTaikoGreen's settings art is 1280-wide; everything is drawn at 1.5x on the 1920 stage.
        const float SettingScale = 1.5f;
        // Layout (stage pixels, y down): the type list on the left, the item list on the right (room
        // for five rows above the footer), the footer strip at the bottom and the choice popup
        // centred over a dimming shade.
        static readonly Vector2 SettingTypeList = new Vector2(40, 150), SettingItemList = new Vector2(690, 150), SettingDetail = new Vector2(315, 340);
        static readonly Color SettingShade = new Color(0, 0, 0, 0.55f);
        // The popup is wider than an item row so three choice buttons and the arrow fit inside it.
        const float SettingItemWidth = 1170, SettingItemHeight = 130, SettingDetailWidth = 1290, SettingDetailHeight = 400;
        // overlay.png keeps a transparent margin around its plate; texts sit inside the visible box.
        const float SettingDetailInset = 90, ChoiceScale = 1.2f;

        // Creates GlobalSettingScene only when missing (an existing layout is kept), then rebinds its
        // art and sounds, adds it to Build Settings after Entry and hands SinglePlayScene its touch
        // drum reference for the Play > drum pad setting.
        [MenuItem("OurTaiko/Create Global Setting Scene")]
        public static void CreateGlobalSettingScene()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before creating scenes.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the current scene edits first.");
            ImportSettingArt();
            // AddStageFps's label uses the shared Nijiiro font like the other scenes.
            font = UiFont();
            if (!File.Exists(SettingPath))
            {
                var root = NewStage();
                BuildSettingScene(root);
                AddStageFps(root);
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), SettingPath);
            }
            var scene = EditorSceneManager.OpenScene(SettingPath);
            var controller = UnityEngine.Object.FindFirstObjectByType<GlobalSettingScene>();
            ConfigureSettingScene(controller);
            ConfigureSettingPopup(controller.view);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != SettingPath).ToList();
            scenes.Insert(scenes.FindIndex(s => s.path == EntryPath) + 1, new EditorBuildSettingsScene(SettingPath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            EditPlayScene(play =>
            {
                play.drumPad = play.pauseButton.transform.parent.Find("TouchDrum").GetComponent<DrumPad>();
            });
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: GlobalSettingScene is ready and in Build Settings.");
        }

        // Unity's default import auto-slices new PNGs into trimmed sprites: start every settings
        // image as one Single sprite, with 9-slice borders on the stretched plates.
        static void ImportSettingArt()
        {
            var borders = new (string name, int border)[]
            {
                ("background/background", 0), ("background/footer", 0), ("background/blue_arrow", 0),
                ("background/overlay", 40), ("background/title", 24), ("background/title_highlight", 24),
                ("box/box", 12), ("box/box_highlight", 12), ("option/button_on", 14), ("option/button_off", 14),
            };
            foreach (var (name, border) in borders)
            {
                string path = Root + "Art/" + SettingArt + name + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path) ?? throw new FileNotFoundException(path);
                var slice = new Vector4(border, border, border, border);
                if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single
                    && importer.spriteBorder == slice && !importer.mipmapEnabled) continue;
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = slice;
                importer.spritePixelsPerUnit = 100;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 4096;
                importer.SaveAndReimport();
            }
        }

        static Sprite SettingSprite(string name) => Sprite(SettingArt + name) ?? throw new FileNotFoundException(SettingArt + name);

        static void BuildSettingScene(RectTransform viewport)
        {
            var uiFont = UiFont();
            var white = UiOutlineMaterial();
            var plain = uiFont.material;
            var controller = new GameObject("GlobalSetting").AddComponent<GlobalSettingScene>();
            controller.bgm = Source(controller.transform, "Bgm", 0.8f);
            controller.sfx = Source(controller.transform, "Sounds");

            var stage = Rect("Stage", viewport, 0, 0, 1920, 1080);
            Stretch(SkinUi.Image("Background", stage, SettingSprite("background/background")).rectTransform);
            var header = SettingText(stage, "Header", uiFont, white, Color.white, 64, TextAlignmentOptions.MidlineLeft, 900);
            header.text = "ゲーム設定";
            header.rectTransform.TopLeft(60, 40);

            var view = stage.gameObject.AddComponent<GlobalSettingView>();
            view.typeSwipe = SwipeArea(stage, "TypeList", SettingTypeList, new Vector2(620, 700), view.typePitch);
            view.itemSwipe = SwipeArea(stage, "ItemList", SettingItemList, new Vector2(SettingItemWidth + 40, 730), view.itemPitch);
            view.typeSwipe.transform.SetAsLastSibling();

            // TypeRow0: box.png (378x92) at 1.5x, label centred.
            var typeSize = new Vector2(378, 92) * SettingScale;
            var typeRow = RowRect("TypeRow0", view.typeSwipe.transform, new Vector2(20, 20), typeSize, SettingSprite("box/box"));
            var typeLabel = SettingText(typeRow.root, "Label", uiFont, white, Color.white, 56, TextAlignmentOptions.Center, typeSize.x - 40);
            Stretch(typeLabel.rectTransform);
            typeRow.label = typeLabel;
            view.typeRows.Add(typeRow);

            // ItemRow0: title.png stretched as a 9-slice plate; label left, current choice right.
            var itemRow = RowRect("ItemRow0", view.itemSwipe.transform, new Vector2(20, 20), new Vector2(SettingItemWidth, SettingItemHeight), SettingSprite("background/title"));
            var itemLabel = SettingText(itemRow.root, "Label", uiFont, white, Color.white, 40, TextAlignmentOptions.MidlineLeft, 820);
            Stretch(itemLabel.rectTransform, 40, 290);
            // Long names shrink to fit in front of the value instead of running into it.
            itemLabel.enableAutoSizing = true; itemLabel.fontSizeMin = 26; itemLabel.fontSizeMax = 40;
            itemRow.label = itemLabel;
            var itemValue = SettingText(itemRow.root, "Value", uiFont, white, Color.white, 40, TextAlignmentOptions.MidlineRight, 250);
            Stretch(itemValue.rectTransform, SettingItemWidth - 280, 40);
            itemRow.value = itemValue;
            view.itemRows.Add(itemRow);

            // Detail: overlay.png as the panel, the item's name and description, the choice buttons.
            var detailRect = Rect("Detail", stage, SettingDetail.x, SettingDetail.y, SettingDetailWidth, SettingDetailHeight);
            var panel = detailRect.gameObject.AddComponent<Image>();
            panel.sprite = SettingSprite("background/overlay"); panel.type = Image.Type.Sliced; panel.raycastTarget = false;
            view.detail = detailRect.gameObject.AddComponent<CanvasGroup>();
            float textWidth = SettingDetailWidth - 2 * SettingDetailInset;
            view.detailTitle = SettingText(detailRect, "Title", uiFont, plain, new Color32(40, 30, 20, 255), 42, TextAlignmentOptions.MidlineLeft, textWidth);
            view.detailTitle.rectTransform.TopLeft(SettingDetailInset, 44);
            view.description = SettingText(detailRect, "Description", uiFont, plain, new Color32(70, 60, 50, 255), 32, TextAlignmentOptions.TopLeft, textWidth);
            view.description.rectTransform.sizeDelta = new Vector2(textWidth, 90);
            view.description.textWrappingMode = TextWrappingModes.Normal;
            view.description.rectTransform.TopLeft(SettingDetailInset, 112);
            // option buttons at 1.2x so the pair fits inside the panel's visible box
            var buttonSize = new Vector2(212, 102) * ChoiceScale;
            var choiceRow = RowRect("ChoiceRow0", detailRect, new Vector2((SettingDetailWidth - buttonSize.x) / 2, SettingDetailHeight - buttonSize.y - 64), buttonSize, SettingSprite("option/button_off"));
            var choiceLabel = SettingText(choiceRow.root, "Label", uiFont, white, Color.white, 40, TextAlignmentOptions.Center, buttonSize.x - 20);
            Stretch(choiceLabel.rectTransform);
            choiceRow.label = choiceLabel;
            view.choiceRows.Add(choiceRow);

            // blue_arrow points at the focused row from its right.
            var cursor = SkinUi.Image("Cursor", stage, SettingSprite("background/blue_arrow"), 70 * SettingScale, 70 * SettingScale);
            view.cursor = cursor.rectTransform;
            var footer = SkinUi.Image("Footer", stage, SettingSprite("background/footer"), 1920, 128 * SettingScale);
            footer.rectTransform.TopLeft(0, 1080 - 128 * SettingScale);
            controller.view = view;
        }

        // The detail panel as the choice popup: a full-stage shade (dims the lists, closes the popup
        // when tapped) under the centred panel, both over the footer, with the cursor arrow on top.
        // Runs once; a scene that already has its shade keeps its layout.
        static void ConfigureSettingPopup(GlobalSettingView view)
        {
            if (view.shade != null) return;
            var stage = view.transform;
            var shade = SkinUi.Image("PopupShade", stage, null, 1920, 1080);
            Stretch(shade.rectTransform);
            shade.color = SettingShade;
            shade.raycastTarget = true;
            view.shade = shade;
            view.shadeClick = shade.gameObject.AddComponent<PointerRelay>();
            var detail = (RectTransform)view.detail.transform;
            detail.anchoredPosition = new Vector2(SettingDetail.x, -SettingDetail.y);
            var itemList = (RectTransform)view.itemSwipe.transform;
            itemList.sizeDelta = new Vector2(itemList.sizeDelta.x, 730);
            shade.transform.SetAsLastSibling();
            detail.SetAsLastSibling();
            view.cursor.SetAsLastSibling();
            shade.gameObject.SetActive(false);
            view.detail.alpha = 0;
            view.detail.blocksRaycasts = false;
            EditorUtility.SetDirty(view);
        }

        static void ConfigureSettingScene(GlobalSettingScene controller)
        {
            var view = controller.view;
            view.typeBox = SettingSprite("box/box");
            view.typeBoxSelected = SettingSprite("box/box_highlight");
            view.itemBox = SettingSprite("background/title");
            view.itemBoxSelected = SettingSprite("background/title_highlight");
            view.choiceOff = SettingSprite("option/button_off");
            view.choiceOn = SettingSprite("option/button_on");
            controller.don = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/don.ogg");
            controller.ka = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/ka.ogg");
            controller.bgm.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "Audio/settings/bgm.ogg");
            foreach (var clip in new[] { controller.don, controller.ka, controller.bgm.clip })
                if (clip == null) throw new FileNotFoundException("A settings sound is missing.");
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(controller);
        }

        static SwipeRelay SwipeArea(Transform parent, string name, Vector2 position, Vector2 size, float step)
        {
            var rect = Rect(name, parent, position.x, position.y, size.x, size.y);
            var hit = rect.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            var swipe = rect.gameObject.AddComponent<SwipeRelay>();
            swipe.step = step * 0.8f;
            return swipe;
        }

        static GlobalSettingView.Row RowRect(string name, Transform parent, Vector2 position, Vector2 size, Sprite sprite)
        {
            var rect = Rect(name, parent, position.x, position.y, size.x, size.y);
            var box = rect.gameObject.AddComponent<Image>();
            box.sprite = sprite; box.type = Image.Type.Sliced; box.raycastTarget = true;
            return new GlobalSettingView.Row { root = rect, box = box, click = rect.gameObject.AddComponent<PointerRelay>() };
        }

        static TMP_Text SettingText(Transform parent, string name, TMP_FontAsset uiFont, Material material, Color color, float size, TextAlignmentOptions alignment, float width)
        {
            var rect = Rect(name, parent, 0, 0, width, size * 1.4f);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            SetOutlinedUiText(text, uiFont, material, color);
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            return text;
        }

        // Fills the parent, inset by `left` / `right`.
        static void Stretch(RectTransform rect, float left = 0, float right = 0)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, 0); rect.offsetMax = new Vector2(-right, 0);
        }
    }
}
