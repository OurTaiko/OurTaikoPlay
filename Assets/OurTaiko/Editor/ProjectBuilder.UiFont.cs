using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string UiFontPath = Root + "Resources/Nijiiro UI SDF.asset";

        [MenuItem("OurTaiko/Create UI Font")]
        public static void CreateUiFont()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before creating the outlined UI font.");

            const string assetPath = UiFontPath;
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                Debug.Log("OurTaiko: Keeping the existing outlined UI font.");
                return;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "Art/DDFont.ttf");
            if (source == null)
                throw new InvalidOperationException("The Nijiiro source font Art/DDFont.ttf is missing.");

            // Wide SDF padding leaves room for the skin's thick outlines without
            // filling the transparent edge of each glyph's quad.
            var uiFont = TMP_FontAsset.CreateFontAsset(source, 64, 32,
                GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (uiFont == null)
                throw new InvalidOperationException("Could not create the outlined UI font from DDFont.ttf.");

            uiFont.name = "Nijiiro UI SDF";
            uiFont.material.name = uiFont.name + " Material";
            uiFont.material.EnableKeyword("OUTLINE_ON");
            uiFont.material.SetFloat(ShaderUtilities.ID_FaceDilate, 0f);
            uiFont.material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);

            // TMP exposes this setting only internally; use its serialized field
            // so player builds start with an empty, dynamically populated atlas.
            var serialized = new SerializedObject(uiFont);
            serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(uiFont, assetPath);
            AssetDatabase.AddObjectToAsset(uiFont.material, uiFont);
            foreach (var atlas in uiFont.atlasTextures)
                AssetDatabase.AddObjectToAsset(atlas, uiFont);

            // Keep the saved seed small; song titles and player names add glyphs
            // on demand, including additional atlas pages when needed.
            if (!uiFont.TryAddCharacters(UiFontSeed, out string missing))
                Debug.LogWarning("OurTaiko: UI font seed contains unavailable characters: " + missing);

            for (int i = 0; i < uiFont.atlasTextures.Length; i++)
            {
                var atlas = uiFont.atlasTextures[i];
                atlas.name = uiFont.name + " Atlas " + i;
                if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, uiFont);
                EditorUtility.SetDirty(atlas);
            }
            EditorUtility.SetDirty(uiFont.material);
            EditorUtility.SetDirty(uiFont);
            AssetDatabase.SaveAssetIfDirty(uiFont);
            Debug.Log("OurTaiko: Created outlined UI font with 64-point sampling and 32-pixel SDF padding.");
        }

        const string UiFontSeed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.,!?+-()★２人プレイ太鼓をたたいてスタート！";

        // Glyphs TMP adds while the Editor or tests run are written into the dynamic font asset.
        // Before committing, empty the atlas so the saved asset only depends on DDFont.ttf; every
        // glyph is added on demand at runtime. Extra atlas pages get new file IDs each time they
        // are created, so none are kept and the result is identical on every run.
        [MenuItem("OurTaiko/Regenerate UI Font Atlas")]
        public static void RegenerateUiFontAtlas()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before regenerating the UI font atlas.");
            var uiFont = UiFont();
            uiFont.ClearFontAssetData();
            EditorUtility.SetDirty(uiFont);
            AssetDatabase.SaveAssetIfDirty(uiFont);
            Debug.Log("OurTaiko: regenerated the UI font atlas with " + uiFont.characterTable.Count + " characters.");
        }

        static TMP_FontAsset UiFont()
        {
            var uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFontPath);
            if (uiFont == null)
            {
                CreateUiFont();
                uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFontPath);
            }
            return uiFont;
        }

        // The one material every text uses (SkinUi.OutlineMaterialName in Resources): opaque black
        // border of SkinUi.OutlineWidth, a fraction of the em, so it follows font size and scale.
        // Equal dilate keeps the border outside the face.
        static Material UiOutlineMaterial()
        {
            var uiFont = UiFont();
            string path = Root + "Resources/" + SkinUi.OutlineMaterialName + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(uiFont.material) { name = SkinUi.OutlineMaterialName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = uiFont.material.shader;
                material.CopyPropertiesFromMaterial(uiFont.material);
            }
            float width = SkinUi.OutlineWidth;
            material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            material.SetFloat(ShaderUtilities.ID_FaceDilate, width);
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, width);
            material.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            ShaderUtilities.UpdateShaderRatios(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static void SetOutlinedUiText(TMP_Text text, TMP_FontAsset uiFont, Material material, Color color)
        {
            text.font = uiFont;
            text.fontSharedMaterial = material;
            text.color = color;
            // Discard any old per-label material instances and atlas references.
            var serialized = new SerializedObject(text);
            serialized.FindProperty("m_fontMaterial").objectReferenceValue = null;
            serialized.FindProperty("m_fontSharedMaterials").ClearArray();
            serialized.FindProperty("m_fontMaterials").ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            text.UpdateMeshPadding();
            EditorUtility.SetDirty(text);
        }
    }
}
