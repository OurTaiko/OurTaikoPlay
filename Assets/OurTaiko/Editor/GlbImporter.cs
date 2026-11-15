using System.IO;
using OurTaiko.Chara;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace OurTaiko.Editor
{
    // .glb files import as their own bytes (GlbAsset), so the Don models under Resources/Chara stay
    // glb and Chara3D reads them at run time, as YataiDON does.
    [ScriptedImporter(1, "glb")]
    public sealed class GlbImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var asset = ScriptableObject.CreateInstance<GlbAsset>();
            asset.bytes = File.ReadAllBytes(ctx.assetPath);
            ctx.AddObjectToAsset("glb", asset);
            ctx.SetMainObject(asset);
        }
    }

    // The face sheet and puchichara pictures under Resources/Chara are sampled by cell, so they keep
    // their size (the face sheet is 128×1536), no mipmaps, clamped. The face sheet stays
    // uncompressed (a cell is 128 px); the 213 puchichara are compressed.
    sealed class CharaTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Chara/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.textureCompression = path.Contains("/Resources/Chara/face/")
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.CompressedHQ;
        }
    }
}
