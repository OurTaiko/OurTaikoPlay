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

        // Unity's incremental exporter expects its original project and target names.
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
            if (target == BuildTarget.iOS) Rename(root, UnityName, AppName);
        }

        public static void Rename(string root, string from, string to)
        {
            string source = Path.Combine(root, from + ".xcodeproj");
            string destination = Path.Combine(root, to + ".xcodeproj");
            if (!Directory.Exists(source))
            {
                if (Directory.Exists(destination)) return;
                throw new BuildFailedException("Missing Xcode project: " + source);
            }
            if (Directory.Exists(destination)) throw new BuildFailedException("Xcode project already exists: " + destination);
            string file = Path.Combine(source, "project.pbxproj");
            var project = new PBXProject(); project.ReadFromFile(file);
            string main = from == UnityName ? project.GetUnityMainTargetGuid() : project.TargetGuidByName(from);
            if (string.IsNullOrEmpty(main)) throw new BuildFailedException("Missing app target: " + from);
            project.SetBuildProperty(main, "PRODUCT_NAME", AppName);
            string text = project.WriteToString();
            // PBXProject exposes build settings but no target rename API. Change only the
            // verified main target's name; preserve file paths, UnityFramework and native libraries.
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
            text = text.Replace("/* " + from + " */", "/* " + to + " */")
                .Replace("remoteInfo = \"" + from + "\";", "remoteInfo = \"" + to + "\";");
            File.WriteAllText(file, text);

            foreach (string schemePath in Directory.GetFiles(source, "*.xcscheme", SearchOption.AllDirectories))
            {
                var scheme = XDocument.Load(schemePath);
                foreach (var reference in scheme.Descendants("BuildableReference"))
                {
                    if ((string)reference.Attribute("ReferencedContainer") == "container:" + from + ".xcodeproj")
                        reference.SetAttributeValue("ReferencedContainer", "container:" + to + ".xcodeproj");
                    if ((string)reference.Attribute("BlueprintIdentifier") == main)
                    {
                        reference.SetAttributeValue("BlueprintName", to);
                        reference.SetAttributeValue("BuildableName", AppName + ".app");
                    }
                }
                scheme.Save(schemePath);
                if (Path.GetFileNameWithoutExtension(schemePath) == from)
                    File.Move(schemePath, Path.Combine(Path.GetDirectoryName(schemePath), to + ".xcscheme"));
            }
            foreach (string managementPath in Directory.GetFiles(source, "xcschememanagement.plist", SearchOption.AllDirectories))
            {
                var plist = new PlistDocument(); plist.ReadFromFile(managementPath);
                if (plist.root.values.TryGetValue("SchemeUserState", out var state))
                {
                    var entries = state.AsDict().values;
                    foreach (string key in entries.Keys.Where(k => k == from + ".xcscheme" || k == from + ".xcscheme_^#shared#^_").ToArray())
                    { var value = entries[key]; entries.Remove(key); entries[to + key.Substring(from.Length)] = value; }
                    plist.WriteToFile(managementPath);
                }
            }
            Directory.Move(source, destination);
        }
    }
}
#endif
