using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko.Tests
{
    // Animated puchichara (.otanim): the file reads back as written, frames ease into each other,
    // loops wrap and one-shots hold, the Don's motion picks the clip, and a downloaded .otanim
    // puchichara is told apart from a two-frame picture by its bytes.
    public sealed class OtanimTests
    {
        static Otanim Fixture() => Otanim.Parse(OtanimFixture.Build(
            OtanimFixture.Make("idle", true, 10, 0, 50, 100),
            OtanimFixture.Make("gogo", false, 4, 0, 100)));

        [Test]
        public void AFileReadsBackAsWritten()
        {
            var file = Fixture();
            Assert.That(file.Name, Is.EqualTo("fixture"));
            Assert.That(file.Premultiplied, Is.False);
            Assert.That(file.View, Is.EqualTo(Rect.MinMaxRect(-10, -10, 210, 110)));
            Assert.That(file.Parts.Length, Is.EqualTo(1));
            Assert.That(file.Parts[0].Indices, Is.EqualTo(new[] { 0, 1, 2, 2, 3, 0 }));
            Assert.That(file.Parts[0].UVs[1], Is.EqualTo(new Vector2(0, 0)));
            Assert.That(file.Clips.Length, Is.EqualTo(2));
            var idle = file.Find("idle");
            Assert.That(idle.Loop, Is.True);
            Assert.That(idle.Frames.Length, Is.EqualTo(3));
            // Frames after the first are stored as changes; they read back whole.
            Assert.That(idle.Positions[idle.Frames[2].Draws[0].Offset], Is.EqualTo(100f));
            Assert.That(idle.Positions[idle.Frames[2].Draws[0].Offset + 5], Is.EqualTo(100f));
            Assert.That(idle.Duration, Is.EqualTo(0.3f).Within(1e-6));
            Assert.That(file.Find("gogo").Duration, Is.EqualTo(0.25f).Within(1e-6));
        }

        [Test]
        public void SomethingElseIsNotAnOtanim()
        {
            Assert.That(Otanim.IsOtanim(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }), Is.False);
            Assert.Throws<InvalidDataException>(() => Otanim.Parse(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
            var bytes = OtanimFixture.Build(OtanimFixture.Make("idle", true, 10, 0));
            Assert.Throws<InvalidDataException>(() => Otanim.Parse(System.MemoryExtensions.AsSpan(bytes, 0, bytes.Length - 40).ToArray()), "a file cut short");
            bytes[4] = 9;
            Assert.Throws<InvalidDataException>(() => Otanim.Parse(bytes), "a later version");
        }

        [Test]
        public void LoopsWrapAndOneShotsHold()
        {
            var file = Fixture();
            Otanim.FrameAt(file.Find("idle"), 0.25, out int f, out int next, out float t);
            Assert.That((f, next), Is.EqualTo((2, 0)));
            Assert.That(t, Is.EqualTo(0.5f).Within(1e-4));
            Otanim.FrameAt(file.Find("idle"), 0.35, out f, out next, out _);
            Assert.That((f, next), Is.EqualTo((0, 1)), "past the end it starts over");
            Otanim.FrameAt(file.Find("gogo"), 5, out f, out next, out t);
            Assert.That((f, next, t), Is.EqualTo((1, -1, 0f)), "a one-shot holds its last frame");
        }

        [Test]
        public void AnIntroPlaysOnceThenTheRestLoops()
        {
            var clip = OtanimFixture.Make("gogo", true, 10, 0, 10, 20, 30, 40);
            clip.LoopStart = 2;
            var file = Otanim.Parse(OtanimFixture.Build(clip));
            var c = file.Find("gogo");
            Assert.That(c.LoopStart, Is.EqualTo(2));
            Otanim.FrameAt(c, 0.15, out int f, out int next, out _);
            Assert.That((f, next), Is.EqualTo((1, 2)), "the intro the first time");
            Otanim.FrameAt(c, 0.45, out f, out next, out _);
            Assert.That((f, next), Is.EqualTo((4, 2)), "the last frame eases back to the loop's start, not the intro's");
            Otanim.FrameAt(c, 0.55, out f, out _, out _);
            Assert.That(f, Is.EqualTo(2), "past the end it goes on from the loop's start");
            Otanim.FrameAt(c, 0.85, out f, out _, out _);
            Assert.That(f, Is.EqualTo(2), "and never back into the intro");

            var bad = OtanimFixture.Make("x", false, 10, 0, 10);
            bad.LoopStart = 1;
            Assert.Throws<InvalidDataException>(() => Otanim.Parse(OtanimFixture.Build(bad)), "a one-shot has no loop start");
        }

        static List<UIVertex> Sample(Otanim file, string clip, double seconds, Rect box, bool mirror = false)
        {
            var vertices = new List<UIVertex>();
            var indices = new List<int>();
            file.Sample(file.Find(clip), seconds, box, mirror, new Color32(255, 255, 255, 255), vertices, indices);
            Assert.That(indices, Is.EqualTo(new[] { 0, 1, 2, 2, 3, 0 }));
            return vertices;
        }

        [Test]
        public void FramesEaseAndFitTheBox()
        {
            var file = Fixture();
            // The view (220 × 120 units) fits 440 × 240 at twice the size, centred.
            var box = new Rect(-220, -120, 440, 240);
            var v = Sample(file, "idle", 0, box);
            Assert.That(v[0].position.x, Is.EqualTo(-200f).Within(1e-3));
            Assert.That(v[0].position.y, Is.EqualTo(-100f).Within(1e-3));
            Assert.That(v[2].position.x, Is.EqualTo(0f).Within(1e-3));
            Assert.That(v[2].position.y, Is.EqualTo(100f).Within(1e-3));
            // A quarter of the way to the next frame (50 units on): 12.5 units, 25 in the box.
            v = Sample(file, "idle", 0.025, box);
            Assert.That(v[0].position.x, Is.EqualTo(-175f).Within(1e-2));
            // Mirrored about the box's middle.
            v = Sample(file, "idle", 0, box, true);
            Assert.That(v[0].position.x, Is.EqualTo(200f).Within(1e-3));
            // The texture's top (v 0 in the file) is Unity's v 1.
            Assert.That(v[1].uv0.y, Is.EqualTo(1f).Within(1e-4));
        }

        [Test]
        public void TheDonsMotionPicksTheClip()
        {
            var go = new GameObject("puchi", typeof(RectTransform), typeof(CanvasRenderer), typeof(PuchiAnimGraphic));
            try
            {
                var graphic = go.GetComponent<PuchiAnimGraphic>();
                var file = Fixture();
                graphic.Show(file, null);
                Assert.That(graphic.PickClip(DonAnim.Normal).Name, Is.EqualTo("idle"));
                Assert.That(graphic.PickClip(DonAnim.Sabi).Name, Is.EqualTo("gogo"));
                Assert.That(graphic.PickClip(DonAnim.SabiStart + (int)DonAnim.MirrorOffset).Name, Is.EqualTo("gogo"), "a mirrored motion too");
                Assert.That(graphic.PickClip(DonAnim.Miss6Mirror).Name, Is.EqualTo("idle"), "no miss clip: idle");

                graphic.Advance(DonAnim.Normal, 1000);
                graphic.Advance(DonAnim.Normal, 1200);
                Assert.That(graphic.ClipName, Is.EqualTo("idle"));
                Assert.That(graphic.ClipSeconds, Is.EqualTo(0.2).Within(1e-9));
                graphic.Advance(DonAnim.Sabi, 1500);
                Assert.That(graphic.ClipName, Is.EqualTo("gogo"));
                Assert.That(graphic.ClipSeconds, Is.EqualTo(0), "a new clip starts from its beginning");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ADownloadedOtanimPuchiIsAnimated()
        {
            string path = OtanimFixture.WriteFile("puchi9101", OtanimFixture.Build(OtanimFixture.Make("idle", true, 10, 0, 50)));
            DonLibrary.UseFile("puchi", 9101, path);
            var texture = DonLibrary.Puchi(9101);
            Assert.That(texture, Is.Not.Null);
            Assert.That((texture.width, texture.height), Is.EqualTo((4, 4)), "the animation's own texture");
            Assert.That(DonLibrary.PuchiAnim(9101), Is.Not.Null);

            var picture = new Texture2D(4, 8);
            string png = OtanimFixture.WriteFile("puchi9102", picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
            DonLibrary.UseFile("puchi", 9102, png);
            Assert.That(DonLibrary.Puchi(9102), Is.Not.Null);
            Assert.That(DonLibrary.PuchiAnim(9102), Is.Null, "a two-frame picture is not animated");

            var look = DonLook.Default();
            look.puchi = 9101;
            var chara = new Chara3D(look);
            try
            {
                Assert.That(chara.PuchiAnim, Is.Not.Null);
                chara.SetPuchi(9102);
                Assert.That(chara.PuchiAnim, Is.Null);
                Assert.That(chara.PuchiTexture, Is.Not.Null);
            }
            finally { chara.Dispose(); }
        }
    }
}
