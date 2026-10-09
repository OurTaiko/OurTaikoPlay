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
        [MenuItem("OurTaiko/Apply Song Search")]
        public static void ApplySongSearch()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string art = Root + "Art/song_select/diff_sort/";
            foreach (string file in Directory.GetFiles(art, "*.png")) ImportBoardArt(file, Vector4.zero);
            var scene = EditorSceneManager.OpenScene(SongSelectPath);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            if (select.view.search != null)
            {
                UpdateSearchLayout(select.view.search);
                select.view.search.panel.SetActive(false);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
                return;
            }
            var root = SkinUi.Rect("Search", select.view.transform, 1920, 1080);
            root.TopLeft(0, 0);
            var view = root.gameObject.AddComponent<SongSearchView>();
            select.view.search = view;
            Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(art + name + ".png");
            TextMeshProUGUI Text(string name, Transform parent, string value, float x, float y, float width, float size)
            {
                var text = SkinUi.Text(name, parent, size); text.text = value; text.richText = false;
                text.rectTransform.sizeDelta = new Vector2(width, size * 1.5f); text.rectTransform.Center(x, y);
                text.enableAutoSizing = true; text.fontSizeMin = size * .6f; text.fontSizeMax = size;
                text.overflowMode = TextOverflowModes.Ellipsis;
                return text;
            }
            UnityEngine.UI.Button Button(string name, Transform parent, string label, float x, float y, float width)
            {
                var image = SkinUi.Image(name, parent, Art("level_box"), width, 76); image.rectTransform.Center(x, y); image.raycastTarget = true;
                var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
                button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                var text = Text("Label", image.transform, label, width / 2, 38, width - 30, 34);
                text.color = Color.black; text.UseUiFont();
                return button;
            }
            view.summary = Text("Summary", root, "", 1510, 290, 700, 28);
            view.summary.rectTransform.sizeDelta = new Vector2(700, 120);
            view.summary.textWrappingMode = TextWrappingModes.Normal;
            var panel = SkinUi.Rect("SearchDialog", root, 1920, 1080); panel.TopLeft(0, 0); view.panel = panel.gameObject;
            var shade = SkinUi.Image("Shade", panel, null, 1920, 1080); shade.rectTransform.TopLeft(0, 0); shade.color = new Color(0, 0, 0, .65f); shade.raycastTarget = true;
            var background = SkinUi.Image("NijiiroPanel", panel, Art("background_search")); background.rectTransform.TopLeft(300, 150);
            view.title = Text("Title", panel, "Song search", 690, 275, 580, 48);
            view.status = Text("Status", panel, "", 1360, 278, 330, 26);
            view.status.textWrappingMode = TextWrappingModes.Normal; view.status.rectTransform.sizeDelta = new Vector2(330, 85);
            var cursor = SkinUi.Image("Cursor", panel, Art("box_highlight"), 1240, 138);
            view.cursor = cursor.rectTransform;
            view.labels = new TextMeshProUGUI[4]; view.values = new TextMeshProUGUI[3];
            view.previous = new UnityEngine.UI.Button[3]; view.next = new UnityEngine.UI.Button[3];
            float[] ys = { 400, 541, 682, 813 };
            for (int i = 0; i < 4; i++)
            {
                view.labels[i] = Text("Label" + i, panel, "", 545, ys[i], 310, 38);
                var pill = SkinUi.Image("Value" + i, panel, Art("level_box"), 792, 104);
                pill.rectTransform.Center(1124, ys[i]);
                if (i < 3)
                {
                    view.previous[i] = Button("Previous" + i, panel, "◀", 780, ys[i], 86);
                    view.next[i] = Button("Next" + i, panel, "▶", 1465, ys[i], 86);
                    view.values[i] = Text("ValueText" + i, panel, "", 1124, ys[i], 570, 38);
                    view.values[i].color = Color.black; view.values[i].UseUiFont();
                }
                else
                {
                    pill.raycastTarget = true;
                    var area = SkinUi.Rect("TextArea", pill.transform, 704, 68); area.Center(396, 43); area.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
                    var text = Text("Text", area, "", 352, 34, 704, 36); text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.MidlineLeft;
                    text.color = Color.black; text.UseUiFont(); text.overflowMode = TextOverflowModes.Overflow;
                    var placeholder = Text("Placeholder", area, "Title / Subtitle / Maker", 352, 44.3f, 704, 30);
                    placeholder.color = Color.white; placeholder.UseUiFont(); placeholder.alignment = TextAlignmentOptions.MidlineLeft;
                    var field = pill.gameObject.AddComponent<TMP_InputField>();
                    field.textViewport = area; field.textComponent = text; field.placeholder = placeholder; field.fontAsset = SkinUi.Font;
                    field.targetGraphic = pill; field.lineType = TMP_InputField.LineType.SingleLine; field.characterLimit = 200;
                    field.richText = false; field.pointSize = 36; field.restoreOriginalTextOnEscape = false;
                    field.customCaretColor = true; field.caretColor = Color.black; field.caretWidth = 3;
                    field.transition = UnityEngine.UI.Selectable.Transition.None;
                    field.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
                    view.keyword = field;
                }
            }
            view.hint = Text("Hint", panel, "", 960, 900, 1400, 26);
            view.apply = Button("ApplySearch", panel, "Search", 620, 973, 300);
            view.close = Button("CloseSearch", panel, "Back", 1300, 973, 300);
            UpdateSearchLayout(view);
            panel.gameObject.SetActive(false);
            EditorUtility.SetDirty(select.view);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }
        static void UpdateSearchLayout(SongSearchView view)
        {
            const string art = Root + "Art/song_select/diff_sort/";
            var oldEntry = view.transform.Find("OpenSearch");
            if (oldEntry != null) UnityEngine.Object.DestroyImmediate(oldEntry.gameObject);
            var oldClear = view.panel.transform.Find("ClearSearch");
            if (oldClear != null) UnityEngine.Object.DestroyImmediate(oldClear.gameObject);
            view.summary.rectTransform.Center(1650, 430);
            view.summary.rectTransform.sizeDelta = new Vector2(440, 175);
            var background = view.panel.transform.Find("NijiiroPanel").GetComponent<UnityEngine.UI.Image>();
            background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(art + "background_search.png");
            background.rectTransform.sizeDelta = new Vector2(1360, 930);
            background.rectTransform.TopLeft(280, 75);
            view.panel.transform.Find("Shade").GetComponent<UnityEngine.UI.Image>().color = new Color(0, 0, 0, .78f);

            void PlaceText(TextMeshProUGUI text, float x, float y, float width, float height, float size)
            {
                text.rectTransform.Center(x, y);
                text.rectTransform.sizeDelta = new Vector2(width, height);
                text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size * .8f;
                text.enableAutoSizing = true;
            }
            PlaceText(view.title, 960, 208, 1100, 76, 48);
            view.title.alignment = TextAlignmentOptions.MidlineLeft;
            PlaceText(view.status, 960, 263, 1100, 48, 24);
            view.status.alignment = TextAlignmentOptions.MidlineLeft;
            view.cursor.sizeDelta = new Vector2(1180, 112);
            view.cursor.Center(960, 458);
            // Arrays retain their difficulty / stars / sort / keyword bindings;
            // their authored positions and runtime navigation put Keyword first.
            float[] ys = { 458, 576, 694, 340 };
            for (int i = 0; i < 4; i++)
            {
                PlaceText(view.labels[i], 560, ys[i], 300, 72, 36);
                var pill = view.panel.transform.Find("Value" + i).GetComponent<RectTransform>();
                pill.Center(1120, ys[i]); pill.sizeDelta = new Vector2(760, 94);
                if (i < 3)
                {
                    PlaceText(view.values[i], 1120, ys[i], 540, 68, 36);
                    ((RectTransform)view.previous[i].transform).Center(795, ys[i]);
                    ((RectTransform)view.next[i].transform).Center(1445, ys[i]);
                }
            }
            view.keyword.textViewport.sizeDelta = new Vector2(680, 64);
            view.keyword.textViewport.Center(380, 47);
            var fieldText = (TextMeshProUGUI)view.keyword.textComponent;
            PlaceText(fieldText, 340, 32, 680, 64, 36);
            fieldText.enableAutoSizing = false;
            fieldText.alignment = TextAlignmentOptions.MidlineLeft;
            var placeholder = (TextMeshProUGUI)view.keyword.placeholder;
            PlaceText(placeholder, 340, 32, 680, 64, 30);
            placeholder.color = new Color(.3f, .3f, .3f); placeholder.UseUiFont();
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;

            foreach (var pair in new[] { (view.apply, 665f), (view.close, 1255f) })
            {
                var rect = (RectTransform)pair.Item1.transform;
                rect.Center(pair.Item2, 820); rect.sizeDelta = new Vector2(510, 88);
                PlaceText(pair.Item1.GetComponentInChildren<TextMeshProUGUI>(), 255, 44, 460, 70, 36);
            }
            view.apply.targetGraphic.color = new Color(1, .92f, .45f);
            view.close.targetGraphic.color = Color.white;
            PlaceText(view.hint, 960, 899, 1100, 40, 24);
            for (int i = 0; i < 3; i++)
                foreach (var pair in new[] { (view.previous[i], "arrow_left"), (view.next[i], "arrow_right") })
                {
                    var image = (UnityEngine.UI.Image)pair.Item1.targetGraphic;
                    image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(art + pair.Item2 + ".png");
                    image.preserveAspect = true; image.rectTransform.sizeDelta = new Vector2(64, 80);
                    pair.Item1.GetComponentInChildren<TMP_Text>(true).gameObject.SetActive(false);
                }
        }
    }
}
