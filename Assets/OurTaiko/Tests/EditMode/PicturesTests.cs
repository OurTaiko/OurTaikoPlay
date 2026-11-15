using System.IO;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The servers' pictures: WebP decoded by unity.webp (scaled down on request), anything else
    // by Unity, and nothing for what is not a picture or is too big.
    public sealed class PicturesTests
    {
        static readonly byte[] Cover = File.ReadAllBytes("Assets/OurTaiko/Tests/Fixtures/playlist-cover.webp");

        [Test]
        public void AWebPIsDecodedAndScaledDownWithItsProportions()
        {
            Assert.That(Pictures.IsWebP(Cover), Is.True);
            var whole = Pictures.Decode(Cover, 2048);
            Assert.That((whole.width, whole.height), Is.EqualTo((1600, 1000)));
            // The checkerboard's first square: (230, 51, 77), lossy.
            Color32 corner = whole.GetPixel(10, 10);
            Assert.That(corner.r, Is.InRange(220, 240));
            Assert.That(corner.g, Is.InRange(40, 62));
            Assert.That(corner.b, Is.InRange(66, 88));
            Object.DestroyImmediate(whole);

            var small = Pictures.Decode(Cover, 2048, 512, readable: false);
            Assert.That((small.width, small.height), Is.EqualTo((512, 320)));
            Assert.That(small.isReadable, Is.False);
            Object.DestroyImmediate(small);
            // Smaller than `longest` stays as it is.
            var kept = Pictures.Decode(Cover, 2048, 4096);
            Assert.That((kept.width, kept.height), Is.EqualTo((1600, 1000)));
            Object.DestroyImmediate(kept);
        }

        [Test]
        public void OtherFormatsTooBigAndNotPicturesAreHandled()
        {
            var source = new Texture2D(30, 20, TextureFormat.RGBA32, false);
            byte[] png = source.EncodeToPNG();
            Object.DestroyImmediate(source);
            Assert.That(Pictures.IsWebP(png), Is.False);
            var decoded = Pictures.Decode(png, 2048);
            Assert.That((decoded.width, decoded.height), Is.EqualTo((30, 20)));
            Object.DestroyImmediate(decoded);

            Assert.That(Pictures.Decode(Cover, 1024), Is.Null, "Wider than the limit.");
            Assert.That(Pictures.Decode(null, 2048), Is.Null);
            Assert.That(Pictures.Decode(new byte[64], 2048), Is.Null);
        }
    }
}
