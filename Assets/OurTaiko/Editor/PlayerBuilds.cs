using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Menus survive platform/domain reloads; batch builds select -buildTarget before executeMethod.
    [InitializeOnLoad]
    public static class PlayerBuilds
    {
        public const string MobileIdentifier = "org.ourtaiko.play";
        const string PendingKey = "OurTaiko.PlayerBuilds.Pending";
        static bool building;
        static readonly double loadedAt;

        [Serializable]
        sealed class QueueState { public BuildTarget[] targets; public int next; }

        [Serializable]
        sealed class Result
        {
            public string platform, status, output, timestamp, message;
            public int errors, warnings;
            public long bytes;
            public double seconds;
        }

        static PlayerBuilds()
        {
            loadedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update += ResumeQueue;
        }

        [MenuItem("OurTaiko/Build/Configure Platforms")]
        public static void ConfigurePlatforms()
        {
            PlayerBranding.Configure();
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, MobileIdentifier);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, MobileIdentifier);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.iOS, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;
            AssetDatabase.SaveAssets();
        }

        [MenuItem("OurTaiko/Build/All Platforms")]
        public static void QueueAll() => Enqueue(BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows64, BuildTarget.Android, BuildTarget.iOS);
        [MenuItem("OurTaiko/Build/macOS Universal")]
        public static void QueueMacOS() => Enqueue(BuildTarget.StandaloneOSX);
        [MenuItem("OurTaiko/Build/Windows x64")]
        public static void QueueWindows() => Enqueue(BuildTarget.StandaloneWindows64);
        [MenuItem("OurTaiko/Build/Android ARM64 APK")]
        public static void QueueAndroid() => Enqueue(BuildTarget.Android);
        [MenuItem("OurTaiko/Build/iOS Xcode Project")]
        public static void QueueIOS() => Enqueue(BuildTarget.iOS);

        public static void BuildMacOS() => Build(BuildTarget.StandaloneOSX);
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64);
        public static void BuildAndroid() => Build(BuildTarget.Android);
        public static void BuildIOS() => Build(BuildTarget.iOS);

        static void Enqueue(params BuildTarget[] targets)
        {
            if (building || SessionState.GetString(PendingKey, "") != "")
                throw new InvalidOperationException("An OurTaiko build is already queued.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building.");
            // Do not silently discard or save authored scene edits when switching platforms.
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            foreach (var target in targets) CheckModule(target);
            ConfigurePlatforms();
            SessionState.SetString(PendingKey, JsonUtility.ToJson(new QueueState { targets = targets }));
            WriteStatus(new Result { status = "Queued", platform = targets[0].ToString() });
        }

        static void ResumeQueue()
        {
            if (building || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.timeSinceStartup - loadedAt < 2) return;
            string pending = SessionState.GetString(PendingKey, "");
            if (pending == "") return;
            var queue = JsonUtility.FromJson<QueueState>(pending);
            try
            {
                if (EditorUtility.scriptCompilationFailed) throw new BuildFailedException("Platform scripts failed to compile.");
                var target = queue.targets[queue.next];
                if (EditorUserBuildSettings.activeBuildTarget != target)
                {
                    WriteStatus(new Result { status = "Switching", platform = target.ToString() });
                    if (!EditorUserBuildSettings.SwitchActiveBuildTargetAsync(BuildPipeline.GetBuildTargetGroup(target), target))
                        throw new BuildFailedException("Could not switch to " + target);
                    return;
                }
                building = true;
                Build(target);
                queue.next++;
                SessionState.SetString(PendingKey, queue.next == queue.targets.Length ? "" : JsonUtility.ToJson(queue));
            }
            catch (Exception error)
            {
                SessionState.EraseString(PendingKey);
                WriteStatus(new Result { status = "Failed", platform = queue.targets[queue.next].ToString(), message = error.ToString() });
                Debug.LogException(error);
            }
            finally { building = false; }
        }

        public static string OutputPath(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneOSX: return "Builds/macOS/OurTaikoPlay.app";
                case BuildTarget.StandaloneWindows64: return "Builds/Windows/OurTaikoPlay.exe";
                case BuildTarget.Android: return "Builds/Android/OurTaikoPlay.apk";
                case BuildTarget.iOS: return "Builds/iOS";
                default: throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        static void CheckModule(BuildTarget target)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new BuildFailedException("Install the Unity " + Application.unityVersion + " build module for " + target + ".");
        }

        static void Build(BuildTarget target)
        {
            CheckModule(target);
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new BuildFailedException("Select " + target + " first. In batch mode pass -buildTarget; in the Editor use OurTaiko/Build.");
            ConfigurePlatforms();
#if UNITY_STANDALONE_OSX
            // This API lives in the macOS module; other Editor hosts need not install it.
            if (target == BuildTarget.StandaloneOSX)
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = OSArchitecture.x64ARM64;
#endif
            if (target == BuildTarget.Android)
            {
                EditorUserBuildSettings.buildAppBundle = false;
                EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            }
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0 || scenes[0] != "Assets/Scenes/Entry.unity")
                throw new BuildFailedException("Enabled scenes must start with Entry.");
            string output = Path.GetFullPath(OutputPath(target));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            WriteStatus(new Result { status = "Building", platform = target.ToString(), output = output });
            var preloaded = PlayerSettings.GetPreloadedAssets();
            try
            {
                var options = BuildOptions.CompressWithLz4HC;
#if UNITY_IOS
                if (target == BuildTarget.iOS && IosProjectBranding.PrepareForExport(output))
                    options |= BuildOptions.AcceptExternalModificationsToPlayer;
#endif
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, target = target, locationPathName = output,
                    options = options
                });
                var summary = report.summary;
                var result = new Result
                {
                    platform = target.ToString(), status = summary.result.ToString(), output = output,
                    errors = (int)summary.totalErrors, warnings = (int)summary.totalWarnings,
                    bytes = (long)summary.totalSize, seconds = summary.totalTime.TotalSeconds,
                    message = string.Join("\n", report.steps.SelectMany(s => s.messages)
                        .Where(m => m.type == LogType.Error || m.type == LogType.Exception).Select(m => m.content))
                };
                WriteStatus(result);
                Directory.CreateDirectory("Builds/Reports");
                File.WriteAllText("Builds/Reports/" + target + ".json", JsonUtility.ToJson(result, true));
                if (summary.result != BuildResult.Succeeded) throw new BuildFailedException(result.message);
                Debug.Log("OurTaiko " + target + " build: " + output);
            }
            finally
            {
                // Input System's build hook temporarily adds its settings to this project list.
                PlayerSettings.SetPreloadedAssets(preloaded);
                AssetDatabase.SaveAssets();
            }
        }

        static void WriteStatus(Result result)
        {
            Directory.CreateDirectory("Builds");
            result.timestamp = DateTime.UtcNow.ToString("O");
            File.WriteAllText("Builds/build-status.json", JsonUtility.ToJson(result, true));
        }
    }
}
