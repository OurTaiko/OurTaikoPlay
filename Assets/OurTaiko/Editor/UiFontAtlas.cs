using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Glyphs TMP adds while the Editor or tests run are written into the dynamic UI font assets
    // (the faces of UiFontSet). Their atlases are emptied so each saved asset only depends on its
    // source font; every glyph is added on demand at runtime. Extra atlas pages get new file IDs
    // each time they are created, so none are kept and the result is identical on every run. This
    // happens on its own whenever Play mode exits and before every build, which replaces TMP's Clear
    // Dynamic Data On Build (that one shrinks the atlas to 1x1 and keeps the ligature table, so the
    // asset never matched what is committed). TMP text in an open scene adds glyphs in Edit mode
    // too, right after that cleanup, so any other save of the assets (Ctrl+S, quitting the Editor)
    // is dropped; only Regenerate writes them.
    [InitializeOnLoad]
    public static class UiFontAtlas
    {
        static bool regenerating;

        static UiFontAtlas()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    Regenerate();
            };
        }

        // The faces' fonts; none before UiFontBuilder has made the set.
        static TMP_FontAsset[] Fonts()
        {
            var set = AssetDatabase.LoadAssetAtPath<UiFontSet>(UiFontBuilder.SetPath);
            return set == null ? Array.Empty<TMP_FontAsset>() : set.Faces.Select(face => face.font).Where(font => font != null).ToArray();
        }

        [MenuItem("OurTaiko/Regenerate UI Font Atlas")]
        public static void Regenerate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before regenerating the UI font atlas.");
            var fonts = Fonts();
            foreach (var font in fonts)
            {
                font.ClearFontAssetData();
                EditorUtility.SetDirty(font);
            }
            regenerating = true;
            try
            {
                foreach (var font in fonts) AssetDatabase.SaveAssetIfDirty(font);
            }
            finally
            {
                regenerating = false;
            }
            Debug.Log("OurTaiko: regenerated the atlases of " + fonts.Length + " UI fonts.");
        }

        sealed class SaveFilter : AssetModificationProcessor
        {
            static string[] OnWillSaveAssets(string[] paths)
            {
                if (regenerating) return paths;
                var fonts = new HashSet<string>(Fonts().Select(AssetDatabase.GetAssetPath));
                return paths.Where(path => !fonts.Contains(path)).ToArray();
            }
        }

        sealed class BeforeBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report) => Regenerate();
        }
    }
}
