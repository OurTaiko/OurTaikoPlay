using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
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
