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

        [MenuItem("OurTaiko/Apply Branch Change Clip")]
        public static void ApplyBranchChangeClip() => EditPlayScene(play => AttachClip(play.branchLane.gameObject, BranchChangeClip()));

        // Nijiiro game/animation.json IDs 41-45 for a route change: the old label nudges 30 px
        // (ease-out, 100 ms), then both labels slide 105 px (ease-out, 133 ms) while crossfading,
        // the route background fades to half, and the level badge grows to 1.2 and back over
        // 116 + 116 ms, holding until it fades out over 1276-1392 ms. Offsets are in the change's
        // direction from the labels' saved position (BranchLaneView signs them).
        static AnimationClip BranchChangeClip() => SaveClip("BranchChange", 60, false, clip =>
        {
            const float nudge = 0.1f, slideEnd = 0.1f + 0.133f, pulse = 0.116f, fadeOut = 1.276f, end = 1.392f;
            float slideSlope = 2 * -105 / (slideEnd - nudge);
            var view = typeof(BranchLaneView);
            var image = typeof(UnityEngine.UI.Image);
            HermiteCurve(clip, "", view, "previousOffset",
                new Keyframe(0, 0, 0, 2 * 30 / nudge), new Keyframe(nudge, 30, 0, slideSlope), new Keyframe(slideEnd, -75, 0, 0));
            HermiteCurve(clip, "", view, "currentOffset",
                new Keyframe(0, 105, 0, 0), new Keyframe(nudge, 105, 0, slideSlope), new Keyframe(slideEnd, 0, 0, 0));
            LinearCurve(clip, "PreviousRoute", image, "m_Color.a", (0, 1), (nudge, 1), (slideEnd, 0));
            SteppedCurve(clip, "PreviousRoute", image, "m_Enabled", (0, 1), (slideEnd, 0));
            LinearCurve(clip, "CurrentRoute", image, "m_Color.a", (0, 0), (nudge, 0), (slideEnd, 1));
            LinearCurve(clip, "RouteBackground", image, "m_Color.a", (0, 0), (nudge, 0), (nudge + 0.0665f, 0.5f), (end, 0.5f));
            LinearCurve(clip, "LevelChange", image, "m_Color.a", (0, 0), (pulse, 1), (fadeOut, 1), (end, 0));
            foreach (var axis in new[] { "x", "y" })
                LinearCurve(clip, "LevelChange", typeof(Transform), "m_LocalScale." + axis, (0, 1), (pulse, 1.2f), (2 * pulse, 1), (end, 1));
        });

        [MenuItem("OurTaiko/Apply Text Stretch Clip")]
        public static void ApplyTextStretchClip() => EditPlayScene(play =>
        {
            AttachTextStretch(play.scoreCounter.gameObject);
            AttachTextStretch(play.balloonCounter.gameObject);
        });

        // The digit rows read their stretch from an AnimatedFloat that TextStretch.anim writes.
        static void AttachTextStretch(GameObject target)
        {
            if (target.GetComponent<AnimatedFloat>() == null) target.AddComponent<AnimatedFloat>();
            AttachClip(target, TextStretchClip());
        }

        // TextStretchAnimation (score counter id 4, balloon counter id 6), in pixels the digits grow
        // upwards: 2 + 0.2 per whole millisecond up to 12 at 50 ms, then 10 and 2 less every 16.57 ms
        // (overshooting to -2 and -4 for its last steps, as the original does) and 0 after 166 ms.
        static AnimationClip TextStretchClip() => SaveClip("TextStretch", 1000, false, clip =>
        {
            const float after = 0.00001f;
            var keys = Enumerable.Range(0, 51).Select(ms => (ms / 1000f, 2 + 0.2f * ms)).ToList();
            keys.Add((0.05f + after, 10));
            for (int k = 1; k <= 7; k++) keys.Add((0.05f + 0.01657f * k, 10 - 2 * k));
            keys.Add((0.166f + after, 0));
            SteppedCurve(clip, "", typeof(AnimatedFloat), "value", keys.ToArray());
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
