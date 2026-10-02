using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class ControlGuideClipTests
    {
        const string ClipPath = "Assets/OurTaiko/Generated/ControlGuide.anim";

        [Test]
        public void ClipSwapsTheDecideLoopCellsAt30Fps()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.frameRate, Is.EqualTo(30));
            Assert.That(clip.isLooping, Is.True);
            var binding = AnimationUtility.GetObjectReferenceCurveBindings(clip).Single();
            Assert.That(binding.type, Is.EqualTo(typeof(UnityEngine.UI.Image)));
            Assert.That(binding.propertyName, Is.EqualTo("m_Sprite"));
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            // One key per cell; Unity gives the last sprite key a full frame.
            Assert.That(keys.Length, Is.EqualTo(115));
            Assert.That(clip.length, Is.EqualTo(115 / 30f).Within(1e-4));
            for (int i = 0; i < 115; i++)
            {
                var sprite = (Sprite)keys[i].value;
                int cell = 210 + i;
                Assert.That(keys[i].time, Is.EqualTo(i / 30f).Within(1e-4));
                Assert.That(sprite.name, Is.EqualTo($"ControlGuide{cell:000}"));
                Assert.That(sprite.texture.name, Is.EqualTo("background"));
                // indicator/background: 13 cells of 352x276 per row, counted from the top.
                Assert.That(sprite.rect, Is.EqualTo(new Rect(cell % 13 * 352, 6900 - (cell / 13 + 1) * 276, 352, 276)));
            }
        }

        [Test]
        public void ViewSamplesTheClipFrameForTheElapsedTime()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
            var parent = new GameObject("Parent", typeof(RectTransform));
            try
            {
                var view = new ControlGuideView(parent.transform, new ArcadeOverlayArt { guideClip = clip });
                Assert.That(view.FrameCount, Is.EqualTo(115));
                Assert.That(view.Image.sprite.name, Is.EqualTo("ControlGuide210"));
                Assert.That(view.Image.rectTransform.sizeDelta, Is.EqualTo(new Vector2(352, 276)));
                foreach (var (ms, frame) in new[] { (0.0, 0), (33.3, 0), (33.4, 1), (1000.0, 30), (3800.0, 114), (3833.4, 0), (4000.0, 5) })
                {
                    view.Show(ms, 1);
                    Assert.That(view.Frame, Is.EqualTo(frame), $"{ms} ms");
                    Assert.That(view.Image.sprite.name, Is.EqualTo($"ControlGuide{210 + frame:000}"), $"{ms} ms");
                }
                view.Show(0, 0.25f);
                Assert.That(view.Image.color.a, Is.EqualTo(0.25f).Within(1e-6));
            }
            finally { Object.DestroyImmediate(parent); }
        }
    }
}
