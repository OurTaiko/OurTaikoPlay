using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        [MenuItem("OurTaiko/Create Outlined UI Font")]
        public static void CreateOutlinedUiFont()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before creating the outlined UI font.");

            const string assetPath = Root + "Resources/Nijiiro UI SDF.asset";
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null)
            {
                Debug.Log("OurTaiko: Keeping the existing outlined UI font.");
                return;
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(Root + "Art/Taiko.ttf");
            if (source == null)
                throw new InvalidOperationException("The Nijiiro source font Art/Taiko.ttf is missing.");

            // Wide SDF padding leaves room for the skin's thick outlines without
            // filling the transparent edge of each glyph's quad.
            var uiFont = TMP_FontAsset.CreateFontAsset(source, 64, 32,
                GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (uiFont == null)
                throw new InvalidOperationException("Could not create the outlined UI font from Taiko.ttf.");

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
            const string seed = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 /:.,!?+-()★２人プレイ太鼓をたたいてスタート！";
            if (!uiFont.TryAddCharacters(seed, out string missing))
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
    }
}
