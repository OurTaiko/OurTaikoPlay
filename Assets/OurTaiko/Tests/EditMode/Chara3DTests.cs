using System;
using System.Linq;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The 3D Don ported from YataiDON: its glb parts and motions load from Resources/Chara, the
    // motion and face logic follows Chara3D::set_anim/update, and it draws something.
    public sealed class Chara3DTests
    {
        [Test]
        public void MotionsComeInAnimIndexOrder()
        {
            var motions = DonLibrary.Motions;
            Assert.That(motions.Count, Is.EqualTo(52));
            Assert.That(motions.Names[(int)DonAnim.BalloonFailure], Is.EqualTo("don_balloon_failure"));
            Assert.That(motions.Names[(int)DonAnim.Normal], Does.StartWith("don_normal"));
            Assert.That(motions.Names[(int)DonAnim.SelectPanelUp], Does.StartWith("don_select"));
            Assert.That(motions.Names[(int)DonAnim.MirrorOffset], Does.Contain("mirror"));
            Assert.That(motions.FrameCounts.All(f => f > 0));
        }

        // A copy of the built-in costume 0 as a downloaded file would be (Resources/Chara keeps
        // only the plain Don; the rest come from the public bucket).
        internal static string CopyOfPlainDon(string name)
        {
            var asset = Resources.Load<GlbAsset>(DonLibrary.Root + "cos/0");
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OurTaikoDonTests", name + ".glb");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, asset.bytes);
            return path;
        }

        [Test]
        public void ThePlainDonIsBuiltInAndTheRestAreFiles()
        {
            var costume = DonLibrary.Part("cos", 0);
            Assert.That(costume, Is.Not.Null);
            Assert.That(costume.Primitives, Is.Not.Empty);
            Assert.That(costume.JointNames.Length, Is.EqualTo(40));
            Assert.That(costume.JointNames.Count(n => DonLibrary.Motions.Joints.ContainsKey(n)), Is.GreaterThan(30));
            Assert.That(costume.Materials.Count(m => m.Recolor), Is.GreaterThan(0));
            Assert.That(costume.FaceMaterial, Is.GreaterThanOrEqualTo(0));
            Assert.That(DonLibrary.Part("cos", 30), Is.Null, "not built in");
            Assert.That(DonLibrary.Part("head", 0, true), Is.Null, "not built in");
            DonLibrary.UseFile("head", 9001, CopyOfPlainDon("head9001"));
            var head = DonLibrary.Part("head", 9001, true);
            Assert.That(head, Is.Not.Null, "read from its file");
            // A head's face plate is always brought to the standard size.
            var face = head.Primitives.First(p => p.Material == head.FaceMaterial);
            float w = face.Positions.Max(v => v.x) - face.Positions.Min(v => v.x);
            float h = face.Positions.Max(v => v.y) - face.Positions.Min(v => v.y);
            Assert.That(Math.Max(w, h), Is.EqualTo(DonPart.FacePlaneSize).Within(0.001f));
        }

        [Test]
        public void OneShotReturnsToTheLoopItInterrupted()
        {
            var chara = new Chara3D(DonLook.Default());
            try
            {
                chara.Bpm = 600;
                chara.SetAnim(DonAnim.Sabi);
                chara.SetAnim(DonAnim.Combo);
                Assert.That(chara.Anim, Is.EqualTo(DonAnim.Combo));
                for (double ms = 1; ms < 5000 && chara.Anim == DonAnim.Combo; ms += 1) chara.Update(ms);
                Assert.That(chara.Anim, Is.EqualTo(DonAnim.Sabi));
            }
            finally { chara.Dispose(); }
        }

        [Test]
        public void MirroredDonUsesTheMirrorMotions()
        {
            var chara = new Chara3D(DonLook.Default(1), mirror: true);
            try
            {
                chara.SetAnim(DonAnim.Normal);
                Assert.That((int)chara.Anim, Is.EqualTo((int)DonAnim.Normal + (int)DonAnim.MirrorOffset));
                chara.SetAnim(DonAnim.Miss);
                Assert.That(chara.Anim, Is.EqualTo(DonAnim.MissMirror));
                chara.SetMirror(false);
                Assert.That(chara.Anim, Is.EqualTo(DonAnim.Miss));
            }
            finally { chara.Dispose(); }
        }

        [Test]
        public void FukkatuHoldsTheReviveFace()
        {
            var chara = new Chara3D(DonLook.Default());
            try
            {
                chara.SetAnim(DonAnim.FukkatuLoop);
                Assert.That(chara.Face, Is.EqualTo(8));
            }
            finally { chara.Dispose(); }
        }

        [Test]
        public void IdlePartsAreFreedBeyondAFew()
        {
            var worn = new Chara3D(DonLook.Default());
            try
            {
                for (int i = 1; i <= 14; i++)
                {
                    DonLibrary.UseFile("cos", 9100 + i, CopyOfPlainDon("cos" + (9100 + i)));
                    var look = DonLook.Default(); look.cos = 9100 + i;
                    new Chara3D(look).Dispose();
                }
                Assert.That(DonLibrary.CachedParts, Is.LessThanOrEqualTo(DonLibrary.IdlePartsKept + 1));
                Assert.That(worn.IsEmpty, Is.False);
            }
            finally { worn.Dispose(); }
        }

        // The Editor keeps statics between Play sessions and destroys the textures a session made
        // when it stops: a Don made after that must not wear a cached part's destroyed images (it
        // came out white, its colour textures missing).
        [Test]
        public void ADonAfterItsCachedTexturesWereDestroyedStillHasItsColours()
        {
            var first = new Chara3D(DonLook.Default());
            first.Dispose();
            foreach (var image in DonLibrary.Part("cos", 0).Images)
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            var again = new Chara3D(DonLook.Default());
            try
            {
                var parts = (System.Collections.IList)typeof(Chara3D)
                    .GetField("parts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(again);
                var materials = (Material[])parts[0].GetType().GetField("Materials").GetValue(parts[0]);
                Assert.That(materials.All(m => m.mainTexture != null), Is.True, "every material has its texture");
            }
            finally { again.Dispose(); }
        }

        [Test]
        public void PuchiLoads()
        {
            Assert.That(DonLibrary.Puchi(0), Is.Null);
            Assert.That(DonLibrary.Puchi(9001), Is.Null, "not downloaded");
            var picture = new Texture2D(4, 8);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OurTaikoDonTests", "puchi9001.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, picture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(picture);
            DonLibrary.UseFile("puchi", 9001, path);
            Assert.That(DonLibrary.Puchi(9001), Is.Not.Null);
            Assert.That(DonLibrary.FaceCount, Is.EqualTo(12));
        }

        [Test]
        public void DrawsAnOutlinedDon()
        {
            var shot = new Editor.DonPreview.Shot("t", DonLook.Default(), DonAnim.Normal);
            var picture = Editor.DonPreview.Render(shot, 320, 400, 0.5f, 0.15f);
            try
            {
                var pixels = picture.GetPixels32();
                var back = pixels[0]; // the corner: the grey the Don was put on
                int grey = pixels.Count(p => p.r == back.r && p.g == back.g && p.b == back.b);
                int dark = pixels.Count(p => p.r < 30 && p.g < 30 && p.b < 30);
                int red = pixels.Count(p => p.r > 200 && p.g < 110 && p.b < 90);
                Assert.That(grey, Is.LessThan(pixels.Length), "nothing drawn");
                Assert.That(dark, Is.GreaterThan(200), "no outline");
                Assert.That(red, Is.GreaterThan(200), "no red face colour");
            }
            finally { UnityEngine.Object.DestroyImmediate(picture); }
        }
    }
}
