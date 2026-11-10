using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace OurTaiko.Editor
{
    // Only the Linux Player build disables Unity's unused FMOD output. BASS owns playback;
    // otherwise FMOD can block before managed startup when the selected PCM is busy.
    // Restore the authored project byte-for-byte even when BuildPipeline throws or fails.
    public sealed class LinuxPlayerAudioOutput : IDisposable
    {
        public const string AssetPath = "ProjectSettings/AudioManager.asset";
        byte[] saved;

        public static void Validate(BuildTarget target)
        {
            if (target != BuildTarget.StandaloneLinux64) return;
            var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(AssetPath)[0]);
            if (!manager.FindProperty("m_DisableAudio").boolValue)
                throw new BuildFailedException("Build Linux through PlayerBuilds.BuildLinux (scoped Unity audio output policy)");
        }

        public LinuxPlayerAudioOutput(BuildTarget target)
        {
            if (target != BuildTarget.StandaloneLinux64) return;
            saved = File.ReadAllBytes(AssetPath);
            try
            {
                var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(AssetPath)[0]);
                var disabled = manager.FindProperty("m_DisableAudio");
                if (disabled == null) throw new InvalidOperationException("Unity AudioManager has no disable-output setting");
                disabled.boolValue = true;
                manager.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (saved == null) return;
            byte[] original = saved;
            saved = null;
            File.WriteAllBytes(AssetPath, original);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            if (!File.ReadAllBytes(AssetPath).SequenceEqual(original))
                throw new IOException("Linux build did not restore AudioManager byte-for-byte");
        }
    }
}
