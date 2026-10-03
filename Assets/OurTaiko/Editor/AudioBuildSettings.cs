using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    public sealed class AudioBuildSettings : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.StandaloneWindows)
                throw new BuildFailedException("The supplied BASS Windows libraries are x64. Select Windows x86_64.");
            Configure();
        }

        [MenuItem("OurTaiko/Configure Native Audio")]
        public static void Configure()
        {
            var targetName = UnityEditor.Build.NamedBuildTarget.iOS;
            var symbols = PlayerSettings.GetScriptingDefineSymbols(targetName);
            if (!System.Array.Exists(symbols.Split(';'), symbol => symbol == "__STATIC_LINKING__"))
                PlayerSettings.SetScriptingDefineSymbols(targetName, string.IsNullOrEmpty(symbols) ? "__STATIC_LINKING__" : symbols + ";__STATIC_LINKING__");
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/Plugins/Un4seen.Bass/", StringComparison.Ordinal)) continue;
                if (!(AssetImporter.GetAtPath(path) is PluginImporter plugin)) continue;
                // Nested files in xcframeworks are handled by the framework importer.
                if (path.Contains(".xcframework/")) continue;
                plugin.SetCompatibleWithAnyPlatform(false);
                plugin.SetCompatibleWithEditor(false);
                foreach (var target in new[] { BuildTarget.StandaloneOSX, BuildTarget.StandaloneLinux64,
                    BuildTarget.StandaloneWindows, BuildTarget.StandaloneWindows64, BuildTarget.Android, BuildTarget.iOS, BuildTarget.WebGL })
                    plugin.SetCompatibleWithPlatform(target, false);
                if (path.Contains("/macOS/"))
                {
                    plugin.SetCompatibleWithEditor(true); plugin.SetEditorData("OS", "OSX"); plugin.SetEditorData("CPU", "AnyCPU");
                    plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, true);
                    plugin.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "AnyCPU");
                }
                else if (path.Contains("/Windows/"))
                {
                    plugin.SetCompatibleWithEditor(true); plugin.SetEditorData("OS", "Windows"); plugin.SetEditorData("CPU", "x86_64");
                    plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                    plugin.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                }
                else if (path.Contains("/Linux/"))
                {
                    plugin.SetCompatibleWithEditor(true); plugin.SetEditorData("OS", "Linux"); plugin.SetEditorData("CPU", "x86_64");
                    plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, true);
                    plugin.SetPlatformData(BuildTarget.StandaloneLinux64, "CPU", "x86_64");
                }
                else if (path.Contains("/Android/"))
                {
                    plugin.SetCompatibleWithPlatform(BuildTarget.Android, true);
                    string arch = path.Contains("/arm64-v8a/") ? "ARM64" : path.Contains("/armeabi-v7a/") ? "ARMv7" : path.Contains("/x86_64/") ? "x86_64" : "x86";
                    plugin.SetPlatformData(BuildTarget.Android, "CPU", arch);
                }
                else if (path.EndsWith(".xcframework", StringComparison.Ordinal))
                {
                    plugin.SetCompatibleWithPlatform(BuildTarget.iOS, true);
                    plugin.SetPlatformData(BuildTarget.iOS, "AddToEmbeddedBinaries", "true");
                }
                plugin.SaveAndReimport();
            }
            // Native playback reads the original file. Keep Unity references unloaded; the explicit Unity backend loads them on demand.
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/OurTaiko" }))
            {
                var importer = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) as AudioImporter;
                if (importer == null) continue;
                var settings = importer.defaultSampleSettings;
                if (settings.loadType == AudioClipLoadType.CompressedInMemory && !settings.preloadAudioData) continue;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }
        }
#if UNITY_IOS
        [UnityEditor.Callbacks.PostProcessBuild(45)]
        static void ConfigureIos(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var project = new UnityEditor.iOS.Xcode.PBXProject();
            string file = UnityEditor.iOS.Xcode.PBXProject.GetPBXProjectPath(path);
            project.ReadFromFile(file);
            foreach (string id in new[] { project.GetUnityMainTargetGuid(), project.GetUnityFrameworkTargetGuid() })
                project.AddBuildProperty(id, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks");
            project.AddFrameworkToProject(project.GetUnityFrameworkTargetGuid(), "libc++.tbd", false);
            project.WriteToFile(file);
        }
#endif
    }
}
