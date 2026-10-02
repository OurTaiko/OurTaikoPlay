using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The time-driven visuals stored as clips in Generated/Clips: each is checked by sampling the
    // asset itself through ClipSampler, the same way the views play it.
    public sealed class AnimationClipTests
    {
        const string Clips = "Assets/OurTaiko/Generated/Clips/";
        const string PlayScenePath = "Assets/Scenes/SinglePlayScene.unity";

        static AnimationClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + name + ".anim");
            Assert.That(clip, Is.Not.Null, name);
            return clip;
        }

        // Builds a throwaway object tree, samples the clip on its root and reads the result.
        static T Sampled<T>(AnimationClip clip, double seconds, Func<GameObject, T> read, params string[] children)
        {
            var root = new GameObject("Root", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
            try
            {
                foreach (var child in children)
                {
                    var go = new GameObject(child, typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    go.transform.SetParent(root.transform, false);
                }
                var sampler = ClipSampler.Attach(root, clip);
                sampler.Sample(seconds);
                return read(root);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Sprite SpriteAt(AnimationClip clip, double seconds, string path = "")
            => Sampled(clip, seconds, go => (path == "" ? go.transform : go.transform.Find(path)).GetComponent<UnityEngine.UI.Image>().sprite,
                path == "" ? new string[0] : new[] { path });

        static void WithPlayScene(Action<PlayScene> check)
        {
            var scene = EditorSceneManager.OpenPreviewScene(PlayScenePath);
            try { check(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single()); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void DancersLoopTheirNineteenFramesAtEightFps()
        {
            var clip = Clip("Dancer");
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.length, Is.EqualTo(19 / 8f).Within(1e-4));
            foreach (var (t, frame) in new[] { (0.0, 0), (0.124, 0), (0.125, 1), (1.0, 8), (2.374, 18) })
                Assert.That(SpriteAt(clip, t).name, Is.EqualTo(frame.ToString()), $"{t} s");
            WithPlayScene(play =>
            {
                Assert.That(play.dancers.Length, Is.EqualTo(5));
                Assert.That(play.dancers.All(d => d.clip == clip && d.GetComponent<Animator>() != null), Is.True);
            });
        }

        [Test]
        public void DrumFlashStaysLitFor120Ms()
        {
            var clip = Clip("DrumFlash");
            Assert.That(clip.isLooping, Is.False);
            foreach (var (t, lit) in new[] { (0.0, true), (0.119, true), (0.12, false), (1.0, false) })
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.EqualTo(lit), $"{t} s");
            WithPlayScene(play => Assert.That(play.drumFlashes.All(f => f.GetComponent<ClipSampler>().clip == clip), Is.True));
        }
    }
}
