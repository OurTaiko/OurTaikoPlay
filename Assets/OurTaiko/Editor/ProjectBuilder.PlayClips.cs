using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Clips for SinglePlayScene's lane feedback.
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Apply Drum Flash Clip")]
        public static void ApplyDrumFlashClip() => EditPlayScene(play =>
        {
            var clip = DrumFlashClip();
            foreach (var flash in play.drumFlashes) AttachClip(flash.gameObject, clip);
        });

        // DrumFlash.anim: the struck drum face is lit for 0.12 s.
        static AnimationClip DrumFlashClip() => SaveClip("DrumFlash", 60, false, clip =>
            SteppedCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Enabled", (0, 1), (0.12f, 0)));

        [MenuItem("OurTaiko/Apply Judgment Fade Clip")]
        public static void ApplyJudgmentFadeClip() => EditPlayScene(play =>
        {
            // The clip owns only alpha; the text is drawn untinted.
            play.judgment.color = new Color(1, 1, 1, 0);
            EditorUtility.SetDirty(play.judgment);
            AttachClip(play.judgment.gameObject, JudgmentFadeClip());
        });

        // JudgmentFade.anim: the 良／可／不可 text fades out linearly over 0.25 s.
        static AnimationClip JudgmentFadeClip() => SaveClip("JudgmentFade", 60, false, clip =>
            LinearCurve(clip, "", typeof(UnityEngine.UI.Image), "m_Color.a", (0, 1), (0.25f, 0)));

        [MenuItem("OurTaiko/Apply Gogo Pulse Clip")]
        public static void ApplyGogoPulseClip() => EditPlayScene(play => AttachClip(play.gogoTint.gameObject, GogoPulseClip()));

        // GogoPulse.anim: the lane tint's alpha 0.18 + 0.05 sin(12 t), one period, looping. Keys carry
        // the exact slope, so the Hermite segments follow the sine to within 1e-6.
        static AnimationClip GogoPulseClip() => SaveClip("GogoPulse", 60, true, clip =>
        {
            const int keys = 24;
            float period = 2 * Mathf.PI / 12;
            HermiteCurve(clip, "", typeof(CanvasGroup), "m_Alpha", Enumerable.Range(0, keys + 1).Select(i =>
            {
                float t = period * i / keys;
                float slope = 0.05f * 12 * Mathf.Cos(12 * t);
                return new Keyframe(t, 0.18f + 0.05f * Mathf.Sin(12 * t), slope, slope);
            }).ToArray());
        });

        static void EditPlayScene(Action<PlayScene> edit)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SinglePlayScene.unity");
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            edit(play);
            EditorUtility.SetDirty(play);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
