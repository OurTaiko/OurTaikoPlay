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

        // Moves the clip frames that were separate sprite assets in Generated (hit rings, soul
        // rainbows, nameplate rainbow, gauge hit burst) into their sheets' importers, rebuilds the
        // clips and the images that showed a first frame, then deletes the old sprite assets.
        [MenuItem("OurTaiko/Apply Frame Sheets")]
        public static void ApplyFrameSheets()
        {
            ApplyHitEffects();
            ApplySoulRainbowClips();
            ApplyNameplateRainbowClip();
            ApplyNoteArcs();
            var old = new[] { "outer_good", "outer_ok", "outer_good_big", "outer_ok_big" }
                .SelectMany(strip => Enumerable.Range(0, 4).Select(i => "HitRing_" + strip + i))
                .Concat(new[] { "easy", "normal", "hard" }.SelectMany(tier => Enumerable.Range(0, 8).Select(i => "Rainbow" + tier + i)))
                .Concat(Enumerable.Range(0, 6).Select(i => "NameplateRainbow" + i))
                .Concat(Enumerable.Range(0, 3).Select(i => "GaugeHitEffect" + i));
            foreach (var name in old) AssetDatabase.DeleteAsset(Root + "Generated/" + name + ".asset");
            AssetDatabase.SaveAssets();
        }

        // Cuts an animation's frames out of its sheet in the sheet's own importer (Multiple sprite mode),
        // so the frames live in the PNG's import settings instead of separate sprite assets. Cells
        // are (name, x, y from the top, width, height); existing sprite IDs are kept by name, so a
        // rebuild never breaks the clips that reference them.
        static Sprite[] SliceSheet(string art, IReadOnlyList<(string name, int x, int y, int width, int height)> cells)
        {
            string path = Root + "Art/" + art + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            bool changed = importer.spriteImportMode != SpriteImportMode.Multiple;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects().GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
            importer.GetSourceTextureWidthAndHeight(out _, out int textureHeight);
            var rects = cells.Select(cell => new SpriteRect
            {
                name = cell.name,
                rect = new Rect(cell.x, textureHeight - cell.y - cell.height, cell.width, cell.height),
                alignment = SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
                spriteID = existing.TryGetValue(cell.name, out var id) ? id : GUID.Generate(),
            }).ToArray();
            var current = provider.GetSpriteRects();
            changed |= current.Length != rects.Length
                || current.Zip(rects, (a, b) => a.name != b.name || a.rect != b.rect || a.spriteID != b.spriteID).Any(d => d);
            if (changed)
            {
                provider.SetSpriteRects(rects);
                provider.GetDataProvider<ISpriteNameFileIdDataProvider>()
                    .SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
                provider.Apply();
                importer.SaveAndReimport();
            }
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(s => s.name);
            return rects.Select(r => sprites[r.name]).ToArray();
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

        // Ease-out (p(2 - p)) from a to b over [t0, t1] as two Hermite keys: the cubic through these
        // values and slopes is that quadratic exactly. Constant before t0 and after t1.
        static Keyframe[] EaseOutKeys(float t0, float t1, float a, float b)
        {
            float slope = 2 * (b - a) / (t1 - t0);
            return new[] { new Keyframe(t0, a, 0, slope), new Keyframe(t1, b, 0, 0) };
        }
    }
}
