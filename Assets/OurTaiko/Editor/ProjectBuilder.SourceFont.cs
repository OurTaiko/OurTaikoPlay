using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OurTaiko.Editor
{
    public static partial class ProjectBuilder
    {
        const string SourceFontPath = Root + "Art/DDFont.ttf";

        // Points the dynamic TMP font asset at Art/DDFont.ttf in place, so every
        // scene, prefab and material keeps its reference (asset GUIDs are unchanged).
        // The atlases are emptied and refilled from the new face; glyphs added at
        // runtime are regenerated on demand. Repeating it leaves the assets unchanged.
        [MenuItem("OurTaiko/Apply Source Font")]
        public static void ApplySourceFont()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before replacing the source font.");

            var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
            if (source == null)
                throw new InvalidOperationException("The source font " + SourceFontPath + " is missing.");

            foreach (var path in new[] { UiFontPath })
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null) continue;
                if (font.sourceFontFile == source)
                {
                    Debug.Log("OurTaiko: " + font.name + " already uses " + SourceFontPath + ".");
                    continue;
                }
                RetargetFont(font, source);
            }
            AssetDatabase.SaveAssets();
        }

        static void RetargetFont(TMP_FontAsset font, Font source)
        {
            string guid = AssetDatabase.AssetPathToGUID(SourceFontPath);
            // Keep the existing glyph seed: everything currently in the table.
            string seed = new string(font.characterTable
                .Where(c => c.unicode <= char.MaxValue).Select(c => (char)c.unicode).ToArray());

            int pointSize = (int)font.faceInfo.pointSize;
            if (FontEngine.LoadFontFace(source, pointSize) != FontEngineError.Success)
                throw new InvalidOperationException("Could not load " + SourceFontPath + ".");
            var face = FontEngine.GetFaceInfo();

            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_SourceFontFile").objectReferenceValue = source;
            serialized.FindProperty("m_SourceFontFileGUID").stringValue = guid;
            serialized.FindProperty("m_SourceFontFilePath").stringValue = string.Empty;
            serialized.FindProperty("m_CreationSettings.sourceFontFileGUID").stringValue = guid;
            serialized.FindProperty("m_CreationSettings.sourceFontFileName").stringValue = source.name;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // The Editor caches the source font outside serialization.
            typeof(TMP_FontAsset).GetField("m_SourceFontFile_EditorRef", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(font, source);

            font.faceInfo = face;
            font.ClearFontAssetData(false);
            if (!font.TryAddCharacters(seed, out string missing))
                Debug.LogWarning("OurTaiko: " + SourceFontPath + " lacks characters used by " + font.name + ": " + missing);

            for (int i = 0; i < font.atlasTextures.Length; i++)
            {
                var atlas = font.atlasTextures[i];
                if (!AssetDatabase.Contains(atlas))
                {
                    atlas.name = font.name + " Atlas " + i;
                    AssetDatabase.AddObjectToAsset(atlas, font);
                }
                EditorUtility.SetDirty(atlas);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, font);
            Debug.Log("OurTaiko: " + font.name + " now renders from " + SourceFontPath + " (" + face.familyName + ").");
        }
    }
}
