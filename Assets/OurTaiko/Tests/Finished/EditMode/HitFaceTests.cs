using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class HitFaceTests
    {
        const string PrefabPath = "Assets/OurTaiko/Generated/HitFace.prefab";
        const string RingPrefabPath = "Assets/OurTaiko/Generated/HitRing.prefab";

        [Test]
        public void OpacityFollowsNijiiroAnimation28()
        {
            Assert.That(HitFaceTiming.Opacity(0), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(HitFaceTiming.Opacity(33.35), Is.EqualTo(0.75).Within(1e-9));
            Assert.That(HitFaceTiming.Opacity(66.7), Is.EqualTo(1).Within(1e-9));
            Assert.That(HitFaceTiming.Opacity(200), Is.EqualTo(1).Within(1e-9));
            Assert.That(HitFaceTiming.Opacity(283.3 + 33.35), Is.EqualTo(0.75).Within(1e-9));
            Assert.That(HitFaceTiming.IsVisible(349.9), Is.True);
            Assert.That(HitFaceTiming.IsVisible(350), Is.False);
            Assert.That(HitFaceTiming.IsVisible(-1), Is.False);
        }

        [Test]
        public void RingFollowsNijiiroAnimations27And30()
        {
            Assert.That(new[] { 0.0, 54.5, 54.6, 72.7, 72.8, 90.9, 91.0, 150, 190 }.Select(HitRingTiming.Frame),
                Is.EqualTo(new[] { 0, 0, 1, 1, 2, 2, 3, 3, 3 }));
            Assert.That(HitRingTiming.Opacity(166.7), Is.EqualTo(1).Within(1e-9));
            Assert.That(HitRingTiming.Opacity(166.7 + 16.65), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(HitRingTiming.IsVisible(199.9), Is.True);
            Assert.That(HitRingTiming.IsVisible(200), Is.False);
        }

        [Test]
        public void RingPrefabHoldsFourFramesPerVariantWithAdditiveBlend()
        {
            var view = AssetDatabase.LoadAssetAtPath<HitRingView>(RingPrefabPath);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.image, Is.SameAs(view.GetComponent<UnityEngine.UI.Image>()));
            Assert.That(view.image.material.shader.name, Is.EqualTo("Mobile/Particles/Additive"));
            Assert.That(((RectTransform)view.transform).sizeDelta, Is.EqualTo(new Vector2(336, 336)));
            var variants = new[] { ("outer_good", view.good), ("outer_ok", view.ok), ("outer_good_big", view.goodBig), ("outer_ok_big", view.okBig) };
            foreach (var (name, frames) in variants)
            {
                Assert.That(frames.Length, Is.EqualTo(4));
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(frames[i].texture.name, Is.EqualTo(name));
                    Assert.That(frames[i].rect, Is.EqualTo(new Rect(i * 336, 0, 336, 336)));
                }
            }
        }

        [Test]
        public void PrefabHoldsTheFourNijiiroFaces()
        {
            var view = AssetDatabase.LoadAssetAtPath<HitFaceView>(PrefabPath);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.image, Is.SameAs(view.GetComponent<UnityEngine.UI.Image>()));
            Assert.That(new[] { view.good, view.ok, view.goodBig, view.okBig }.Select(s => s.name),
                Is.EqualTo(new[] { "hit_effect_good", "hit_effect_ok", "hit_effect_good_big", "hit_effect_ok_big" }));
        }

        [Test]
        public void SinglePlaySceneUsesAPrefabInstanceAtTheJudgePoint()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SinglePlayScene.unity");
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayScene>(true)).Single();
                var face = play.hitFace;
                Assert.That(face, Is.Not.Null);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(face.gameObject),
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)));
                var rect = (RectTransform)face.transform;
                Assert.That(rect.parent, Is.SameAs(play.judgment.transform.parent));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(510, -2)));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(216, 216)));
                Assert.That(rect.parent.Find("HitFlash"), Is.Null);
                // Player::draw: face under the notes; ring over the notes and lane cover, under the drum.
                Assert.That(rect.GetSiblingIndex() + 1, Is.EqualTo(play.noteLayer.parent.GetSiblingIndex()));
                var ring = (RectTransform)play.hitRing.transform;
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(ring.gameObject),
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath)));
                Assert.That(ring.parent, Is.SameAs(rect.parent));
                Assert.That(ring.anchoredPosition, Is.EqualTo(new Vector2(450, 58)));
                Assert.That(ring.GetSiblingIndex(), Is.GreaterThan(rect.parent.Find("MojiClip").GetSiblingIndex()));
                Assert.That(ring.GetSiblingIndex(), Is.GreaterThan(rect.parent.Find("PlayerCover").GetSiblingIndex()));
                Assert.That(ring.GetSiblingIndex() + 1, Is.EqualTo(rect.parent.Find("Drum").GetSiblingIndex()));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
