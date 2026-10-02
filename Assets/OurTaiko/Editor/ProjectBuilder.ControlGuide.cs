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
        const string ControlGuideClipPath = Root + "Generated/ControlGuide.anim";
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
            // The old per-frame assets are no longer referenced.
            for (int cell = GuideFirstDecide; cell < GuideCells; cell++)
                AssetDatabase.DeleteAsset($"{Root}Generated/ControlGuide{cell:000}.asset");
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
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ControlGuideClipPath);
            if (clip == null)
            {
                clip = new AnimationClip { name = "ControlGuide" };
                AssetDatabase.CreateAsset(clip, ControlGuideClipPath);
            }
            clip.frameRate = GuideFrameRate;
            var keys = frames.Select((sprite, i) => new ObjectReferenceKeyframe { time = i / GuideFrameRate, value = sprite }).ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(UnityEngine.UI.Image), "m_Sprite"), keys);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        // Slices the decide-loop cells (210-324) in the guide sheet's importer. Existing sprite IDs
        // are kept by name, so reapplying never breaks the clip's references.
        static Sprite[] SliceControlGuide()
        {
            string path = Root + "Art/" + GuideSheet + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool changed = importer.spriteImportMode != SpriteImportMode.Multiple;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
            int height = 0;
            importer.GetSourceTextureWidthAndHeight(out _, out height);
            var rects = new List<SpriteRect>();
            for (int cell = GuideFirstDecide; cell < GuideCells; cell++)
            {
                string name = $"ControlGuide{cell:000}";
                int x = cell % GuideColumns * 352, y = cell / GuideColumns * 276;
                rects.Add(new SpriteRect
                {
                    name = name,
                    rect = new Rect(x, height - y - 276, 352, 276),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    spriteID = existing.TryGetValue(name, out var id) ? id : GUID.Generate(),
                });
            }
            changed |= provider.GetSpriteRects().Length != rects.Count
                || provider.GetSpriteRects().Zip(rects, (a, b) => a.name != b.name || a.rect != b.rect || a.spriteID != b.spriteID).Any(d => d);
            if (changed)
            {
                provider.SetSpriteRects(rects.ToArray());
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>()
                    .SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                importer.SaveAndReimport();
            }
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            return rects.Select(r => sprites[r.name]).ToArray();
        }
    }
}
