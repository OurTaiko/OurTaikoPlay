using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Play Score Rank")]
        public static void ApplyPlayScoreRank()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var clip = PlayScoreRankClip();
                foreach (string name in new[] { "SinglePlayScene", "PracticeScene" })
                {
                    var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                    var counter = UnityEngine.Object.FindFirstObjectByType<PlayScene>().scoreCounter;
                    var view = counter.scoreRank;
                    if (view == null)
                    {
                        var root = SkinUi.Rect("PlayScoreRank", counter.transform, 0, 0);
                        // rank_badge POS[1] (152, 276 - 118), relative to the lane at y=276.
                        root.TopLeft(152, -118);
                        view = root.gameObject.AddComponent<PlayScoreRankView>();
                        AttachClip(root.gameObject, clip);
                        view.rank = CreateRankView(root, 208);
                        ((RectTransform)view.rank.transform).Center(0, 0);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(view.rank.transform);
                        view.rank.Show(0);
                        counter.scoreRank = view;
                    }
                    // Keep the source's full-size badge in front of JudgeCounter and
                    // the floating score additions, without moving the existing HUD.
                    var canvas = view.GetComponent<Canvas>();
                    if (canvas == null) canvas = view.gameObject.AddComponent<Canvas>();
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = counter.GetComponentInParent<Canvas>().sortingOrder + 2;
                    EditorUtility.SetDirty(canvas);
                    EditorUtility.SetDirty(counter);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        // Exported 60 fps rank_hud frames, compiled to a native Unity clip. The HUD
        // plates match the seven shared result icons; no duplicate icon assets.
        static AnimationClip PlayScoreRankClip()
        {
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "Animations/rank_hud.txt");
            var frames = ScoreRankAnimation.Frames.Parse(source.text).FramesByIndex;
            return SaveClip("PlayScoreRank", 60, false, clip =>
            {
                (float, float)[] Keys(int column, float fallback, float sign = 1) => frames.Select((rows, i) =>
                    (i / 60f, rows.Length == 0 ? fallback : sign * rows[0][column])).ToArray();
                SteppedCurve(clip, "ScoreRank", typeof(CanvasGroup), "m_Alpha", Keys(6, 0));
                SteppedCurve(clip, "ScoreRank", typeof(RectTransform), "m_AnchoredPosition.x", Keys(1, 0));
                SteppedCurve(clip, "ScoreRank", typeof(RectTransform), "m_AnchoredPosition.y", Keys(2, 0, -1));
                SteppedCurve(clip, "ScoreRank", typeof(Transform), "m_LocalScale.x", Keys(3, 1));
                SteppedCurve(clip, "ScoreRank", typeof(Transform), "m_LocalScale.y", Keys(4, 1));
                SteppedCurve(clip, "ScoreRank", typeof(Transform), "localEulerAnglesRaw.z", Keys(5, 0, -1));
            });
        }
    }
}
