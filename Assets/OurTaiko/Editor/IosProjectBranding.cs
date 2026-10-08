#if UNITY_IOS
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace OurTaiko.Editor
{
    public static class IosProjectBranding
    {
        const string UnityName = "Unity-iPhone";
        const string AppName = PlayerBranding.ProductName;

        // Unity's incremental exporter expects its original project, target and resource paths.
        // Restore these just for export, then brand again after all other build callbacks.
        public static bool PrepareForExport(string root)
        {
            string branded = Path.Combine(root, AppName + ".xcodeproj");
            string original = Path.Combine(root, UnityName + ".xcodeproj");
            if (!Directory.Exists(branded)) return false;
            if (Directory.Exists(original))
                throw new BuildFailedException("Both OurTaikoPlay.xcodeproj and Unity-iPhone.xcodeproj exist. Export to a new folder to avoid overwriting project edits.");
            Rename(root, AppName, UnityName);
            return true;
        }

        [PostProcessBuild(999)]
        public static void OnPostprocessBuild(BuildTarget target, string root)
        {
            if (target != BuildTarget.iOS) return;

            // Declare no non-exempt encryption on every export, including Append builds
            // where Unity preserves the existing Info.plist.
            string plistPath = Path.Combine(root, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetBoolean("ITSAppUsesNonExemptEncryption", false);
            plist.WriteToFile(plistPath);

            Rename(root, UnityName, AppName);
        }

        public static void Rename(string root, string from, string to)
        {
            string source = Path.Combine(root, from + ".xcodeproj");
            string destination = Path.Combine(root, to + ".xcodeproj");
            if (!Directory.Exists(source))
            {
                // Upgrade exports produced by the old, main-target-only branding pass too.
                if (!Directory.Exists(destination)) throw new BuildFailedException("Missing Xcode project: " + source);
                source = destination;
            }
            if (source != destination && Directory.Exists(destination))
                throw new BuildFailedException("Xcode project already exists: " + destination);
            foreach (string suffix in new[] { "", " Tests" })
                ValidateDirectoryRename(Path.Combine(root, from + suffix), from, to);
            string file = Path.Combine(source, "project.pbxproj");
            var project = new PBXProject(); project.ReadFromFile(file);
            string main = project.TargetGuidByName(AppName);
            if (string.IsNullOrEmpty(main)) main = project.GetUnityMainTargetGuid();
            if (string.IsNullOrEmpty(main)) throw new BuildFailedException("Missing app target: " + from);
            project.SetBuildProperty(main, "PRODUCT_NAME", AppName);
            string text = RenameText(project.WriteToString(), from, to);
            // Rename the generated app and test names everywhere they are referenced, including
            // groups, products, build settings and file paths. UnityFramework keeps its identity.
            project.ReadFromString(text);
            project.SetBuildProperty(main, "PRODUCT_NAME", AppName);
            text = project.WriteToString();
            // PBXProject's serializer hardcodes this comment even for a renamed project.
            text = text.Replace("PBXProject \"" + UnityName + "\"", "PBXProject \"" + to + "\"");
            var target = new Regex(@"(?m)^(\s*" + Regex.Escape(main) + @" /\* [^\r\n]* \*/ = \{\r?\n)(.*?)(^\s*\};)", RegexOptions.Singleline | RegexOptions.Multiline);
            var match = target.Match(text);
            if (!match.Success) throw new BuildFailedException("Cannot locate app target in Xcode project.");
            string body = Regex.Replace(match.Groups[2].Value, @"(?m)^(\s*name = )[^;]+;", "$1\"" + to + "\";");
            body = Regex.Replace(body, @"(?m)^(\s*productName = )[^;]+;", "$1" + AppName + ";");
            text = text.Substring(0, match.Index) + match.Groups[1].Value + body + match.Groups[3].Value + text.Substring(match.Index + match.Length);
            string productGuid = project.GetTargetProductFileRef(main);
            var product = new Regex(@"(?m)^(\s*" + Regex.Escape(productGuid) + @" /\* [^\r\n]* \*/ = \{)(.*?)(\};)", RegexOptions.Singleline | RegexOptions.Multiline);
            text = product.Replace(text, m => m.Groups[1].Value
                + Regex.Replace(m.Groups[2].Value, @"\bpath = [^;]+;", "path = " + AppName + ".app;") + m.Groups[3].Value, 1);
            text = text.Replace(productGuid + " /* Unity-Target-New.app */", productGuid + " /* " + AppName + ".app */");
            File.WriteAllText(file, text);

            foreach (string schemePath in Directory.GetFiles(source, "*.xcscheme", SearchOption.AllDirectories))
            {
                var scheme = XDocument.Load(schemePath);
                foreach (var attribute in scheme.Descendants().Attributes())
                    attribute.Value = RenameText(attribute.Value, from, to);
                foreach (var reference in scheme.Descendants("BuildableReference"))
                {
                    if ((string)reference.Attribute("BlueprintIdentifier") == main)
                    {
                        reference.SetAttributeValue("BlueprintName", to);
                        reference.SetAttributeValue("BuildableName", AppName + ".app");
                    }
                }
                scheme.Save(schemePath);
                string renamedScheme = Path.Combine(Path.GetDirectoryName(schemePath), RenameText(Path.GetFileName(schemePath), from, to));
                if (schemePath != renamedScheme) File.Move(schemePath, renamedScheme);
            }
            foreach (string managementPath in Directory.GetFiles(source, "xcschememanagement.plist", SearchOption.AllDirectories))
            {
                var plist = new PlistDocument(); plist.ReadFromFile(managementPath);
                if (plist.root.values.TryGetValue("SchemeUserState", out var state))
                {
                    var entries = state.AsDict().values;
                    foreach (string key in entries.Keys.Where(k => RenameText(k, from, to) != k).ToArray())
                    { var value = entries[key]; entries.Remove(key); entries[RenameText(key, from, to)] = value; }
                    plist.WriteToFile(managementPath);
                }
            }
            foreach (string workspace in Directory.GetFiles(source, "contents.xcworkspacedata", SearchOption.AllDirectories))
                File.WriteAllText(workspace, RenameText(File.ReadAllText(workspace), from, to));
            foreach (string suffix in new[] { "", " Tests" })
                RenameDirectory(Path.Combine(root, from + suffix), from, to);
            if (source != destination) Directory.Move(source, destination);
        }

        static string RenameText(string text, string from, string to) => text
            .Replace(from.Replace('-', '_') + "_Tests", to.Replace('-', '_') + "_Tests")
            .Replace(from, to);

        static void ValidateDirectoryRename(string path, string from, string to)
        {
            if (!Directory.Exists(path)) return;
            string destination = Path.Combine(Path.GetDirectoryName(path), RenameText(Path.GetFileName(path), from, to));
            if (path != destination && (Directory.Exists(destination) || File.Exists(destination)))
                throw new BuildFailedException("Cannot rename export directory; destination already exists: " + destination);
            foreach (string child in Directory.GetDirectories(path)) ValidateDirectoryRename(child, from, to);
            foreach (string file in Directory.GetFiles(path))
            {
                string renamed = Path.Combine(path, RenameText(Path.GetFileName(file), from, to));
                if (file != renamed && (File.Exists(renamed) || Directory.Exists(renamed)))
                    throw new BuildFailedException("Cannot rename export file; destination already exists: " + renamed);
            }
        }

        static void RenameDirectory(string path, string from, string to)
        {
            if (!Directory.Exists(path)) return;
            foreach (string child in Directory.GetDirectories(path)) RenameDirectory(child, from, to);
            foreach (string file in Directory.GetFiles(path))
            {
                string renamed = Path.Combine(path, RenameText(Path.GetFileName(file), from, to));
                if (file != renamed) File.Move(file, renamed);
            }
            string destination = Path.Combine(Path.GetDirectoryName(path), RenameText(Path.GetFileName(path), from, to));
            if (path != destination) Directory.Move(path, destination);
        }
    }
}
#endif
