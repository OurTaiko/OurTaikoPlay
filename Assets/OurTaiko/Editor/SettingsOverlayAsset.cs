using UnityEditor;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Resources/SettingsOverlay.prefab: the settings overlay and the sounds it plays. The overlay
    // builds its hierarchy at runtime, so the prefab holds only the component and its clips.
    // Rebuilding keeps the prefab's GUID.
    public static class SettingsOverlayAsset
    {
        const string Path = "Assets/OurTaiko/Resources/" + SettingsOverlay.ResourceName + ".prefab";

        [MenuItem("OurTaiko/Build Settings Overlay")]
        public static void Build()
        {
            var root = new GameObject(SettingsOverlay.ResourceName);
            try
            {
                root.AddComponent<AudioSource>().playOnAwake = false;
                var overlay = root.AddComponent<SettingsOverlay>();
                overlay.don = Clip("Assets/OurTaiko/Audio/don.ogg");
                overlay.ka = Clip("Assets/OurTaiko/Audio/ka.ogg");
                // The voice the old settings scene previewed Voice Volume with.
                overlay.previewVoice = Clip("Assets/OurTaiko/Audio/entry/select_mode.ogg");
                PrefabUtility.SaveAsPrefabAsset(root, Path);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
        }

        static AudioClip Clip(string path) => AssetDatabase.LoadAssetAtPath<AudioClip>(path)
            ?? throw new System.InvalidOperationException("Missing " + path);
    }
}
