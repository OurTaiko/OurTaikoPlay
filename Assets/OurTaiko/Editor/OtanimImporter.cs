using System.IO;
using OurTaiko.Chara;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace OurTaiko.Editor
{
    // .otanim files (OurTaikoLogs' OurTaikoPlay/ArtSource/stage/build.sh bakes the stage's from AviUtl projects) import as an
    // OtanimAsset: the animation's bytes and its picture as a texture.
    [ScriptedImporter(2, "otanim")]
    public sealed class OtanimImporter : ScriptedImporter
    {
        [Tooltip("Block-compress the picture as the dancers' atlas was: BC7 on desktop, ASTC 6×6 on phones. Off keeps it as it is.")]
        public bool compress = true;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            byte[] bytes = File.ReadAllBytes(ctx.assetPath);
            var file = Otanim.Parse(bytes);
            bool phone = ctx.selectedBuildTarget == BuildTarget.Android || ctx.selectedBuildTarget == BuildTarget.iOS;
            Texture2D texture;
            Vector2 share;
            try { texture = Picture(file.Texture, compress ? phone ? TextureFormat.ASTC_6x6 : TextureFormat.BC7 : TextureFormat.RGBA32, out share); }
            catch (InvalidDataException error) { throw new InvalidDataException(ctx.assetPath + ": " + error.Message); }
            texture.Apply(false, true);

            var asset = ScriptableObject.CreateInstance<OtanimAsset>();
            asset.bytes = Otanim.WithoutTexture(bytes);
            asset.texture = texture;
            asset.pictureShare = share;
            ctx.AddObjectToAsset("otanim", asset);
            ctx.AddObjectToAsset("texture", texture);
            ctx.SetMainObject(asset);
        }

        // The sides a format's textures must be whole multiples of: BC7 is made of 4×4 blocks and
        // is refused for any other size (the texture is then silently left as it was); ASTC takes any.
        public static int BlockSide(TextureFormat format) => format == TextureFormat.BC7 ? 4 : 1;

        // texture itself when its sides are whole multiples of block; else a copy widened to them,
        // the picture in its top left corner and its last column and bottom row repeated outwards.
        public static Texture2D Widened(Texture2D texture, int block)
        {
            int width = (texture.width + block - 1) / block * block, height = (texture.height + block - 1) / block * block;
            if (width == texture.width && height == texture.height) return texture;
            var widened = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = texture.name };
            var from = texture.GetPixels32();
            var to = new Color32[width * height];
            // A texture's rows run upwards, so the rows added are the first ones.
            int below = height - texture.height;
            for (int y = 0; y < height; y++)
            {
                int row = Mathf.Max(0, y - below) * texture.width;
                for (int x = 0; x < width; x++) to[y * width + x] = from[row + Mathf.Min(x, texture.width - 1)];
            }
            widened.SetPixels32(to);
            widened.Apply(false, false);
            return widened;
        }

        // A file's picture as a texture in `format` (RGBA32 leaves it as it is). A format that needs
        // whole blocks gets a texture widened to them (so what is drawn at the picture's edge is
        // filtered as it was); share is how much of the texture the picture takes, from its top
        // left corner. Throws when the format did not take.
        public static Texture2D Picture(byte[] png, TextureFormat format, out Vector2 share)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Texture" };
            if (!texture.LoadImage(png)) throw new InvalidDataException("its picture is not a PNG");
            var widened = Widened(texture, BlockSide(format));
            share = new Vector2(texture.width / (float)widened.width, texture.height / (float)widened.height);
            if (widened != texture) DestroyImmediate(texture);
            texture = widened;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            if (format == TextureFormat.RGBA32) return texture;
            EditorUtility.CompressTexture(texture, format, TextureCompressionQuality.Normal);
            if (texture.format != format)
                throw new InvalidDataException($"its {texture.width}×{texture.height} picture could not be compressed as {format} and is still {texture.format}");
            return texture;
        }
    }
}
