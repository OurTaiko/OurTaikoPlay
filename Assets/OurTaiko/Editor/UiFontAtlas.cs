using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Glyphs TMP adds while the Editor or tests run are written into the dynamic font asset.
    // Before committing, empty the atlas so the saved asset only depends on DDFont.ttf; every
    // glyph is added on demand at runtime. Extra atlas pages get new file IDs each time they
    // are created, so none are kept and the result is identical on every run.
    public static class UiFontAtlas
    {
        const string UiFontPath = "Assets/OurTaiko/Resources/Nijiiro UI SDF.asset";

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
    }
}
