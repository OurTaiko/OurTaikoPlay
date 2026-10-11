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
        [MenuItem("OurTaiko/Apply Timing Analysis")]
        public static void ApplyTimingAnalysis()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(ResultPath);
                var result = UnityEngine.Object.FindFirstObjectByType<ResultScene>();
                if (result.view.analysis == null)
                {
                    BuildTimingAnalysis(result);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                foreach (string name in new[] { "SinglePlayScene", "PracticeScene" })
                {
                    scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                    var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                    if (play.timingFeedback != null) continue;
                    var root = SkinUi.Rect("TimingFeedback", play.combo.transform.parent, 260, 60);
                    // Lane top is y=276; score additions sit near screen y=219. Centre above the combo drum.
                    root.Center(401, -36);
                    var view = root.gameObject.AddComponent<TimingFeedbackView>();
                    view.label = SkinUi.Text("Offset", root, 38);
                    view.label.rectTransform.sizeDelta = new Vector2(260, 60);
                    view.label.rectTransform.Center(130, 30);
                    view.label.text = "+0ms"; view.label.alpha = 0;
                    var canvas = root.gameObject.AddComponent<Canvas>();
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = play.scoreCounter.GetComponentInParent<Canvas>().sortingOrder + 1;
                    play.timingFeedback = view;
                    EditorUtility.SetDirty(play);
                    EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static void BuildTimingAnalysis(ResultScene result)
        {
            var stage = result.stage;
            var view = result.view;
            var oldTouch = stage.Find("TouchArea");
            if (oldTouch != null) UnityEngine.Object.DestroyImmediate(oldTouch.gameObject);
            var shared = new[] { view.background.group.transform, view.fadeIn.group.transform,
                view.songTitle.transform, view.songNumber.transform, view.freePlay.transform.parent };
            var scoreChildren = stage.Cast<Transform>().Where(child => !shared.Contains(child)).ToArray();
            var scoreRoot = SkinUi.Rect("ScorePage", stage, 1920, 1080); scoreRoot.TopLeft(0, 0);
            scoreRoot.SetSiblingIndex(view.fadeIn.group.transform.GetSiblingIndex());
            foreach (var child in scoreChildren)
            {
                child.SetParent(scoreRoot, false);
                if (PrefabUtility.IsPartOfPrefabInstance(child)) PrefabUtility.RecordPrefabInstancePropertyModifications(child);
            }
            var detailRoot = SkinUi.Rect("TimingPage", stage, 1920, 1080); detailRoot.TopLeft(0, 0);
            detailRoot.SetSiblingIndex(scoreRoot.GetSiblingIndex() + 1);
            var navigation = SkinUi.Rect("ResultNavigation", stage, 1920, 1080); navigation.TopLeft(0, 0);
            navigation.SetSiblingIndex(view.fadeIn.group.transform.GetSiblingIndex());
            var analysis = navigation.gameObject.AddComponent<ResultAnalysisView>();
            view.analysis = analysis;
            analysis.scorePage = scoreRoot.gameObject.AddComponent<CanvasGroup>();
            analysis.detailPage = detailRoot.gameObject.AddComponent<CanvasGroup>();
            analysis.scorePage.blocksRaycasts = analysis.detailPage.blocksRaycasts = false;
            analysis.scorePage.interactable = analysis.detailPage.interactable = false;

            Color dark = new Color32(44, 37, 31, 255), cream = new Color32(255, 250, 232, 255);
            AnalysisBox("Border", detailRoot, 40, 202, 884, 776, dark);
            AnalysisBox("Paper", detailRoot, 49, 211, 866, 758, cream);
            AnalysisBox("Header", detailRoot, 58, 220, 848, 70, new Color32(235, 77, 31, 255));
            AnalysisText("Title", detailRoot, "判定详情", 42, 482, 255, 700, Color.white);
            string[] captions = { "平均偏差", "标准差", "平均绝对偏差" };
            var metrics = new TextMeshProUGUI[3];
            for (int i = 0; i < 3; i++)
            {
                float x = 204 + 278 * i;
                AnalysisText("MetricCaption" + i, detailRoot, captions[i], 28, x, 330, 266, dark);
                metrics[i] = AnalysisText("MetricValue" + i, detailRoot, "—", 40, x, 379, 266, dark);
            }
            analysis.mean = metrics[0]; analysis.deviation = metrics[1]; analysis.absolute = metrics[2];
            AnalysisText("YAxisTitle", detailRoot, "次数", 24, 112, 432, 85, dark);
            var graph = SkinUi.Rect("Histogram", detailRoot, 744, 300); graph.TopLeft(135, 460);
            analysis.histogram = graph.gameObject.AddComponent<TimingHistogramGraphic>();
            analysis.histogram.raycastTarget = false;
            analysis.yLabels = new TextMeshProUGUI[5];
            for (int i = 0; i < 5; i++)
                analysis.yLabels[i] = AnalysisText("Y" + i, detailRoot, "0", 24, 98, 760 - i * 75, 65, dark);
            for (int i = -2; i <= 2; i++)
            {
                int ms = i * 50;
                float x = 135 + (float)((ms - TimingStatistics.MinimumMs) / 265) * 744;
                AnalysisText("X" + i, detailRoot, ms.ToString("+0;-0;0"), 24, x, 784, 100, dark);
            }
            AnalysisText("AxisTitle", detailRoot, "早 ← 偏差 ms → 晚    每格 5 ms", 25, 507, 827, 750, dark);
            analysis.meanLegend = AnalysisText("MeanLegend", detailRoot, "虚线：平均偏差", 23, 676, 432, 420, new Color32(133, 38, 135, 255));
            AnalysisText("Windows", detailRoot, "底色：<color=#9A7700>良</color> / <color=#B45329>可</color> / <color=#3975A0>不可</color>", 23, 317, 432, 330, dark);
            analysis.counts = AnalysisText("Counts", detailRoot, "有效击打 0    漏打 0", 28, 482, 875, 800, dark);
            analysis.sides = AnalysisText("Sides", detailRoot, "早 0    晚 0", 28, 482, 920, 800, dark);
            analysis.empty = AnalysisText("Empty", detailRoot, "暂无有效击打", 32, 507, 600, 720, dark);
            // The only panel-sized raycast surface handles swipes, never clicks or graph selection.
            analysis.swipeArea = AnalysisControl("SwipeArea", navigation, 40, 180, 884, 798, Color.clear);

            AnalysisBox("PageNavigationBack", navigation, 390, 990, 354, 64, cream);
            analysis.previous = AnalysisControl("Previous", navigation, 390, 990, 52, 64, Color.clear);
            AnalysisText("Arrow", analysis.previous.transform, "‹", 38, 26, 32, 48, dark);
            analysis.next = AnalysisControl("Next", navigation, 692, 990, 52, 64, Color.clear);
            AnalysisText("Arrow", analysis.next.transform, "›", 38, 26, 32, 48, dark);
            analysis.pageName = AnalysisText("PageName", navigation, "成绩", 28, 518, 1022, 154, dark);
            analysis.scoreDot = AnalysisControl("ScoreDot", navigation, 592, 990, 50, 64, Color.clear);
            analysis.detailDot = AnalysisControl("DetailDot", navigation, 642, 990, 50, 64, Color.clear);
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            analysis.dots = new[] { analysis.scoreDot, analysis.detailDot }.Select(control =>
            {
                var dot = SkinUi.Image("Dot", control.transform, knob, 18, 18); dot.rectTransform.Center(25, 32); return dot;
            }).ToArray();
            AnalysisBox("ActionBorder", navigation, 776, 990, 368, 76, dark);
            analysis.action = AnalysisControl("Continue", navigation, 782, 996, 356, 64, new Color32(236, 78, 33, 255));
            analysis.actionLabel = AnalysisText("Label", analysis.action.transform, "跳过演出", 34, 178, 32, 346, Color.white);
            view.freePlay.rectTransform.Center(1510, 1046);
            analysis.Bind(new PlayResult()); analysis.SetReady(false);
            PersistSongSelectTextMaterials(stage);
            EditorUtility.SetDirty(view);
        }

        static UnityEngine.UI.Image AnalysisBox(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var image = SkinUi.Image(name, parent, null, width, height);
            image.rectTransform.TopLeft(x, y); image.color = color; return image;
        }
        static TextMeshProUGUI AnalysisText(string name, Transform parent, string value, float size, float x, float y, float width, Color color)
        {
            var text = SkinUi.Text(name, parent, size); text.text = value; text.color = color; text.UseUiFont();
            text.rectTransform.sizeDelta = new Vector2(width, size * 1.6f); text.rectTransform.Center(x, y); return text;
        }
        static ResultPointerControl AnalysisControl(string name, Transform parent, float x, float y, float width, float height, Color color)
        {
            var image = AnalysisBox(name, parent, x, y, width, height, color); image.raycastTarget = true;
            return image.gameObject.AddComponent<ResultPointerControl>();
        }
    }
}
