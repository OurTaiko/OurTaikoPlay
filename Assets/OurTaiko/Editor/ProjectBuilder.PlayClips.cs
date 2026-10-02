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
