using System;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Glyphs TMP adds while the Editor or tests run are written into the dynamic font asset.
    // The atlas is emptied so the saved asset only depends on DDFont.ttf; every glyph is added
    // on demand at runtime. Extra atlas pages get new file IDs each time they are created, so
    // none are kept and the result is identical on every run. This happens on its own whenever
    // Play mode exits and before every build, which replaces TMP's Clear Dynamic Data On Build
    // (that one shrinks the atlas to 1x1 and keeps the ligature table, so the asset never
    // matched what is committed).
    [InitializeOnLoad]
    public static class UiFontAtlas
    {
        const string UiFontPath = "Assets/OurTaiko/Resources/Nijiiro UI SDF.asset";

        static UiFontAtlas()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Regenerate();
            };
        }

        [MenuItem("OurTaiko/Regenerate UI Font Atlas")]
        public static void Regenerate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before regenerating the UI font atlas.");
            var uiFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFontPath)
                ?? throw new InvalidOperationException("The UI font " + UiFontPath + " is missing.");
            uiFont.ClearFontAssetData();
            EditorUtility.SetDirty(uiFont);
            AssetDatabase.SaveAssetIfDirty(uiFont);
            Debug.Log("OurTaiko: regenerated the UI font atlas with " + uiFont.characterTable.Count + " characters.");
        }

        sealed class BeforeBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report) => Regenerate();
        }
    }
}
