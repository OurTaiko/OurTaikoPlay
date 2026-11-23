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
