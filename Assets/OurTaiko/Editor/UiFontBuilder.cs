using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OurTaiko.Editor
{
    // Builds the UI faces and the UiFontSet that lists them (Runtime/Text/UiFontSet.cs). Each face is a
    // dynamic SDF font asset (64-point sampling, 32 padding so the outline never meets the cell edge,
    // 1024 multi-atlas) with a black-outline material for light text. Faces that exist are kept, so
    // their GUIDs and the references to them stay; rebuilding only refreshes the outline materials,
    // the fallbacks and the set. Each face falls back on the others, in set order, for characters it
    // lacks; UiFontSet.Pick only lets that happen when no face has every character of a text.
    public static class UiFontBuilder
    {
        public const string Folder = "Assets/OurTaiko/Resources/UiFonts";
        public const string SetPath = Folder + "/UiFontSet.asset";
        const int SamplingPointSize = 64, AtlasPadding = 32, AtlasSize = 1024;

        sealed class Spec
        {
            public readonly string Name, Source;
            public readonly TextScript[] Scripts;

            public Spec(string name, string source, params TextScript[] scripts)
            {
                Name = name;
                Source = source;
                Scripts = scripts;
            }

            public string FontPath => Folder + "/" + Name + " SDF.asset";
            public string OutlinePath => Folder + "/" + Name + " SDF Outline.mat";
        }

        // In UiFontSet order: the first face is also the one for scripts no face lists.
        static readonly Spec[] Specs =
        {
            new Spec("Edo", "Assets/OurTaiko/Art/大江户勘亭流.otf", TextScript.Japanese),
            new Spec("PangWa", "Assets/OurTaiko/Art/方正胖娃GBK.ttf", TextScript.SimplifiedChinese, TextScript.TraditionalChinese),
        };

        [MenuItem("OurTaiko/Build UI Fonts")]
        public static void Build()
        {
            var fonts = Specs.Select(FontAsset).ToArray();
            for (int i = 0; i < fonts.Length; i++)
            {
                fonts[i].fallbackFontAssetTable = fonts.Where((_, j) => j != i).ToList();
                EditorUtility.SetDirty(fonts[i]);
            }

            var set = AssetDatabase.LoadAssetAtPath<UiFontSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<UiFontSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }
            var serialized = new SerializedObject(set);
            var faces = serialized.FindProperty("faces");
            faces.arraySize = Specs.Length;
            for (int i = 0; i < Specs.Length; i++)
            {
                var face = faces.GetArrayElementAtIndex(i);
                face.FindPropertyRelative("font").objectReferenceValue = fonts[i];
                face.FindPropertyRelative("outline").objectReferenceValue = Outline(Specs[i], fonts[i]);
                var scripts = face.FindPropertyRelative("scripts");
                scripts.arraySize = Specs[i].Scripts.Length;
                for (int j = 0; j < Specs[i].Scripts.Length; j++)
                    scripts.GetArrayElementAtIndex(j).enumValueIndex = (int)Specs[i].Scripts[j];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            UiFontAtlas.Regenerate();
        }

        static TMP_FontAsset FontAsset(Spec spec)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(spec.FontPath);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>(spec.Source)
                ?? throw new InvalidOperationException("Missing source font " + spec.Source);
            var font = TMP_FontAsset.CreateFontAsset(source, SamplingPointSize, AtlasPadding, GlyphRenderMode.SDFAA,
                AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true)
                ?? throw new InvalidOperationException("TMP could not load " + spec.Source + "; enable Include Font Data.");
            font.name = spec.Name + " SDF";
            AssetDatabase.CreateAsset(font, spec.FontPath);
            // UiFontAtlas empties the atlas before builds instead of TMP.
            var serialized = new SerializedObject(font);
            serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            font.atlasTextures[0].name = font.name + " Atlas";
            AssetDatabase.AddObjectToAsset(font.atlasTextures[0], font);
            font.material.name = font.name + " Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
            AssetDatabase.SaveAssets();
            return font;
        }

        // Light text's material: the face's own with an opaque black outline as wide as the dilation, so
        // the white face keeps its full weight. TMP widths are a fraction of the em.
        static Material Outline(Spec spec, TMP_FontAsset font)
        {
            var outline = AssetDatabase.LoadAssetAtPath<Material>(spec.OutlinePath);
            if (outline == null)
            {
                outline = new Material(font.material);
                AssetDatabase.CreateAsset(outline, spec.OutlinePath);
            }
            outline.CopyPropertiesFromMaterial(font.material);
            outline.name = font.name + " Outline";
            outline.EnableKeyword(ShaderUtilities.Keyword_Outline);
            outline.SetFloat(ShaderUtilities.ID_FaceDilate, SkinUi.OutlineWidth);
            outline.SetFloat(ShaderUtilities.ID_OutlineWidth, SkinUi.OutlineWidth);
            outline.SetFloat(ShaderUtilities.ID_OutlineSoftness, 0);
            outline.SetColor(ShaderUtilities.ID_FaceColor, Color.white);
            outline.SetColor(ShaderUtilities.ID_OutlineColor, Color.black);
            EditorUtility.SetDirty(outline);
            return outline;
        }
    }
}
