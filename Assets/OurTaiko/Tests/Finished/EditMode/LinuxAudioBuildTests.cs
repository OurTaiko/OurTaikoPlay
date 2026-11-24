using System;
using System.IO;
using NUnit.Framework;
using OurTaiko.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class LinuxAudioBuildTests
    {
        static bool failBuild;
        static bool Disabled() => new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(LinuxPlayerAudioOutput.AssetPath)[0])
            .FindProperty("m_DisableAudio").boolValue;

        [TestCase(BuildTarget.StandaloneLinux64)]
        [TestCase(BuildTarget.StandaloneWindows64)]
        [TestCase(BuildTarget.StandaloneOSX)]
        [TestCase(BuildTarget.Android)]
        [TestCase(BuildTarget.iOS)]
        public void OnlyLinuxTemporarilyDisablesUnityOutputAndRestoresOnFailure(BuildTarget target)
        {
            byte[] saved = File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath);
            bool original = Disabled();
            Assert.Throws<IOException>(() =>
            {
                using var scope = new LinuxPlayerAudioOutput(target);
                Assert.That(Disabled(), Is.EqualTo(target == BuildTarget.StandaloneLinux64 || original));
                LinuxPlayerAudioOutput.Validate(target);
                throw new IOException("Expected test failure");
            });
            Assert.That(File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath), Is.EqualTo(saved));
            Assert.That(Disabled(), Is.EqualTo(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LinuxScopeRestoresAnEnabledOrDisabledProjectOnSuccess(bool originallyDisabled)
        {
            byte[] saved = File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath);
            try
            {
                var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(LinuxPlayerAudioOutput.AssetPath)[0]);
                manager.FindProperty("m_DisableAudio").boolValue = originallyDisabled;
                manager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                byte[] configured = File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath);
                if (originallyDisabled) Assert.DoesNotThrow(() => LinuxPlayerAudioOutput.Validate(BuildTarget.StandaloneLinux64));
                else Assert.Throws<BuildFailedException>(() => LinuxPlayerAudioOutput.Validate(BuildTarget.StandaloneLinux64));
                using (var scope = new LinuxPlayerAudioOutput(BuildTarget.StandaloneLinux64)) Assert.That(Disabled(), Is.True);
                Assert.That(Disabled(), Is.EqualTo(originallyDisabled));
                Assert.That(File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath), Is.EqualTo(configured));
            }
            finally
            {
                File.WriteAllBytes(LinuxPlayerAudioOutput.AssetPath, saved);
                AssetDatabase.ImportAsset(LinuxPlayerAudioOutput.AssetPath, ImportAssetOptions.ForceUpdate);
            }
        }

        // Run as executeMethod, not inside Test Runner: this exercises an actual failed build
        // after native-audio resources have been generated, not merely a throwing mock delegate.
        public static void VerifyFailedBuild()
        {
            byte[] saved = File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath);
            bool observed = false;
            failBuild = true;
            try { PlayerBuilds.BuildLinux(); }
            catch (BuildFailedException error)
            {
                if (!error.ToString().Contains("Intentional Linux audio restoration failure")) throw;
                observed = true;
            }
            finally { failBuild = false; }
            if (!observed || !System.Linq.Enumerable.SequenceEqual(saved, File.ReadAllBytes(LinuxPlayerAudioOutput.AssetPath))
                || AssetDatabase.IsValidFolder("Assets/OurTaikoNativeAudioBuild"))
                throw new Exception("Failed Linux build did not restore its audio settings/resources");
            Debug.Log("LINUX ACTUAL FAILED BUILD RESTORE PASS");
        }

        public sealed class FailLinuxBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 10000;
            public void OnPreprocessBuild(BuildReport report)
            {
                if (!failBuild) return;
                if (report.summary.platform != BuildTarget.StandaloneLinux64 || !Disabled())
                    throw new BuildFailedException("Linux output policy did not apply before build callbacks");
                throw new BuildFailedException("Intentional Linux audio restoration failure");
            }
        }
    }
}
