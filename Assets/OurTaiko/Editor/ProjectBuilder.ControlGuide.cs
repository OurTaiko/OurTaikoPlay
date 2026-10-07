using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const float GuideFrameRate = 30;

        // Moves the control guide from 115 separate sprite assets to one clip: the frames are
        // sliced in the guide sheet's own import settings and ControlGuide.anim swaps them.
        [MenuItem("OurTaiko/Apply Control Guide Clip")]
        public static void ApplyControlGuideClip()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before editing scenes.");
            var clip = ControlGuideClip();
            foreach (var path in new[] { EntryPath, "Assets/Scenes/SongSelect.unity", "Assets/Scenes/Result.unity" })
            {
                var scene = EditorSceneManager.OpenScene(path);
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var entry in root.GetComponentsInChildren<EntryScene>(true)) SetGuideClip(entry, entry.overlay, clip);
                    foreach (var select in root.GetComponentsInChildren<SongSelectScene>(true)) SetGuideClip(select, select.overlay, clip);
                    foreach (var result in root.GetComponentsInChildren<ResultScene>(true)) SetGuideClip(result, result.overlay, clip);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
        }

        static void SetGuideClip(UnityEngine.Object owner, ArcadeOverlayArt overlay, AnimationClip clip)
        {
            if (overlay == null) return;
            overlay.guideClip = clip;
            EditorUtility.SetDirty(owner);
        }

        // ControlGuide.anim: one Image.sprite key per cell at 30 fps, looping. Unity gives the last
        // sprite key a full frame, so the clip is 115/30 s; ControlGuideView samples mid-frame.
        static AnimationClip ControlGuideClip()
        {
            var frames = SliceControlGuide();
            return SaveClip("ControlGuide", GuideFrameRate, true, clip =>
                SpriteKeys(clip, "", typeof(UnityEngine.UI.Image), frames, frames.Select((_, i) => i / GuideFrameRate).ToArray()));
        }

        // The decide-loop cells (210-324), sliced in the guide sheet's own importer.
        static Sprite[] SliceControlGuide() => SliceSheet(GuideSheet, Enumerable.Range(GuideFirstDecide, GuideCells - GuideFirstDecide)
            .Select(cell => ($"ControlGuide{cell:000}", cell % GuideColumns * 352, cell / GuideColumns * 276, 352, 276)).ToArray());
    }
}
