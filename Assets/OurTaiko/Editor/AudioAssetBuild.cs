using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Generated only for player builds. Original encoded files are packaged losslessly as bytes;
    // scene AudioClip references are lookup keys and never decoded by the native backend.
    public sealed class AudioAssetBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string Root = "Assets/OurTaikoNativeAudioBuild";
        public int callbackOrder => 10;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL) Cleanup();
            else Generate();
        }
        public void OnPostprocessBuild(BuildReport report) => Cleanup();
        public static void Cleanup() => AssetDatabase.DeleteAsset(Root);
        public static void Generate()
        {
            Cleanup();
            Directory.CreateDirectory(Root + "/Resources/NativeAudio");
            var entries = new List<AudioAssetCatalog.Entry>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/OurTaiko" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string resource = "NativeAudio/" + guid;
                File.Copy(path, Root + "/Resources/" + resource + ".bytes", true);
                entries.Add(new AudioAssetCatalog.Entry { clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path), resource = resource });
            }
            AssetDatabase.Refresh();
            var catalog = ScriptableObject.CreateInstance<AudioAssetCatalog>();
            catalog.entries = entries.ToArray();
            AssetDatabase.CreateAsset(catalog, Root + "/Resources/NativeAudioCatalog.asset");
            AssetDatabase.SaveAssets();
        }
    }
}
