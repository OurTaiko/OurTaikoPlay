using NUnit.Framework;
using OurTaiko.Chara;

namespace OurTaiko.Tests
{
    // The play backdrops in tests. The game carries a few sets; the rest are published and
    // fetched as a play needs them, so a test of one of those runs only on a machine that has it
    // (fetched by playing, or built in with OurTaiko/Build/Install Stage Bundles Here) and is
    // ignored elsewhere.
    public static class TestStage
    {
        // The preset's sets are all here to draw, or the test is ignored.
        public static void Require(string preset)
        {
            var catalog = StageLibrary.Catalog;
            var pack = catalog.Pack(preset);
            if (pack == null) Assert.Ignore($"The backdrop {preset} is not in this machine's catalog (the published ones are fetched as they are played).");
            foreach (var (part, name) in catalog.Files(catalog.Choose(preset, StageMode.Single, new System.Random(0)), 1))
                if (name != null && StageLibrary.Load(part, name, quiet: true) == null && name == catalog.MainFile(part, SetNamed(part, name)))
                    Assert.Ignore($"The backdrop file {part}/{name} is not on this machine (the published ones are fetched as they are played).");
        }

        // A troupe's first dancer stands for its set.
        static string SetNamed(string part, string file) =>
            part == StageCatalog.DancerFolder && file.EndsWith("_0", System.StringComparison.Ordinal) ? file.Substring(0, file.Length - 2) : file;

        // The file's asset ("header/toho"), or the test is ignored on a machine that has not got it.
        public static OtanimAsset Asset(string file)
        {
            int slash = file.IndexOf('/');
            var asset = StageLibrary.Load(file.Substring(0, slash), file.Substring(slash + 1), quiet: true);
            if (asset == null) Assert.Ignore($"The backdrop file {file} is not on this machine (the published ones are fetched as they are played).");
            return asset;
        }
    }
}
