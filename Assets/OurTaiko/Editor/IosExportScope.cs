using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace OurTaiko.Editor
{
    // Unity only appends to an export under its own project name, so a branded
    // OurTaikoPlay.xcodeproj goes back to Unity-iPhone.xcodeproj for the build.
    // IosProjectBranding's post-process brands it again after a successful build;
    // disposing this scope does so when the build failed before reaching it.
    // The Xcode-specific rename and Unity's append check are passed in, so this
    // also compiles and is tested without the iOS build module.
    public sealed class IosExportScope : IDisposable
    {
        public const string UnityName = "Unity-iPhone";
        const string AppName = PlayerBranding.ProductName;

        readonly string root;
        readonly Action<string, string> rename;

        // True when an existing export is appended (BuildOptions.AcceptExternalModificationsToPlayer).
        public bool Appending { get; }

        public IosExportScope(string root, Action<string, string> rename, Func<CanAppendBuild> canAppend)
        {
            this.root = root;
            this.rename = rename;
            Appending = Prepare(root, rename, canAppend);
        }

        // rename(from, to) renames the export's project; canAppend asks Unity about the unbranded export.
        public static bool Prepare(string root, Action<string, string> rename, Func<CanAppendBuild> canAppend)
        {
            if (!Directory.Exists(Project(root, AppName))) return false;
            if (Directory.Exists(Project(root, UnityName)))
                throw new BuildFailedException("Both " + AppName + ".xcodeproj and " + UnityName + ".xcodeproj exist in " + root
                    + ". Export to a new folder to avoid overwriting project edits.");
            rename(AppName, UnityName);
            CanAppendBuild result;
            try { result = canAppend(); }
            catch { rename(UnityName, AppName); throw; }
            if (result == CanAppendBuild.Yes) return true;
            rename(UnityName, AppName);
            throw new BuildFailedException("The Xcode export in " + root + " cannot be appended (" + result
                + "); it was probably made by another Unity version. Export to a new folder, or move the old export to Builds/Backups.");
        }

        public void Dispose()
        {
            // A successful build was already branded by the post-process.
            if (!Appending || !Directory.Exists(Project(root, UnityName)) || Directory.Exists(Project(root, AppName))) return;
            try { rename(UnityName, AppName); }
            catch (Exception error)
            {
                // Dispose runs while the build's own exception propagates; report this one without replacing it.
                Debug.LogError("Could not rename " + UnityName + ".xcodeproj in " + root + " back to " + AppName
                    + ".xcodeproj after the failed build; the next export will fail until it is renamed.");
                Debug.LogException(error);
            }
        }

        static string Project(string root, string name) => Path.Combine(root, name + ".xcodeproj");
    }
}
