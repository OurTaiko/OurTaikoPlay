using System.IO;
using UnityEngine;

namespace OurTaiko.Chara
{
    // An .otanim file in the project (the editor's OtanimImporter makes it): the file without its
    // picture, and the picture as a texture, so showing it decodes no PNG. Animations fetched at
    // run time (puchichara) are read with Otanim.Parse instead.
    public sealed class OtanimAsset : ScriptableObject
    {
        [HideInInspector] public byte[] bytes;
        public Texture2D texture;
        // How much of the texture the file's picture takes, from its top left corner: less than 1
        // where the importer widened the texture for a format that needs whole blocks.
        [HideInInspector] public Vector2 pictureShare = Vector2.one;

        Otanim file;

        public Otanim File => file ??= Read();

        // A file from outside the game (a player's, or one fetched as it is), its picture decoded
        // into a texture of its own; whoever reads it destroys both. Throws when it is not an
        // .otanim with a PNG picture (FormatException, InvalidDataException and the like).
        public static OtanimAsset FromBytes(byte[] bytes, string name)
        {
            var read = Otanim.Parse(bytes);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name, hideFlags = HideFlags.DontSave };
            if (read.Texture == null || !texture.LoadImage(read.Texture, true))
            {
                if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
                throw new InvalidDataException("its picture is not a PNG");
            }
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            var asset = CreateInstance<OtanimAsset>();
            asset.name = name;
            asset.hideFlags = HideFlags.DontSave;
            asset.bytes = Otanim.WithoutTexture(bytes);
            asset.texture = texture;
            asset.file = read;
            return asset;
        }

        Otanim Read()
        {
            var read = Otanim.Parse(bytes);
            if (pictureShare != Vector2.one)
                foreach (var part in read.Parts)
                    for (int i = 0; i < part.UVs.Length; i++) part.UVs[i] = Vector2.Scale(part.UVs[i], pictureShare);
            return read;
        }
    }
}
