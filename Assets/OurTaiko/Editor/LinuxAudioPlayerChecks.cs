using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Explicit validation Player: normal builds never define OURTAIKO_AUDIO_PROBE.
    public static class LinuxAudioPlayerChecks
    {
        [Serializable] sealed class Item { public string resource, hash; public float length; public int samples, frequency, channels; }
        [Serializable] sealed class Manifest { public Item[] items; }
        public static void Build()
        {
            const string resources = "Assets/OurTaikoAudioProbe/Resources";
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64)
                throw new InvalidOperationException("Select StandaloneLinux64 before building the audio-check Player");
            var preloaded = PlayerSettings.GetPreloadedAssets();
            LinuxPlayerAudioOutput unityAudio = null;
            try
            {
                using var hash = SHA256.Create();
                var items = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/OurTaiko" }).Select(guid =>
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    return new Item { resource = "NativeAudio/" + guid, length = clip.length, samples = clip.samples,
                        frequency = clip.frequency, channels = clip.channels,
                        hash = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))) };
                }).ToArray();
                Directory.CreateDirectory(resources);
                File.WriteAllText(resources + "/LinuxAudioProbeManifest.json", JsonUtility.ToJson(new Manifest { items = items }));
                AssetDatabase.Refresh();
                unityAudio = new LinuxPlayerAudioOutput(BuildTarget.StandaloneLinux64);
                var args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-customBuildPath");
                if (index < 0 || index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    throw new ArgumentException("-customBuildPath is required for an isolated audio-check Player");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    target = BuildTarget.StandaloneLinux64, locationPathName = args[index + 1],
                    options = BuildOptions.Development | BuildOptions.CompressWithLz4HC,
                    extraScriptingDefines = new[] { "OURTAIKO_AUDIO_PROBE" }
                });
                if (report.summary.result != BuildResult.Succeeded) throw new Exception("Linux audio-check build failed");
            }
            finally
            {
                try
                {
                    PlayerSettings.SetPreloadedAssets(preloaded);
                    AssetDatabase.DeleteAsset("Assets/OurTaikoAudioProbe");
                    AudioAssetBuild.Cleanup();
                    AssetDatabase.SaveAssets();
                }
                finally { unityAudio?.Dispose(); }
                Debug.Log("LINUX AUDIO CHECK BUILD FINISHED");
            }
        }
    }
}
