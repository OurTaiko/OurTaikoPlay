using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Shared helpers for the generated AnimationClips in Generated/Clips. Each builder rewrites its
    // clip in place, so the asset GUID (and every reference to it) survives a rebuild.
    public static partial class ProjectBuilder
    {
        const string ClipFolder = Root + "Generated/Clips/";

        static AnimationClip SaveClip(string name, float frameRate, bool loop, Action<AnimationClip> fill)
        {
            if (!AssetDatabase.IsValidFolder(ClipFolder.TrimEnd('/'))) AssetDatabase.CreateFolder(Root + "Generated", "Clips");
            string path = ClipFolder + name + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = name };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            clip.frameRate = frameRate;
            fill(clip);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssetIfDirty(clip);
            return clip;
        }

        // Adds (or reuses) the Animator and ClipSampler that play a clip on this object.
        static ClipSampler AttachClip(GameObject target, AnimationClip clip)
        {
            if (target.GetComponent<Animator>() == null) target.AddComponent<Animator>();
            var sampler = target.GetComponent<ClipSampler>();
            if (sampler == null) sampler = target.AddComponent<ClipSampler>();
            sampler.clip = clip;
            EditorUtility.SetDirty(sampler);
            return sampler;
        }

        // Cuts frames out of a sheet in the sheet's own importer (Multiple sprite mode), so they live in
        // the PNG's import settings instead of separate sprite assets. Cells are (name, x, y from the
        // top, width, height). Sub-sprites are matched by name: their rect is updated and their sprite
        // ID kept, so a rebuild never breaks references; other sub-sprites of the sheet are kept. A
        // sheet entering Multiple mode starts clean, ignoring stale cuts left in its .meta.
        static Sprite[] SliceSheet(string art, IReadOnlyList<(string name, int x, int y, int width, int height)> cells)
        {
            string path = Root + "Art/" + art + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool wasSheet = importer.spriteImportMode == SpriteImportMode.Multiple;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var current = provider.GetSpriteRects();
            var ids = current.GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
            var rects = wasSheet ? current.ToList() : new List<SpriteRect>();
            importer.GetSourceTextureWidthAndHeight(out _, out int textureHeight);
            foreach (var cell in cells)
            {
                var rect = new Rect(cell.x, textureHeight - cell.y - cell.height, cell.width, cell.height);
                int index = rects.FindIndex(r => r.name == cell.name);
                if (index >= 0 && rects[index].rect == rect) continue;
                var sprite = new SpriteRect
                {
                    name = cell.name, rect = rect,
                    alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f),
                    spriteID = ids.TryGetValue(cell.name, out var id) ? id : GUID.Generate(),
                };
                if (index >= 0) rects[index] = sprite; else rects.Add(sprite);
            }
            bool changed = !wasSheet || current.Length != rects.Count
                || current.Zip(rects, (a, b) => a.name != b.name || a.rect != b.rect || a.spriteID != b.spriteID).Any(d => d);
            if (changed)
            {
                provider.SetSpriteRects(rects.ToArray());
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>()
                    .SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                importer.SaveAndReimport();
            }
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            return cells.Select(c => sprites[c.name]).ToArray();
        }

        // Sprite swaps: frame i from times[i] until the next key (Unity holds the last key one frame).
        static void SpriteKeys(AnimationClip clip, string path, Type type, IReadOnlyList<Sprite> sprites, IReadOnlyList<float> times, string property = "m_Sprite")
        {
            var keys = sprites.Select((sprite, i) => new ObjectReferenceKeyframe { time = times[i], value = sprite }).ToArray();
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(path, type, property), keys);
        }

        // A float curve whose keys hold their value until the next key.
        static void SteppedCurve(AnimationClip clip, string path, Type type, string property, params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.time, k.value)).ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        // Straight lines between keys.
        static void LinearCurve(AnimationClip clip, string path, Type type, string property, params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.time, k.value)).ToArray());
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }

        // Exact keys as given, tangents included (for eased segments).
        static void HermiteCurve(AnimationClip clip, string path, Type type, string property, params Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyBroken(curve, i, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Free);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
        }
    }
}
