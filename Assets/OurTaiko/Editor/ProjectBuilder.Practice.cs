using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Practice Mode")]
        public static void ApplyPracticeMode()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string path = "Assets/Scenes/PracticeScene.unity";
            // Copy the entire saved scene, with its references and editable authored layout intact.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null
                && !AssetDatabase.CopyAsset("Assets/Scenes/SinglePlayScene.unity", path))
                throw new InvalidOperationException("Could not copy SinglePlayScene.");
            var scene = EditorSceneManager.OpenScene(path);
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            if (play.practiceView == null)
            {
                var parent = play.noteLayer.GetComponentInParent<Canvas>().transform.Find("Viewport1920x1080");
                if (parent == null) parent = play.title.transform.parent;
                var root = SkinUi.Rect("PracticeControls", parent);
                root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 1);
                root.anchoredPosition = new Vector2(0, -5);
                root.sizeDelta = new Vector2(900, 230);
                var shade = root.gameObject.AddComponent<UnityEngine.UI.Image>();
                shade.color = new Color(0.02f, 0.09f, 0.12f, 0.9f);
                shade.raycastTarget = true;
                var view = root.gameObject.AddComponent<PracticeView>();
                view.panel = root.gameObject;
                view.heading = PracticeLabel(root, "Heading", "小节进度", 38, 12, 60);
                view.value = PracticeLabel(root, "Value", "1 / 1", 42, 73, 62);
                view.hint = PracticeLabel(root, "Hint", "咔：前后小节    咚：调整播放速度", 26, 161, 50);
                view.previous = PracticeButton(root, "Previous", "<", -335);
                view.next = PracticeButton(root, "Next", ">", 335);
                view.confirm = PracticeButton(root, "Confirm", "决定", 190);
                // Keep the reading centred and confirmation away from the value.
                view.previous.GetComponent<RectTransform>().sizeDelta = new Vector2(90, 68);
                view.next.GetComponent<RectTransform>().sizeDelta = new Vector2(90, 68);
                view.confirm.GetComponent<RectTransform>().sizeDelta = new Vector2(145, 68);
                play.practiceView = view;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == path))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Append(new EditorBuildSettingsScene(path, true)).ToArray();
            else if (EditorBuildSettings.scenes.Any(s => s.path == path && !s.enabled))
                EditorBuildSettings.scenes = EditorBuildSettings.scenes.Select(s => s.path == path ? new EditorBuildSettingsScene(path, true) : s).ToArray();
            ApplyPracticeEntry();
            AssetDatabase.SaveAssets();
            Debug.Log("OurTaiko: PracticeScene and Entry practice board saved.");
        }
        static TMP_Text PracticeLabel(Transform parent, string name, string text, int size, float top, float height)
        {
            var label = SkinUi.Text(name, parent, size);
            label.text = text;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(860, height);
            label.raycastTarget = false;
            return label;
        }
        static UnityEngine.UI.Button PracticeButton(Transform parent, string name, string text, float x)
        {
            var image = SkinUi.Image(name, parent, null, 90, 68);
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 10);
            image.color = new Color(0.1f, 0.4f, 0.38f, 1); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            var label = PracticeLabel(rect, "Label", text, 30, 0, 68);
            label.rectTransform.sizeDelta = new Vector2(140, 68);
            return button;
        }
        static void ApplyPracticeEntry()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Entry.unity");
            var entry = UnityEngine.Object.FindFirstObjectByType<EntryScene>();
            var view = entry.view;
            var existing = view.boards.FirstOrDefault(b => b.practice);
            if (existing != null)
            {
                if (existing.scene != SceneSwitcher.ServerLoginScene)
                {
                    existing.scene = SceneSwitcher.ServerLoginScene;
                    EditorUtility.SetDirty(view);
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
                return;
            }
            // Clone just one saved board; never regenerate the Entry layout.
            var source = view.boards[0];
            var root = UnityEngine.Object.Instantiate(source.root, view.modeBoards);
            root.name = "練習モード";
            T Child<T>(string name) where T : Component => root.Find(name).GetComponent<T>();
            var board = new EntryView.BoardView
            {
                root = root, scene = SceneSwitcher.ServerLoginScene, practice = true,
                cursor = Child<UnityEngine.UI.Image>("Cursor"), closed = Child<UnityEngine.UI.Image>("Closed"),
                open = Child<UnityEngine.UI.Image>("Open"), flash = Child<UnityEngine.UI.Image>("Flash"),
                title = Child<TextMeshProUGUI>("Title"), titleOpen = Child<RectTransform>("TitleOpen"),
                titleClosed = Child<RectTransform>("TitleClosed"), hit = Child<UnityEngine.UI.Image>("Hit"),
                hitRelay = Child<PointerRelay>("Hit"), closedHitSize = source.closedHitSize, openHitSize = source.openHitSize,
                info = source.info.Select(t => Child<TextMeshProUGUI>(t.name)).ToArray(),
            };
            board.title.text = "練習モード";
            board.open.sprite = Required("entry/mode_select/box/2");
            board.closed.sprite = Required("entry/mode_select/box/3");
            string[] descriptions = { "小節とスピードをえらんで", "くりかえし練習できるよ！" };
            for (int i = 0; i < board.info.Length; i++) board.info[i].text = descriptions[Math.Min(i, 1)];
            var list = LumenClip.Parse(entry.modeListTimeline.text);
            var boards = view.boards.ToList(); boards.Insert(1, board);
            for (int i = 1; i < boards.Count; i++)
            {
                var slot = EntryModeList.Slot(list, i);
                var previous = EntryModeList.Slot(list, i - 1);
                boards[i].root.anchoredPosition = i == 1 ? new Vector2(slot.x, -slot.y)
                    : boards[i].root.anchoredPosition + new Vector2(slot.x - previous.x, previous.y - slot.y);
            }
            root.SetSiblingIndex(source.root.GetSiblingIndex() + 1);
            view.boards = boards.ToArray();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
    }
}
