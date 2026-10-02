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

        [Test]
        public void JudgmentTextFadesOutOver250Ms()
        {
            var clip = Clip("JudgmentFade");
            foreach (var (t, alpha) in new[] { (0.0, 1f), (0.125, 0.5f), (0.25, 0f), (1.0, 0f) })
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().color.a), Is.EqualTo(alpha).Within(1e-4), $"{t} s");
            WithPlayScene(play =>
            {
                Assert.That(play.judgment.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
                Assert.That((Vector3)(Vector4)play.judgment.color, Is.EqualTo(Vector3.one), "The clip only drives alpha.");
            });
        }

        [Test]
        public void GogoTintPulsesLikeTheSine()
        {
            var clip = Clip("GogoPulse");
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.length, Is.EqualTo(2 * Mathf.PI / 12).Within(1e-4));
            for (int i = 0; i <= 50; i++)
            {
                double t = clip.length * i / 50;
                Assert.That(Sampled(clip, t, go => go.GetComponent<CanvasGroup>().alpha),
                    Is.EqualTo(0.18f + Mathf.Sin((float)t * 12) * 0.05f).Within(1e-4), $"{t} s");
            }
            WithPlayScene(play => Assert.That(play.gogoTint.GetComponent<ClipSampler>().clip, Is.SameAs(clip)));
        }

        [Test]
        public void HitFaceFollowsNijiiroAnimation28()
        {
            var clip = Clip("HitFace");
            Assert.That(clip.length, Is.EqualTo(0.35).Within(1e-3));
            foreach (var (t, alpha) in new[] { (0.0, 0.5f), (0.03335, 0.75f), (0.0667, 1f), (0.2, 1f), (0.2833 + 0.03335, 0.75f), (0.3499, 0.5f) })
            {
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().color.a), Is.EqualTo(alpha).Within(1e-3), $"{t} s");
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.True, $"{t} s");
            }
            Assert.That(Sampled(clip, 0.35, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.False);
            var prefab = AssetDatabase.LoadAssetAtPath<HitFaceView>("Assets/OurTaiko/Generated/HitFace.prefab");
            Assert.That(prefab.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
        }

        [TestCase("HitRingGood", "outer_good")]
        [TestCase("HitRingOk", "outer_ok")]
        [TestCase("HitRingGoodBig", "outer_good_big")]
        [TestCase("HitRingOkBig", "outer_ok_big")]
        public void HitRingFollowsNijiiroAnimations27And30(string name, string strip)
        {
            var clip = Clip(name);
            Assert.That(clip.length, Is.EqualTo(0.2).Within(1e-4));
            foreach (var (t, frame, alpha) in new[] { (0.0, 0, 1f), (0.0544, 0, 1f), (0.0546, 1, 1f), (0.0728, 2, 1f), (0.091, 3, 1f), (0.1667, 3, 1f), (0.18335, 3, 0.5f) })
            {
                var image = Sampled(clip, t, go => (go.GetComponent<UnityEngine.UI.Image>().sprite, go.GetComponent<UnityEngine.UI.Image>().color.a, go.GetComponent<UnityEngine.UI.Image>().enabled));
                Assert.That(image.Item1.name, Is.EqualTo($"HitRing_{strip}{frame}"), $"{t} s");
                Assert.That(image.Item2, Is.EqualTo(alpha).Within(1e-3), $"{t} s");
                Assert.That(image.Item3, Is.True, $"{t} s");
            }
            Assert.That(Sampled(clip, 0.2, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.False);
        }
    }
}
