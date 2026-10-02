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
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
