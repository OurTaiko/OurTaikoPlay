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

        // Nijiiro Graphics/game/animation.json 35-39 and 40. No '+' glyph in ScoreCounterAnimation.
        // User decision: each addition slides in from the right at its final height (no y255 start
        // and 146 ms jump to y219), stays one level row (no per-digit (i + 1) * 5 px fan) and rises
        // 15 px while fading out (the timing of the source's otherwise unused animation 40).
        // score_number's y=-272 is already included in ScoreCounterLayout.DigitTop.
        static AnimationClip ScoreAdditionClip() => SaveClip("ScoreAddition", 1000, false, clip =>
        {
            const float end = .44674f, fadeOut = .36674f;
            var type = typeof(ScoreAdditionView);
            LinearCurve(clip, "", typeof(CanvasGroup), "m_Alpha", (0, 0), (.05f, 1), (fadeOut, 1), (end, 0));
            LinearCurve(clip, "", type, "horizontalOffset", (0, 30), (.08f, 0), (end, 0));
            // y=219 throughout, the 3 px lift at 279.36-345.36 ms, then 15 px up during the fade out.
            HermiteCurve(clip, "", type, "verticalOffset",
                new Keyframe(0, -58.5f, 0, 0),
                new Keyframe(.27936f, -58.5f, 0, -3 / .066f),
                new Keyframe(.34536f, -61.5f, -3 / .066f, 0),
                new Keyframe(fadeOut, -61.5f, 0, -15 / .08f), new Keyframe(end, -76.5f, -15 / .08f, 0));
        });
    }
}
