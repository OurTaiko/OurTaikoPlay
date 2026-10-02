using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string OutlinedUiFontPath = Root + "Resources/Nijiiro UI SDF.asset";

        [MenuItem("OurTaiko/Create Outlined UI Font")]
        public static void CreateOutlinedUiFont()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before creating the outlined UI font.");

            const string assetPath = OutlinedUiFontPath;
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

        [MenuItem("OurTaiko/Apply Outlined UI Font")]
        public static void ApplyOutlinedUiFont()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before applying the outlined UI font.");

            var uiFont = OutlinedUiFont();
            var nameplate = PrefabUtility.LoadPrefabContents(NameplatePrefabPath);
            try
            {
                var view = nameplate.GetComponent<NameplateView>();
                SetOutlinedUiText(view.title, uiFont,
                    OutlinedUiMaterial("Nameplate Title", uiFont, view.title.fontSize, 0), Color.black);
                SetOutlinedUiText(view.playerName, uiFont,
                    OutlinedUiMaterial("Nameplate Name", uiFont, view.playerName.fontSize, 3), Color.white);
                PrefabUtility.SaveAsPrefabAsset(nameplate, NameplatePrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(nameplate); }

            var switcher = PrefabUtility.LoadPrefabContents(SwitcherPrefab);
            try
            {
                var curtain = switcher.GetComponentInChildren<SongTransition>(true);
                SetOutlinedUiText(curtain.title, uiFont,
                    OutlinedUiMaterial("Curtain Title", uiFont, curtain.title.fontSize, 5), Color.white);
                SetOutlinedUiText(curtain.subtitle, uiFont,
                    OutlinedUiMaterial("Curtain Subtitle", uiFont, curtain.subtitle.fontSize, 5), Color.white);
                PrefabUtility.SaveAsPrefabAsset(switcher, SwitcherPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(switcher); }
            Debug.Log("OurTaiko: applied the outlined UI font to the nameplate and loading curtain prefabs.");
        }

        static TMP_FontAsset OutlinedUiFont()
        {
            var uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutlinedUiFontPath);
            if (uiFont == null)
            {
                CreateOutlinedUiFont();
                uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OutlinedUiFontPath);
            }
            return uiFont;
        }

        static Material OutlinedUiMaterial(string purpose, TMP_FontAsset uiFont, float size, float pixels)
        {
            string name = "Nijiiro UI " + purpose;
            string path = Root + "Generated/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(uiFont.material) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = uiFont.material.shader;
                material.CopyPropertiesFromMaterial(uiFont.material);
            }
            float width = pixels * uiFont.faceInfo.pointSize / (2f * uiFont.atlasPadding * size);
            material.EnableKeyword("OUTLINE_ON");
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
