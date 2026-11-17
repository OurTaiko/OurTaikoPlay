using System.Collections;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OurTaiko.Tests
{
    // An animated (.otanim) puchichara beside the Don on a canvas: it is drawn instead of the
    // two-frame picture, moves on the Don's clock, is mirrored beside the 2P Don, and switches to
    // its GOGO clip while the Don dances. OTANIM_PUCHI may name a real .otanim to draw instead of
    // the test square (TestResults/PuchiAnim*.png).
    public sealed class PuchiAnimFlowTests
    {
        [UnityTest]
        public IEnumerator AnAnimatedPuchiMovesBesideTheDon()
        {
            string real = System.Environment.GetEnvironmentVariable("OTANIM_PUCHI");
            byte[] bytes = !string.IsNullOrEmpty(real) && System.IO.File.Exists(real)
                ? System.IO.File.ReadAllBytes(real)
                : OtanimFixture.Build(OtanimFixture.Make("idle", true, 10, 0, 50, 100), OtanimFixture.Make("gogo", true, 10, 100));
            DonLibrary.UseFile("puchi", 9201, OtanimFixture.WriteFile("puchi9201", bytes));

            var root = new GameObject("PuchiAnimTest", typeof(Canvas), typeof(CanvasScaler));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            var backdrop = new GameObject("Backdrop", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(root.transform, false);
            var br = (RectTransform)backdrop.transform;
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.sizeDelta = Vector2.zero;
            backdrop.GetComponent<Image>().color = new Color(0.93f, 0.91f, 0.86f);
            try
            {
                var look = DonLook.Default();
                look.puchi = 9201;
                double clock = 0;
                var p1 = DonView.Create(root.transform, "Don1P", 560, 800);
                p1.Clock = () => clock;
                p1.Show(look);
                var p2 = DonView.Create(root.transform, "Don2P", 1360, 800);
                p2.Clock = () => clock;
                p2.Show(WithPuchi(DonLook.Default(1), 9201), true);
                yield return null;
                yield return null;

                var puchi1 = p1.GetComponentInChildren<PuchiAnimGraphic>();
                var puchi2 = p2.GetComponentInChildren<PuchiAnimGraphic>();
                Assert.That(puchi1, Is.Not.Null, "an .otanim puchichara is drawn animated");
                Assert.That(puchi1.enabled && puchi2.enabled, Is.True);
                Assert.That(puchi2.Mirrored, Is.True, "mirrored beside the 2P Don");
                var picture = p1.transform.Find("Puchi");
                Assert.That(picture == null || !picture.GetComponent<RawImage>().enabled, Is.True, "not the two-frame picture");
                Assert.That(puchi1.ClipName, Is.EqualTo(puchi1.PickClip(DonAnim.Normal).Name));
                Assert.That(((RectTransform)puchi1.transform).anchoredPosition.x, Is.LessThan(0), "behind the 1P Don");
                Assert.That(((RectTransform)puchi2.transform).anchoredPosition.x, Is.GreaterThan(0), "behind the 2P Don");

                TestCapture.Capture("PuchiAnim1.png", canvas: canvas);
                var meshBefore = Corners(puchi1);
                clock += 250;
                yield return null;
                yield return null;
                Assert.That(puchi1.ClipSeconds, Is.EqualTo(0.25).Within(1e-6), "it runs on the Don's clock");
                Assert.That(Corners(puchi1), Is.Not.EqualTo(meshBefore), "it moved");
                TestCapture.Capture("PuchiAnim2.png", canvas: canvas);

                p1.Chara.SetAnim(DonAnim.Sabi);
                clock += 100;
                yield return null;
                yield return null;
                Assert.That(puchi1.ClipName, Is.EqualTo(puchi1.PickClip(DonAnim.Sabi).Name));
                if (puchi1.Anim.Find("gogo") != null) Assert.That(puchi1.ClipName, Is.EqualTo("gogo"));
                TestCapture.Capture("PuchiAnimGogo.png", canvas: canvas);

                p1.Alpha = 0.5f;
                Assert.That(puchi1.color.a, Is.EqualTo(0.5f), "it fades with the Don");
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        static DonLook WithPuchi(DonLook look, int puchi)
        {
            look.puchi = puchi;
            return look;
        }

        static string Corners(PuchiAnimGraphic graphic)
        {
            var vh = new UnityEngine.UI.VertexHelper();
            graphic.GetType().GetMethod("OnPopulateMesh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(VertexHelper) }, null).Invoke(graphic, new object[] { vh });
            var sb = new System.Text.StringBuilder();
            var v = new UIVertex();
            for (int i = 0; i < Mathf.Min(8, vh.currentVertCount); i++) { vh.PopulateUIVertex(ref v, i); sb.Append(v.position.ToString("F2")); }
            vh.Dispose();
            return sb.ToString();
        }
    }
}
