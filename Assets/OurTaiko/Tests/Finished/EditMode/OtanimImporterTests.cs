using NUnit.Framework;
using OurTaiko.Chara;
using OurTaiko.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // How an .otanim's picture becomes a texture: BC7 takes only sides that are whole multiples of
    // four, so a picture of any other size is widened to them and drawn from the same pixels.
    public sealed class OtanimImporterTests
    {
        // Every pixel its own colour, by where it is from the picture's top left corner.
        static Color32 At(int x, int fromTop) => new Color32((byte)(x * 20 + 10), (byte)(fromTop * 30 + 10), 128, 255);

        static byte[] Png(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) texture.SetPixel(x, y, At(x, height - 1 - y));
            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            return png;
        }

        [Test]
        public void APictureIsWidenedToWholeBlocksFromItsTopLeftCorner()
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(Png(10, 7));
            var widened = OtanimImporter.Widened(texture, 4);
            try
            {
                Assert.That((widened.width, widened.height), Is.EqualTo((12, 8)));
                for (int fromTop = 0; fromTop < 8; fromTop++)
                    for (int x = 0; x < 12; x++)
                        Assert.That(widened.GetPixel(x, 7 - fromTop), Is.EqualTo((Color)At(Mathf.Min(x, 9), Mathf.Min(fromTop, 6))),
                            $"({x}, {fromTop} from the top): the picture where it was, its last column and row repeated past it.");
                Assert.That(OtanimImporter.Widened(widened, 4), Is.SameAs(widened), "Whole blocks already.");
                Assert.That(OtanimImporter.Widened(texture, 1), Is.SameAs(texture));
            }
            finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(widened); }
        }

        [Test]
        public void EachFormatGetsATextureItTakes()
        {
            byte[] png = Png(10, 7);
            (TextureFormat format, int width, int height, Vector2 share) Made(TextureFormat asked)
            {
                var texture = OtanimImporter.Picture(png, asked, out var share);
                try { return (texture.format, texture.width, texture.height, share); }
                finally { Object.DestroyImmediate(texture); }
            }
            // Uncompressed, as the PNG was read (Unity reads one as ARGB32).
            Assert.That(Made(TextureFormat.RGBA32), Is.EqualTo((TextureFormat.ARGB32, 10, 7, Vector2.one)));
            Assert.That(Made(TextureFormat.ASTC_6x6), Is.EqualTo((TextureFormat.ASTC_6x6, 10, 7, Vector2.one)), "ASTC takes any size.");
            Assert.That(Made(TextureFormat.BC7), Is.EqualTo((TextureFormat.BC7, 12, 8, new Vector2(10 / 12f, 7 / 8f))),
                "BC7 is refused for a size that is not whole blocks, and leaves the texture uncompressed without saying so.");
        }

        // The file's parts say where they are in the picture; in a widened texture that is a smaller share of it.
        [Test]
        public void AWidenedTexturesPartsAreStillTheSamePixels()
        {
            var imported = UnityEditor.AssetDatabase.LoadAssetAtPath<OtanimAsset>("Assets/OurTaiko/Tests/Finished/EditMode/Stage/screen.otanim");
            var plain = Otanim.Parse(imported.bytes);
            var asset = ScriptableObject.CreateInstance<OtanimAsset>();
            try
            {
                asset.bytes = imported.bytes;
                asset.pictureShare = new Vector2(10 / 12f, 7 / 8f);
                Assert.That(asset.File.Parts.Length, Is.EqualTo(plain.Parts.Length).And.GreaterThan(0));
                for (int p = 0; p < plain.Parts.Length; p++)
                    for (int i = 0; i < plain.Parts[p].UVs.Length; i++)
                    {
                        Vector2 was = plain.Parts[p].UVs[i], now = asset.File.Parts[p].UVs[i];
                        Assert.That((new Vector2(now.x * 12, now.y * 8) - new Vector2(was.x * 10, was.y * 7)).magnitude, Is.LessThan(1e-4f),
                            "In pixels from the top left corner, each part is where it was.");
                    }
            }
            finally { Object.DestroyImmediate(asset); }
        }
    }
}
