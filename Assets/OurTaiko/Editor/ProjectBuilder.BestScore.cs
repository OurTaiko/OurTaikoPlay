using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Song Best Score")]
        public static void ApplySongBestScore()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene first.");
            // Reuse the opaque gameplay sheet, not the 60%-alpha course-select watermark.
            var icons = LaneDifficultySprites();
            var scene = EditorSceneManager.OpenScene(SongSelectPath);
            var select = UnityEngine.Object.FindFirstObjectByType<SongSelectScene>();
            if (select.view.bestScore != null && select.view.bestScore.judgments != null)
            {
                var existing = select.view.bestScore;
                if (!existing.difficultySprites.SequenceEqual(icons) || existing.group.alpha == 0)
                {
                    int shown = Array.IndexOf(existing.difficultySprites, existing.difficulty.sprite);
                    existing.difficultySprites = icons;
                    existing.difficulty.rectTransform.sizeDelta = new Vector2(64, 48);
                    existing.difficulty.rectTransform.TopLeft(10, 12);
                    existing.difficulty.sprite = existing.difficultySprites[Mathf.Max(0, shown)];
                    PreviewBestScore(existing);
                    EditorUtility.SetDirty(existing);
                    EditorUtility.SetDirty(existing.difficulty);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                AssetDatabase.DeleteAsset(Root + "Art/song_select/best_score");
                return;
            }
            var playScene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity", OpenSceneMode.Additive);
            try
            {
                var source = playScene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<JudgeCounterView>(true)).Single();
                var view = select.view.bestScore;
                RectTransform root;
                if (view == null)
                {
                    root = SkinUi.Rect("BestScore", select.view.transform, 310, 300);
                    root.TopLeft(24, 585);
                    view = root.gameObject.AddComponent<SongBestScoreView>();
                    view.group = root.gameObject.AddComponent<CanvasGroup>();
                }
                else
                {
                    root = (RectTransform)view.transform;
                    var topLeft = root.anchoredPosition - new Vector2(root.rect.width * root.pivot.x, -root.rect.height * (1 - root.pivot.y));
                    root.sizeDelta = new Vector2(310, 300);
                    root.TopLeft(topLeft.x, -topLeft.y);
                }
                view.group.alpha = 0; view.group.blocksRaycasts = false; view.group.interactable = false;
                // Replace only the old window's styling; the rest of SongSelect stays untouched.
                var panel = root.Find("Background")?.GetComponent<UnityEngine.UI.Image>()
                    ?? SkinUi.Image("Background", root, null);
                panel.sprite = source.GetComponent<UnityEngine.UI.Image>().sprite;
                panel.type = UnityEngine.UI.Image.Type.Sliced;
                panel.rectTransform.sizeDelta = root.sizeDelta;
                panel.rectTransform.TopLeft(0, 0);
                if (panel.TryGetComponent<Outline>(out var border)) UnityEngine.Object.DestroyImmediate(border);
                if (root.Find("GoldStripe") is Transform gold) UnityEngine.Object.DestroyImmediate(gold.gameObject);
                var bar = SkinUi.Image("CaptionBar", root, source.transform.Find("Good/Bar").GetComponent<UnityEngine.UI.Image>().sprite, 216, 34);
                bar.rectTransform.TopLeft(76, 18);
                var title = root.Find("Caption")?.GetComponent<TMPro.TextMeshProUGUI>() ?? SkinUi.Text("Caption", root, 28);
                title.transform.SetAsLastSibling();
                title.text = "自己ベスト"; title.fontSize = 28; title.color = Color.white; title.UseUiFont();
                title.rectTransform.sizeDelta = new Vector2(216, 34); title.rectTransform.TopLeft(76, 18);
                view.difficultySprites = icons;
                view.difficulty ??= SkinUi.Image("Difficulty", root, null, 64, 48);
                view.difficulty.sprite = view.difficultySprites[3];
                view.difficulty.preserveAspect = true; view.difficulty.rectTransform.TopLeft(10, 12);
                view.numbers = source.digits;
                view.scoreCount = SkinUi.Rect("Score", root, 260, 42);
                view.scoreCount.TopLeft(28, 60);
                if (view.digits == null || view.digits.Length == 0)
                    view.digits = Enumerable.Range(0, 10).Select(i => SkinUi.Image("Digit" + i, view.scoreCount, null)).ToArray();
                foreach (var digit in view.digits)
                {
                    digit.transform.SetParent(view.scoreCount, false);
                    if (digit.TryGetComponent<Outline>(out var outline)) UnityEngine.Object.DestroyImmediate(outline);
                    JudgeCounterView.PlaceDigit(digit, view.numbers[0], 0, 1, 42, source.pitch);
                    digit.enabled = false;
                }
                view.digits[view.digits.Length - 1].enabled = true;
                // Clone the actual saved JudgeCounter, including its labels, bars, digit sprites and component.
                view.judgments = UnityEngine.Object.Instantiate(source, root, false);
                view.judgments.name = "Judgments";
                UnityEngine.Object.DestroyImmediate(view.judgments.GetComponent<UnityEngine.UI.Image>());
                var stats = (RectTransform)view.judgments.transform;
                stats.anchorMin = stats.anchorMax = stats.pivot = new Vector2(0, 1);
                stats.anchoredPosition = new Vector2(0, -106);
                stats.localScale = Vector3.one * (root.rect.width / stats.rect.width);
                PreviewBestScore(view);
                select.view.bestScore = view;
                EditorUtility.SetDirty(view); EditorUtility.SetDirty(select.view);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.CloseScene(playScene, true); }
            AssetDatabase.DeleteAsset(Root + "Art/song_select/best_score");
        }
        // Editor-only sample content: no globals, player files, or runtime score lookup.
        public static void PreviewBestScore(SongBestScoreView view)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Preview is only available in Edit mode.");
            view.group.alpha = 1;
            view.difficulty.sprite = view.difficultySprites[(int)Difficulty.Oni];
            view.difficulty.color = Color.white;
            const string score = "1002540";
            for (int i = 0; i < view.digits.Length; i++)
            {
                int position = i - (view.digits.Length - score.Length);
                view.digits[i].enabled = position >= 0;
                if (position >= 0)
                    JudgeCounterView.PlaceDigit(view.digits[i], view.numbers[score[position] - '0'], position,
                        score.Length, view.scoreCount.rect.height, view.judgments.pitch);
            }
            view.judgments.Show(853, 14, 2, 35);
        }
    }
}
