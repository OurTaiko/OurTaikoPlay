using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OurTaiko.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // A script reload in the Editor empties StageLibrary's statics. The fetched backdrops it had
    // opened must not outlive them: an asset bundle still loaded keeps the same file from being
    // opened again, and every published backdrop would be drawn empty until the Editor restarts.
    public sealed class StageLibraryReloadTests
    {
        // The test runner resumes the test after the reload where it was, but not its locals: what
        // it needs on both sides is fixed here or kept in SessionState.
        static readonly string Root = Path.Combine(Path.GetTempPath(), "OurTaikoStageReload");
        static readonly string Folder = Path.Combine(Root, "Stage"), Fetched = Path.Combine(Root, "Resources", "Stage");
        const string LoadedBefore = "OurTaiko.Tests.StageLibraryReloadTests.LoadedBefore";

        [UnityTest]
        public IEnumerator AFetchedBackdropOpensAgainAfterTheScriptsReload()
        {
            try
            {
                if (Directory.Exists(Root)) Directory.Delete(Root, true);
                SessionState.SetString(LoadedBefore, string.Join("\n", AssetBundle.GetAllLoadedAssetBundles().Select(b => b.name)));
                string built = Path.Combine(Root, "built");
                var files = StageBundles.Files().Where(f => f.part == StageCatalog.HeaderFolder && f.name == "practice").ToArray();
                var index = StageBundles.Build(built, StageBundles.TargetNamed(StageLibrary.Platform).Value, files);
                string bundle = Path.Combine(built, "stage", "bundle", StageBundles.Version, StageLibrary.Platform, index.files.Single().file);
                StageLibrary.UseFolder(Folder, Fetched);
                string here = StageLibrary.DownloadedFile(StageCatalog.HeaderFolder, "reloaded");
                Directory.CreateDirectory(Path.GetDirectoryName(here));
                File.Copy(bundle, here);
                Assert.That(StageLibrary.Load(StageCatalog.HeaderFolder, "reloaded"), Is.Not.Null, "Opened before the reload.");

                EditorUtility.RequestScriptReload();
                yield return new WaitForDomainReload();

                // New statics: the same folders again, as the game's would be.
                StageLibrary.UseFolder(Folder, Fetched);
                Assert.That(StageLibrary.Load(StageCatalog.HeaderFolder, "reloaded", quiet: true), Is.Not.Null, "Opened again after the reload.");
            }
            finally
            {
                StageLibrary.Forget();
                // Whatever this left loaded, whether or not the library still knew of it.
                var had = SessionState.GetString(LoadedBefore, "").Split('\n');
                foreach (var left in AssetBundle.GetAllLoadedAssetBundles().Where(b => !had.Contains(b.name)).ToArray()) left.Unload(true);
                SessionState.EraseString(LoadedBefore);
                try { Directory.Delete(Root, true); } catch (IOException) { }
            }
        }
    }
}
