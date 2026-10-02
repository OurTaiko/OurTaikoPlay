using System;
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
