using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Also runs for Unity's regular Build Profiles button, not only the OurTaiko menus.
    [InitializeOnLoad]
    public sealed class PlayerBranding : IPreprocessBuildWithReport
    {
        public const string ProductName = "OurTaikoPlay";
        public const string IconPath = "Assets/OurTaiko/Branding/AppIcon.png";
        const string ForegroundPath = "Assets/OurTaiko/Branding/AndroidForeground.png";
        const string BackgroundPath = "Assets/OurTaiko/Branding/AndroidBackground.png";
        public int callbackOrder => -100;

        static PlayerBranding()
        {
#if UNITY_IOS
            // BuildPipeline validates Append before invoking preprocess callbacks.
            BuildPlayerWindow.RegisterBuildPlayerHandler(options =>
            {
                if (options.target == BuildTarget.iOS && IosProjectBranding.PrepareForExport(options.locationPathName))
                    options.options |= BuildOptions.AcceptExternalModificationsToPlayer;
                BuildPlayerWindow.DefaultBuildMethods.BuildPlayer(options);
            });
#endif
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            Configure();
#if UNITY_IOS
            if (report.summary.platform == BuildTarget.iOS)
                IosProjectBranding.PrepareForExport(report.summary.outputPath);
#endif
        }

        [MenuItem("OurTaiko/Build/Configure Name and Icons")]
        public static void Configure()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.iOS.applicationDisplayName = ProductName;
            ImportIcon(IconPath);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null) throw new BuildFailedException("Missing application icon: " + IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone,
                PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any).Select(_ => icon).ToArray(), IconKind.Any);

            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
                foreach (var slot in slots) slot.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, slots);
            }

            // Same 20% inset and white background as OurTaikoPlayer's adaptive launcher icon.
            EnsureAdaptiveImages();
            var foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(ForegroundPath);
            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var slot in slots)
                    if (slot.minLayerCount == 2) slot.SetTextures(new[] { background, foreground });
                    else slot.SetTexture(icon);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }
            AssetDatabase.SaveAssets();
        }

        static void ImportIcon(string path)
        {
            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new BuildFailedException("Missing application icon: " + path);
            if (importer.textureType == TextureImporterType.Default && !importer.mipmapEnabled
                && importer.npotScale == TextureImporterNPOTScale.None && importer.maxTextureSize == 2048
                && importer.textureCompression == TextureImporterCompression.Uncompressed) return;
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static void EnsureAdaptiveImages()
        {
            var source = new Texture2D(2, 2, TextureFormat.RGB24, false);
            var foreground = new Texture2D(1024, 1024, TextureFormat.RGBA32, false);
            var background = new Texture2D(4, 4, TextureFormat.RGB24, false);
            try
            {
                source.LoadImage(File.ReadAllBytes(IconPath));
                var pixels = new Color32[1024 * 1024];
                const int inset = 205, size = 614;
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                        pixels[(y + inset) * 1024 + x + inset] = source.GetPixelBilinear((x + .5f) / size, (y + .5f) / size);
                foreground.SetPixels32(pixels); foreground.Apply();
                background.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray()); background.Apply();
                SavePng(ForegroundPath, foreground.EncodeToPNG());
                SavePng(BackgroundPath, background.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(foreground);
                UnityEngine.Object.DestroyImmediate(background);
            }
            ImportIcon(ForegroundPath); ImportIcon(BackgroundPath);
        }

        static void SavePng(string path, byte[] bytes)
        {
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
        }
    }
}
