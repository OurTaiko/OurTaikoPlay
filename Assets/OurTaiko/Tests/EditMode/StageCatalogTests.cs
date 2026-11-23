using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // Which backdrop a play gets (the chart's #SCENEPRESET, or a draw from the pool), that the
    // game's own catalog lists only what is there, and the drumroll effect.
    public sealed class StageCatalogTests
    {
        static StageSet[] Sets(params string[] names) => names.Select(n => new StageSet { name = n.TrimEnd('*'), pooled = !n.EndsWith("*") }).ToArray();

        // Names ending in * are a preset's only, out of the pool.
        static StageCatalog Made()
        {
            var catalog = ScriptableObject.CreateInstance<StageCatalog>();
            catalog.headers = Sets("h0", "h1", "h2", "miku*", "dojo*");
            catalog.scenes = Sets("s0", "s1", "miku*");
            catalog.clears = Sets("c0", "c1");
            catalog.troupes = new[] { new StageTroupe { name = "t0" }, new StageTroupe { name = "t1" }, new StageTroupe { name = "miku", pooled = false, leadFirst = true } };
            catalog.footers = Sets("f0", "miku*");
            catalog.crowds = Sets("w0", "w1");
            catalog.rendas = Sets("r0", "r1", "r2");
            catalog.packs = new[]
            {
                // Its own header, scene and troupe, no stand, and any crowd and drumroll effect.
                new StagePack { preset = "MIKU", header = "miku", scene = "miku", clear = "c1", troupe = "miku", footer = "-" },
                new StagePack { preset = StageCatalog.DanPreset, header = "dojo", scene = "s0", clear = "-", troupe = "-", footer = "-", crowd = "-", renda = "-" },
            };
            return catalog;
        }

        static string[] Parts(StageChoice c) => new[] { c.Header, c.Scene, c.Clear, c.Troupe, c.Footer, c.Crowd, c.Renda };

        [Test]
        public void AChartThatNamesNoBackdropGetsEachPartDrawnFromThePool()
        {
            var catalog = Made();
            try
            {
                var seen = Enumerable.Range(0, 7).Select(_ => new HashSet<string>()).ToArray();
                var random = new System.Random(7);
                for (int i = 0; i < 400; i++)
                {
                    // A preset no pack has is the same as none.
                    var choice = catalog.Choose(i % 2 == 0 ? null : "NOSUCH", StageMode.Single, random);
                    Assert.That(choice.Preset, Is.Null);
                    var parts = Parts(choice);
                    for (int k = 0; k < parts.Length; k++) seen[k].Add(parts[k]);
                }
                Assert.That(seen.Select(s => string.Join(" ", s.OrderBy(n => n))), Is.EqualTo(new[] { "h0 h1 h2", "s0 s1", "c0 c1", "t0 t1", "f0", "w0 w1", "r0 r1 r2" }),
                    "Every pooled set comes up, on its own; a preset's own sets never do.");
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void AScenePresetGetsItsPack()
        {
            var catalog = Made();
            try
            {
                var seen = new HashSet<string>();
                var random = new System.Random(3);
                for (int i = 0; i < 100; i++)
                {
                    // Written in any case, with spaces around it.
                    var choice = catalog.Choose(i % 2 == 0 ? "MIKU" : " miku ", StageMode.Single, random);
                    Assert.That((choice.Preset, choice.Header, choice.Scene, choice.Clear, choice.Troupe, choice.Footer), Is.EqualTo(("MIKU", "miku", "miku", "c1", "miku", (string)null)));
                    seen.Add(choice.Crowd + " " + choice.Renda);
                }
                Assert.That(seen.Count, Is.EqualTo(6), "The parts the pack leaves open are drawn like any chart's.");
                Assert.That(catalog.Troupe("miku").leadFirst, Is.True);
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        // A song is asked a backdrop by its chart and by the folders it is in: the smallest scope
        // the catalog has a pack for decides, and a name it has none for is passed over.
        [Test]
        public void TheNearestPresetTheCatalogHasDecides()
        {
            var catalog = Made();
            try
            {
                catalog.packs = catalog.packs.Append(new StagePack { preset = "SOLO", header = "h2", singleOnly = true }).ToArray();
                string Chosen(StageMode mode, params string[] asked) => catalog.Choose(asked, mode, new System.Random(1)).Preset;
                Assert.That(Chosen(StageMode.Single, "MIKU", "SOLO", "DAN"), Is.EqualTo("MIKU"), "The chart's own, whatever its folders name.");
                Assert.That(Chosen(StageMode.Single, "SOLO", "MIKU"), Is.EqualTo("SOLO"));
                Assert.That(Chosen(StageMode.Single, "nothing", "", null, " solo ", "MIKU"), Is.EqualTo("SOLO"), "Names the catalog does not have are passed over.");
                Assert.That(Chosen(StageMode.Single, "nothing", "nor this"), Is.Null);
                Assert.That(Chosen(StageMode.Single), Is.Null);
                Assert.That(catalog.Choose((string[])null, StageMode.Single, new System.Random(1)).Preset, Is.Null);
                Assert.That(Chosen(StageMode.Double, "SOLO", "MIKU"), Is.EqualTo("MIKU"), "A pack for solo play is not there for two players.");
                Assert.That(Chosen(StageMode.Double, "SOLO"), Is.Null);
                Assert.That(Chosen(StageMode.Dan, "MIKU"), Is.EqualTo("DAN"), "The dojo's is its own whatever is asked.");
                Assert.That(Chosen(StageMode.Practice, "nothing", "MIKU"), Is.EqualTo("MIKU"));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void TwoPlayersGetOnlyTheHeaderAndTheDrumrollEffect()
        {
            var catalog = Made();
            try
            {
                var choice = catalog.Choose((string)null, StageMode.Double, new System.Random(1));
                Assert.That(Parts(choice).Select(p => p != null), Is.EqualTo(new[] { true, false, false, false, false, false, true }));
                Assert.That(catalog.Choose("MIKU", StageMode.Double, new System.Random(1)).Header, Is.EqualTo("miku"));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void TheDojoAndPracticeAskForTheirOwnSets()
        {
            var catalog = Made();
            try
            {
                var dan = catalog.Choose("MIKU", StageMode.Dan, new System.Random(1));
                // "-" stays for the clear: it also takes away a scene's own.
                Assert.That(Parts(dan), Is.EqualTo(new[] { "dojo", "s0", "-", null, null, null, null }));
                // Practice has no set of its own here, so it plays the chart's.
                Assert.That(catalog.Choose("MIKU", StageMode.Practice, new System.Random(1)).Preset, Is.EqualTo("MIKU"));
                catalog.packs = catalog.packs.Append(new StagePack { preset = StageCatalog.PracticePreset, header = "h2", troupe = "-" }).ToArray();
                var practice = catalog.Choose("MIKU", StageMode.Practice, new System.Random(1));
                Assert.That((practice.Preset, practice.Header, practice.Troupe), Is.EqualTo(("PRACTICE", "h2", (string)null)));
            }
            finally { Object.DestroyImmediate(catalog); }
        }

        const string Folder = "Assets/OurTaiko/Resources/Stage/";

        static Otanim File(string folder, string name)
        {
            // In the project: the game's own under Resources/Stage, the published ones' sources beside them.
            string carried = $"{Folder}{folder}/{name}.otanim", path = System.IO.File.Exists(carried) ? carried : $"{Sources}{folder}/{name}.otanim";
            var asset = AssetDatabase.LoadAssetAtPath<OtanimAsset>(path);
            Assert.That(asset, Is.Not.Null, $"{folder}/{name}.otanim is in the catalog but not in the project.");
            if (path == carried) Assert.That(Resources.Load<OtanimAsset>(StageCatalog.Path(folder, name)), Is.SameAs(asset));
            return asset.File;
        }

        const string Sources = "Assets/OurTaiko/StageSource/", PublishedCatalog = "Tools/stage-publish/catalog.json";

        static bool HasLayer(Otanim file) => file.Clips.Any(c => c.Name == StageBandView.LayerClip || c.Name.StartsWith(StageBandView.LayerClip + "_"));

        // Whoever adds a set runs this: every set the catalog lists is there, with the clips its part
        // plays, and every pack names sets the catalog has.
        // published: the catalog of every set there is (Tools/stage-publish), checked against the
        // sources where the project has them; else the one the game carries.
        [TestCase(false)]
        [TestCase(true)]
        public void ACatalogListsOnlyWhatIsThere(bool published)
        {
            if (published && !System.IO.Directory.Exists(Sources)) Assert.Ignore("The published sets' sources are not in this project.");
            var catalog = StageCatalogFile.Read(published ? System.IO.File.ReadAllText(PublishedCatalog) : Resources.Load<TextAsset>(StageLibrary.BuiltInCatalog).text, warning => Assert.Fail(warning));
            foreach (var set in catalog.headers) Assert.That(HasLayer(File(StageCatalog.HeaderFolder, set.name)), Is.True, $"header {set.name} has no clip named normal");
            foreach (var set in catalog.scenes) Assert.That(HasLayer(File(StageCatalog.SceneFolder, set.name)), Is.True, $"scene {set.name} has no clip named normal");
            foreach (var set in catalog.clears) Assert.That(File(StageCatalog.ClearFolder, set.name).Find(StageBandView.ClearClip), Is.Not.Null, $"clear {set.name} has no clip named clear");
            foreach (var set in catalog.footers) Assert.That(HasLayer(File(StageCatalog.FooterFolder, set.name)), Is.True, $"footer {set.name} has no clip named normal");
            foreach (var set in catalog.crowds) Assert.That(File(StageCatalog.CrowdFolder, set.name).Find(StageView.CrowdClip), Is.Not.Null, $"crowd {set.name} has no clip named full");
            foreach (var set in catalog.rendas) Assert.That(File(StageCatalog.RendaFolder, set.name).Clips, Is.Not.Empty, $"renda {set.name} has no clips");
            foreach (var troupe in catalog.troupes)
            {
                var files = troupe.movie ? new[] { troupe.name } : Enumerable.Range(0, troupe.members).Select(i => troupe.name + "_" + i).ToArray();
                for (int slot = 0; slot < files.Length; slot++)
                {
                    string file = files[slot];
                    var rig = File(StageCatalog.DancerFolder, file).Find(DancerView.RigClip);
                    Assert.That(rig, Is.Not.Null, $"dancer {file} has no clip named rig");
                    if (!troupe.movie)
                    {
                        Assert.That(troupe.dance, Is.GreaterThanOrEqualTo(0));
                        Assert.That(troupe.LeaveAt(slot), Is.InRange(troupe.dance + 1, rig.Frames.Length), file);
                    }
                }
            }
            void Named(string part, string asked, IEnumerable<string> names, string preset)
            {
                if (!string.IsNullOrEmpty(asked) && asked != StagePack.None) Assert.That(names, Has.Member(asked), $"pack {preset}: no {part} named {asked}");
            }
            Assert.That(catalog.packs.Select(p => p.preset.Trim().ToUpperInvariant()), Is.Unique);
            foreach (var pack in catalog.packs)
            {
                Named("header", pack.header, catalog.headers.Select(s => s.name), pack.preset);
                Named("scene", pack.scene, catalog.scenes.Select(s => s.name), pack.preset);
                Named("clear", pack.clear, catalog.clears.Select(s => s.name), pack.preset);
                Named("troupe", pack.troupe, catalog.troupes.Select(s => s.name), pack.preset);
                Named("footer", pack.footer, catalog.footers.Select(s => s.name), pack.preset);
                Named("crowd", pack.crowd, catalog.crowds.Select(s => s.name), pack.preset);
                Named("renda", pack.renda, catalog.rendas.Select(s => s.name), pack.preset);
            }
            if (!published)
            {
                // The game carries one set of everything a play draws, and the modes' own; all of it is published too.
                foreach (var (folder, set) in catalog.Sets()) Assert.That(System.IO.File.Exists($"{Folder}{folder}/{catalog.MainFile(folder, set)}.otanim"), Is.True, folder + "/" + set);
                Assert.That(catalog.packs.Select(p => p.preset), Is.EquivalentTo(new[] { StageCatalog.DanPreset, StageCatalog.PracticePreset, StageCatalog.TutorialPreset }));
                Assert.That(Parts(catalog.Choose((string)null, StageMode.Single, new System.Random(1))), Has.None.Null, "A play that names no backdrop gets every part.");
                var all = StageCatalogFile.Read(System.IO.File.ReadAllText(PublishedCatalog));
                Assert.That(all.Sets().Select(s => s.folder + "/" + s.set), Is.SupersetOf(catalog.Sets().Select(s => s.folder + "/" + s.set)));
                Object.DestroyImmediate(all);
                Object.DestroyImmediate(catalog);
                return;
            }
            Assert.That(catalog.headers.Count(s => s.pooled), Is.EqualTo(6));
            Assert.That(catalog.scenes.Count(s => s.pooled), Is.EqualTo(5));
            Assert.That(catalog.clears.Count(s => s.pooled), Is.EqualTo(4));
            Assert.That(catalog.troupes.Count(t => t.pooled), Is.EqualTo(6));
            Assert.That(catalog.troupes.Count(t => t.movie), Is.EqualTo(5));
            Assert.That(catalog.crowds.Where(s => s.pooled).Select(s => s.name), Is.EquivalentTo(new[] { "common_1", "common_2" }));
            foreach (var name in new[] { "ANIMAL", "BUTTOBURST", "FUNASSYI", "OSHIRI" })
            {
                var characters = File("chibi", catalog.Pack(name).chibi);
                Assert.That(characters.Clips.Any(c => c.Name.StartsWith("good_")), Is.True);
                Assert.That(characters.Clips.Any(c => c.Name.StartsWith("bad_")), Is.True);
                Assert.That(catalog.Choose(name, StageMode.Single, new System.Random(1)).Preset, Is.EqualTo(name));
                Assert.That(catalog.Choose(name, StageMode.Double, new System.Random(1)).Preset, Is.Null);
            }
            Assert.That(catalog.Choose("MIKU", StageMode.Tutorial, new System.Random(1)).Preset, Is.EqualTo("TUTORIAL"));
            Assert.That(catalog.footers.Count(s => s.pooled), Is.EqualTo(3));
            Assert.That(catalog.rendas.Count(s => s.pooled), Is.EqualTo(3));
            Assert.That(catalog.Pack("MIKU").troupe, Is.EqualTo("miku"));
            Assert.That(catalog.Troupe(catalog.Pack("YOKAI_MATSURI").troupe).LeaveAt(3), Is.EqualTo(400));
        }

        [Test]
        public void EveryDrumrollHitPlaysOneClipOnce()
        {
            var root = new GameObject("Renda", typeof(RectTransform), typeof(StageRendaView));
            try
            {
                var view = root.GetComponent<StageRendaView>();
                view.most = 3;
                view.Hit(0, 0);
                Assert.That(view.Out, Is.Zero, "No set, nothing thrown.");
                // Any file will do for the mechanics: the dojo's header, whose first clip is 1280 frames at 60.
                var file = AssetDatabase.LoadAssetAtPath<OtanimAsset>(Folder + "header/dojo.otanim");
                double length = file.File.Clips[0].Frames.Length / (double)file.File.Clips[0].Fps;
                view.Use(file);
                view.Hit(10, 0);
                view.Hit(11, 4);
                Assert.That(view.Out, Is.EqualTo(2));
                Assert.That(root.GetComponentsInChildren<OtanimView>().Where(v => v.RunCount > 0).Count(), Is.EqualTo(2));
                view.Show(10 + length + 0.01);
                Assert.That(view.Out, Is.EqualTo(1), "The first has played through and is gone.");
                view.Hit(12, 1); view.Hit(12, 1); view.Hit(12, 1); view.Hit(12, 1);
                Assert.That(view.Out, Is.EqualTo(3), "No more than `most` at a time.");
                view.Use(null);
                Assert.That(view.Out, Is.Zero);
                Assert.That(root.GetComponentsInChildren<UnityEngine.UI.MaskableGraphic>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
