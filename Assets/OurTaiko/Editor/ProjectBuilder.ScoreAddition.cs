using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Score Addition")]
        public static void ApplyScoreAddition()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var clip = ScoreAdditionClip();
            foreach (string name in new[] { "SinglePlayScene", "PracticeScene" })
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + name + ".unity");
                var counter = UnityEngine.Object.FindFirstObjectByType<PlayScene>().scoreCounter;
                if (counter.additionTemplate != null) continue;
                var root = SkinUi.Rect("ScoreAddition", counter.transform, 300, 64);
                root.TopLeft(0, 0);
                var group = root.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
                // The custom JudgeCounter overlaps the source animation's upper edge. Keep the
                // original position, but draw these transient digits above the gameplay HUD.
                var canvas = root.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = counter.GetComponentInParent<Canvas>().sortingOrder + 1;
                var row = root.gameObject.AddComponent<ScoreAdditionView>();
                AttachClip(root.gameObject, clip);
                row.digits = new UnityEngine.UI.Image[10];
                for (int i = 0; i < row.digits.Length; i++)
                {
                    var digit = SkinUi.Image("Digit" + i, root, counter.digits[0], ScoreCounterLayout.DigitWidth, ScoreCounterLayout.DigitHeight);
                    digit.color = new Color32(254, 102, 0, 255);
                    digit.raycastTarget = false;
                    digit.enabled = false;
                    row.digits[i] = digit;
                }
                root.gameObject.SetActive(false);
                counter.additionTemplate = row;
                EditorUtility.SetDirty(counter);
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }

        // Nijiiro Graphics/game/animation.json 35-39. No '+' glyph in ScoreCounterAnimation.
        // score_number's y=-272 is already included in ScoreCounterLayout.DigitTop.
        static AnimationClip ScoreAdditionClip() => SaveClip("ScoreAddition", 1000, false, clip =>
        {
            const float end = .44674f, fanAt = .146f;
            var type = typeof(ScoreAdditionView);
            LinearCurve(clip, "", typeof(CanvasGroup), "m_Alpha", (0, 0), (.05f, 1), (.36674f, 1), (end, 0));
            LinearCurve(clip, "", type, "horizontalOffset", (0, 30), (.08f, 0), (end, 0));
            // At 146 ms the source switches from y=255 to the per-digit fan at y=219.
            HermiteCurve(clip, "", type, "verticalOffset",
                new Keyframe(0, -22.5f, 0, 0), new Keyframe(fanAt, -58.5f, float.PositiveInfinity, 0),
                new Keyframe(.27936f, -58.5f, 0, -3 / .066f),
                new Keyframe(.34536f, -61.5f, -3 / .066f, 0), new Keyframe(end, -61.5f, 0, 0));
            SteppedCurve(clip, "", type, "fanStep", (0, 0), (fanAt, 5), (end, 5));
        });
    }
}
