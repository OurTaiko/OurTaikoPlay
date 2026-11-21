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

        // The face plate as drawn (posed): the largest side of its box.
        static float FacePlateSize(Chara3D chara)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var parts = (System.Collections.IList)typeof(Chara3D).GetField("parts", flags).GetValue(chara);
            var part = parts[0];
            var data = (DonPart)part.GetType().GetField("Data").GetValue(part);
            var meshes = (Mesh[])part.GetType().GetField("Meshes").GetValue(part);
            int p = data.Primitives.FindIndex(prim => prim.Material == data.FaceMaterial);
            var bounds = meshes[p].vertices;
            Vector3 min = bounds[0], max = bounds[0];
            foreach (var v in bounds) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            return Math.Max(max.x - min.x, Math.Max(max.y - min.y, max.z - min.z));
        }

        // YataiDON set_paint: makeup swaps the face sheet and draws it on a plate a third larger;
        // 0 brings back the plain face and plate; makeup not downloaded changes nothing.
        [Test]
        public void MakeupSwapsTheFaceSheetOnALargerPlate()
        {
            var sheet = new Texture2D(16, 16 * 12);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OurTaikoDonTests", "paint9001.png");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
            Assert.That(DonLibrary.Paint(0), Is.Null);
            Assert.That(DonLibrary.Paint(9001), Is.Null, "not downloaded");
            DonLibrary.UseFile("paint", 9001, path);

            var chara = new Chara3D(DonLook.Default());
            try
            {
                chara.SetAnim(DonAnim.Sabi);
                chara.SetAnimProgress(0.3f);
                float plain = FacePlateSize(chara);
                int face = chara.Face;
                chara.SetPaint(9002);
                Assert.That(chara.PaintIndex, Is.EqualTo(0), "not downloaded: unchanged");
                chara.SetPaint(9001);
                Assert.That(chara.PaintIndex, Is.EqualTo(9001));
                Assert.That(chara.FaceSheet, Is.SameAs(DonLibrary.Paint(9001)));
                Assert.That(chara.Face, Is.EqualTo(face), "the face of the sheet stays");
                Assert.That(FacePlateSize(chara), Is.EqualTo(plain * Chara3D.PaintPlaneScale).Within(plain * 0.01f));
                var other = new Chara3D(DonLook.Default());
                try
                {
                    other.SetAnim(DonAnim.Sabi);
                    other.SetAnimProgress(0.3f);
                    Assert.That(FacePlateSize(other), Is.EqualTo(plain).Within(plain * 0.01f), "the shared part is untouched");
                }
                finally { other.Dispose(); }
                chara.SetPaint(0);
                Assert.That(chara.FaceSheet, Is.SameAs(DonLibrary.FaceSheet));
                Assert.That(FacePlateSize(chara), Is.EqualTo(plain).Within(plain * 0.01f));
            }
            finally { chara.Dispose(); }

            var look = DonLook.Default(); look.paint = 9001;
            var dressed = new Chara3D(look);
            try { Assert.That(dressed.PaintIndex, Is.EqualTo(9001), "a look's makeup is worn from the start"); }
            finally { dressed.Dispose(); }
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

        // The canvas draws only the part of the Don's area the Don is in (DonView.ShownPart), since
        // the rim pass costs twenty texture reads a pixel: in every motion that part must hold all
        // of the Don and its rim, and be a small share of the 640 × 640 a lane gives it.
        [Test]
        public void TheShownPartOfItsAreaHoldsTheWholeDonInEveryMotion()
        {
            const int Width = 640, Height = 640;
            var options = new Editor.DonPreview.Options { Screen = 0, Supersample = 1, Msaa = 1, Footprint = true };
            foreach (bool mirror in new[] { false, true })
            {
                var chara = new Chara3D(DonLook.Default(), mirror, useSkinPose: true);
                try
                {
                    chara.SetFrozen(true);
                    float largest = 0;
                    for (int anim = 0; anim < (int)DonAnim.MirrorOffset; anim++)
                    foreach (float progress in new[] { 0f, 0.3f, 0.6f, 0.9f })
                    {
                        chara.SetAnim((DonAnim)anim);
                        chara.SetAnimProgress(progress);
                        var picture = Editor.DonPreview.Render(chara, Width, Height, 0.5f, 0.2f, 1, options);
                        try
                        {
                            var shown = DonView.ShownPart(chara.Covered, Width, Height, 1, 1);
                            largest = Mathf.Max(largest, shown.width * shown.height);
                            var pixels = picture.GetPixels32();
                            var back = pixels[0];
                            int drawn = 0, outside = 0;
                            for (int i = 0; i < pixels.Length; i++)
                            {
                                var p = pixels[i];
                                if (p.r == back.r && p.g == back.g && p.b == back.b) continue;
                                drawn++;
                                float x = (i % picture.width + 0.5f) / picture.width, y = (i / picture.width + 0.5f) / picture.height;
                                if (!shown.Contains(new Vector2(x, y))) outside++;
                            }
                            Assert.That(drawn, Is.GreaterThan(1000), $"{(DonAnim)anim} at {progress}: nothing drawn");
                            Assert.That(outside, Is.Zero, $"{(DonAnim)anim} at {progress} (mirror {mirror}): part of the Don is outside what the canvas draws ({shown})");
                        }
                        finally { UnityEngine.Object.DestroyImmediate(picture); }
                    }
                    Assert.That(largest, Is.LessThan(0.35f), "The canvas draws most of the area whatever the Don covers.");
                }
                finally { chara.Dispose(); }
            }
        }
    }
}
