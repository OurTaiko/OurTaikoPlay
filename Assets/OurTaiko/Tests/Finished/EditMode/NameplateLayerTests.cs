using System.Collections.Generic;
using System.Linq;
using System.IO;
using NUnit.Framework;
using OurTaiko.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The layers a nameplate wears (band, effect, icon .otanim files) and the Editor's preview of it.
    public sealed class NameplateLayerTests
    {
        static readonly Rect PlateView = new Rect(0, 0, NameplateLayout.Width, NameplateLayout.Height);
        // The band of the plain title every plate without a choice of its own wears.
        const string WoodBand = "band/lumen-title-wood";
        string folder;

        [SetUp]
        public void UseOwnFolder()
        {
            folder = Path.Combine(Path.GetTempPath(), "OurTaikoNameplateTests", System.Guid.NewGuid().ToString("N"));
            NameplateItems.UseFolder(folder);
        }

        [TearDown]
        public void Clean()
        {
            NameplateItems.UseFolder(null);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }

        // The fixture's red and blue square, 100 units on a side from the plate's bottom, its left
        // edge at `lefts`, a frame a second.
        void Item(NameplateSlot slot, string name, params float[] lefts) => Item(slot, name, PlateView, lefts);

        void Item(NameplateSlot slot, string name, Rect view, params float[] lefts)
        {
            string path = NameplateItems.PathOf(slot, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, OtanimFixture.Build(view, OtanimFixture.Make(NameplateItems.ClipName, true, 1, lefts)));
        }

        [TestCase("band/sakura", true, NameplateSlot.Band, "sakura")]
        [TestCase("effect/shine-2", true, NameplateSlot.Effect, "shine-2")]
        [TestCase("icon/星", true, NameplateSlot.Icon, "星")]
        [TestCase("", false, NameplateSlot.Band, null)]
        [TestCase("band/", false, NameplateSlot.Band, null)]
        [TestCase("band/..", false, NameplateSlot.Band, null)]
        [TestCase("band/a/b", false, NameplateSlot.Band, null)]
        [TestCase("band/a\\b", false, NameplateSlot.Band, null)]
        [TestCase("hat/sakura", false, NameplateSlot.Band, null)]
        [TestCase("/sakura", false, NameplateSlot.Band, null)]
        public void AnItemsIdNamesAFileOfItsSlotsFolderAndNothingElse(string id, bool valid, NameplateSlot slot, string name)
        {
            Assert.That(NameplateSlots.TryParse(id, out var s, out string n), Is.EqualTo(valid));
            if (valid) Assert.That((s, n), Is.EqualTo((slot, name)));
        }

        [Test]
        public void PlayerJsonKeepsTheWornLayersAndOldFilesWearNone()
        {
            var info = new PlayerInfo { name = "a" };
            info.Wear(NameplateSlot.Band, "band/x");
            info.Wear(NameplateSlot.Effect, "effect/y");
            info.Wear(NameplateSlot.Icon, "icon/z");
            var back = PlayerInfo.FromJson(info.ToJson());
            Assert.That((back.ItemIn(NameplateSlot.Band), back.ItemIn(NameplateSlot.Effect), back.ItemIn(NameplateSlot.Icon)), Is.EqualTo(("band/x", "effect/y", "icon/z")));
            var old = PlayerInfo.FromJson("{\"name\":\"b\",\"title\":\"t\",\"titleBackground\":2}");
            Assert.That((old.bandItem, old.effectItem, old.iconItem), Is.EqualTo(("", "", "")));
            var nulls = PlayerInfo.FromJson("{\"bandItem\":null}");
            Assert.That(nulls.bandItem, Is.EqualTo(""));
        }

        [Test]
        public void AnItemIsReadFromItsSlotsFolderOnlyWhenDrawnOnThePlate()
        {
            TestData.RequireBakedNameplates();
            Item(NameplateSlot.Band, "b", 0);
            Item(NameplateSlot.Band, "a", 0);
            Item(NameplateSlot.Effect, "wide", new Rect(0, 0, 500, 96), 0);
            File.WriteAllText(Path.Combine(folder, "band", "notes.txt"), "not an item");
            Directory.CreateDirectory(Path.Combine(folder, "icon"));
            File.WriteAllBytes(NameplateItems.PathOf(NameplateSlot.Icon, "broken"), new byte[] { 1, 2, 3 });

            Assert.That(NameplateItems.Names(NameplateSlot.Band), Does.Contain("a").And.Contain("b"));
            Assert.That(NameplateItems.Names(NameplateSlot.Effect), Does.Contain("wide").And.Contain("lumen-title-gold"), "The files of the folder come with the built-in ones.");

            var band = NameplateItems.Load(NameplateSlot.Band, "band/a");
            Assert.That(band, Is.Not.Null);
            Assert.That(NameplateItems.ClipOf(band.File).Name, Is.EqualTo(NameplateItems.ClipName));
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/a"), Is.SameAs(band), "Read once while the file stays the same.");
            Assert.That(NameplateItems.Load(NameplateSlot.Effect, "band/a"), Is.Null, "Worn only in its own slot.");
            Assert.That(NameplateItems.Load(NameplateSlot.Band, "band/missing"), Is.Null);
            Assert.That(NameplateItems.Load(NameplateSlot.Effect, "effect/wide"), Is.Null, "Drawn for another box than the plate.");
            Assert.That(NameplateItems.Load(NameplateSlot.Icon, "icon/broken"), Is.Null);

            // A file saved again is read again.
            string path = NameplateItems.PathOf(NameplateSlot.Band, "a");
            Item(NameplateSlot.Band, "a", 50);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
            var again = NameplateItems.Load(NameplateSlot.Band, "band/a");
            Assert.That(again, Is.Not.Null.And.Not.SameAs(band));
            Assert.That(band != null, "What the plates on screen still draw stays until the items are forgotten.");
        }

        // A plate's pixel at (x, y) plate pixels from its top left corner.
        static Color Pixel(NameplatePreview preview, int x, int y)
        {
            var picture = preview.Capture(1);
            try { return picture.GetPixel(NameplatePreview.Margin + x, picture.height - 1 - (NameplatePreview.Margin + y)); }
            finally { Object.DestroyImmediate(picture); }
        }

        // Where `part` is drawn among the plate's pictures and text: the place of the plate's child
        // that holds it (a layer cut to the band sits in its mask).
        static int Order(NameplatePreview preview, Component part)
        {
            var root = preview.Plate.transform.Find(PlateComposition.RootName);
            var t = part.transform;
            while (t.parent != root) t = t.parent;
            return t.GetSiblingIndex();
        }

        static bool IsRed(Color c) => c.r > 0.8f && c.g < 0.2f && c.b < 0.2f;
        static bool IsBlue(Color c) => c.b > 0.8f && c.r < 0.2f && c.g < 0.2f;

        // A plate with a 段位 and no title or name: its band shows, and nothing is written over it.
        static PlayerInfo Plain() => new PlayerInfo { plateState = "custom", name = "", title = "", dan = 10 };

        [Test]
        public void AWornBandTakesTheBandsPlaceAndItAndItsEffectStayInsideTheBand()
        {
            TestData.RequireBakedNameplates();
            Item(NameplateSlot.Band, "moving", 250, 200);
            Item(NameplateSlot.Effect, "still", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var plate = preview.Plate;
            var info = Plain();
            info.bandItem = "band/moving";
            preview.Show(info, 0);

            var layers = plate.Layers;
            Assert.That(layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, "band/moving")), "The worn band is drawn instead of the title's.");
            Assert.That(layers.BandView.gameObject.activeSelf && !layers.EffectView.gameObject.activeSelf && !layers.IconView.gameObject.activeSelf);

            // The square's left half is red, its right half blue; the band is the plate's top 56.
            Assert.That(IsRed(Pixel(preview, 275, 20)) && IsBlue(Pixel(preview, 325, 20)), "The band item is drawn where the band is.");
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False, "Below the band it is cut away.");
            preview.Seek(1);
            Assert.That(IsRed(Pixel(preview, 225, 20)) && !IsRed(Pixel(preview, 275, 20)), "It moves on with the time.");

            info.bandItem = "";
            info.effectItem = "effect/still";
            preview.Show(info, 0);
            Assert.That(plate.Layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, WoodBand)), "Without a band item the title's own band is back, the effect over it.");
            Assert.That(IsRed(Pixel(preview, 275, 20)), "The effect is drawn over the band.");
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False, "And cut to it.");
        }

        [Test]
        public void ATriggeredClipStartsAtItsOwnFirstFrameAndHoldsItsEnd()
        {
            TestData.RequireBakedNameplates();
            string path = NameplateItems.PathOf(NameplateSlot.Icon, "transition");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, OtanimFixture.Build(PlateView,
                OtanimFixture.Make("idle", true, 1, 0),
                OtanimFixture.Make("start", false, 1, 120, 260)));
            using var preview = new NameplatePreview { Background = Color.black };
            var info = Plain();
            info.iconItem = "icon/transition";
            preview.Show(info, 0);
            preview.Seek(7);
            var layers = preview.Plate.Layers;
            Assert.That(layers.Play(NameplateSlot.Icon, "start", 7, foreground: true));
            Assert.That(IsRed(Pixel(preview, 145, 60)));
            Assert.That(layers.Play(NameplateSlot.Icon, "missing", 7), Is.False);
            preview.Seek(12);
            Assert.That(IsRed(Pixel(preview, 285, 80)));
        }

        [Test]
        public void CatalogBandShowsItsBackingAndOverflowWhileLooseLayersStayMasked()
        {
            TestData.RequireBakedNameplates();
            Item(NameplateSlot.Band, "loose", 250);
            Item(NameplateSlot.Effect, "overlay", 250);
            using var preview = new NameplatePreview();
            var info = Plain();
            info.bandItem = "band/lumen-title-aprilfool2025";
            preview.Show(info, 0);
            var layers = preview.Plate.Layers;
            Assert.That(layers.BandView.transform.parent, Is.EqualTo(preview.Plate.transform.Find("Complete nameplate")));
            Assert.That(Pixel(preview, 300, 45).maxColorComponent, Is.LessThan(0.15f));
            info.bandItem = "band/loose";
            info.effectItem = "effect/overlay";
            preview.Show(info, 0);
            Assert.That(layers.BandView.transform.parent, Is.EqualTo(layers.BandMask));
            Assert.That(layers.EffectView.transform.parent, Is.EqualTo(layers.EffectMask));
            Assert.That(Order(preview, layers.BandView), Is.LessThan(Order(preview, preview.Plate.title)));
            Assert.That(Order(preview, layers.EffectView), Is.GreaterThan(Order(preview, preview.Plate.title)), "The effect sparkles over the title's text.");
        }

        [TestCase("Title", 10, 0)]
        [TestCase("Title", -1, 1)]
        [TestCase("", 10, 1)]
        [TestCase("", -1, 0)]
        public void AutoUsesStandardTextPositions(string title, int dan, int side)
        {
            using var preview = new NameplatePreview();
            var info = new PlayerInfo { name = "Player", title = title, dan = dan };
            preview.Show(info, side);
            var box = NameplateLayout.NameBox(info);
            var nameCenter = new Vector2(box.X, -box.Y);
            var titleCenter = new Vector2(NameplateLayout.TitleX, -NameplateLayout.TitleY);

            info.plateState = "auto";
            preview.Show(info, side);
            var labels = preview.Plate.transform.Find("Complete nameplate").GetComponentsInChildren<TMPro.TMP_Text>();
            Assert.That(System.Array.Find(labels, text => text.text == "Player").rectTransform.anchoredPosition, Is.EqualTo(nameCenter));
            if (info.HasTitle)
                Assert.That(System.Array.Find(labels, text => text.text == title).rectTransform.anchoredPosition, Is.EqualTo(titleCenter));
        }

        // Before a set is here (a first launch that could not download it) a plate is its name and
        // title where the layouts put them; with one, the same plate is drawn whole.
        [Test]
        public void WithoutASetAPlateIsItsTextAlone()
        {
            var canvas = new GameObject("Canvas", typeof(Canvas));
            try
            {
                NameplateItems.Use(null);
                var plate = Object.Instantiate(Resources.Load<NameplateView>(NameplateView.Resource), canvas.transform);
                var info = new PlayerInfo { name = "Player", title = "Taiko Master", plateTitle = "gold", dan = 10 };
                plate.Pin(info, 0);
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Where(v => v.RunCount > 0), Is.Empty, "No pictures without a set.");
                Assert.That(plate.playerName.text, Is.EqualTo("Player"));
                Assert.That(plate.playerName.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(261, -64.5f)), "The name box of a plate with a 段位.");
                Assert.That(plate.title.text, Is.EqualTo("Taiko Master"));
                Assert.That(plate.title.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(226.5f, -32)));
                Assert.That(plate.title.color, Is.EqualTo(Color.white), "No band under it: white, as the name.");
            }
            finally
            {
                Object.DestroyImmediate(canvas);
                TestData.UseBakedNameplates();
            }
        }

        [Test]
        public void APlateOfTextAloneIsDrawnInFullWithASet()
        {
            TestData.RequireBakedNameplates();
            var canvas = new GameObject("Canvas", typeof(Canvas));
            try
            {
                NameplateItems.Use(null);
                var plate = Object.Instantiate(Resources.Load<NameplateView>(NameplateView.Resource), canvas.transform);
                var info = new PlayerInfo { name = "Player", title = "Taiko Master", plateTitle = "gold", dan = 10 };
                plate.Pin(info, 0);
                Assert.That(plate.title.color, Is.EqualTo(Color.white));

                TestData.UseBakedNameplates();
                plate.Pin(info, 0);
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Any(v => v.clip == "1kyuu_red"), Is.True);
                Assert.That(plate.Layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold")));
                Assert.That(plate.title.color, Is.Not.EqualTo(Color.white));
            }
            finally
            {
                Object.DestroyImmediate(canvas);
                TestData.UseBakedNameplates();
            }
        }

        [TestCase(6, 0, false)]
        [TestCase(10, 1, false)]
        [TestCase(24, 2, true)]
        public void StandardAndAutoUseTheSameRankArtAtTheSameScale(int rank, int combo, bool gold)
        {
            TestData.RequireBakedNameplates();
            using var preview = new NameplatePreview();
            var info = new PlayerInfo { dan = rank, danCombo = combo, gold = gold };
            preview.Show(info, 0);
            var plate = preview.Plate;
            var expectedClip = PlateComposition.RankLabel(rank) + (gold ? "_gold" : "_red");
            var expected = NameplateItems.CompositionPart("dan-" + new[] { "clear", "full", "donfull" }[combo]);
            foreach (string state in new[] { "auto", "custom" })
            {
                info.plateState = state;
                preview.Show(info, 0);
                var views = plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>();
                var match = System.Array.Find(views, v => v.asset == expected && v.clip == expectedClip);
                Assert.That(match, Is.Not.Null);
                Assert.That(((RectTransform)match.transform).anchoredPosition, Is.EqualTo(Vector2.zero));
            }
        }

        [Test]
        public void FullCollectionSupportsEveryLayoutAndReturnsToStandard()
        {
            TestData.RequireBakedNameplates();
            var catalog = PlateComposition.ReadCatalog();
            Assert.That(catalog.States.Count, Is.EqualTo(36));
            Assert.That(catalog.Titles.Count, Is.GreaterThanOrEqualTo(88), "The arcade's 88, and any added since.");
            Assert.That(catalog.AiTitles.Count, Is.EqualTo(8));
            using var preview = new NameplatePreview();
            var info = new PlayerInfo { name = "Player", title = "Title", dan = 24,
                plateTitle = "pjsekai", plateAiTitle = "ai_3", plateBlank = "off" };
            foreach (var state in catalog.States.Properties())
            {
                info.plateState = state.Name;
                preview.Show(info, 0);
                var group = preview.Plate.transform.Find("Complete nameplate");
                Assert.That(group.gameObject.activeSelf, state.Name);
                Assert.That(group.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Length, Is.GreaterThan(0), state.Name);
                foreach (var view in group.GetComponentsInChildren<OurTaiko.Chara.OtanimView>())
                    Assert.That(view.Clip, Is.Not.Null, state.Name);
                var labels = group.GetComponentsInChildren<TMPro.TMP_Text>();
                foreach (var row in state.Value)
                {
                    string role = (string)row["role"];
                    if (role != "text_title" && role != "text_name") continue;
                    var label = System.Array.Find(labels, t => t.text == (role == "text_title" ? info.title : info.name));
                    float y = role == "text_title" ? 32 : (float)row["size"] == 30 ? 46 : 64.5f;
                    Assert.That(label.rectTransform.anchoredPosition, Is.EqualTo(new Vector2((float)row["x"], -y)), state.Name);
                }
                preview.Seek(0.55);
            }
            info.plateState = "auto";
            preview.Show(info, 1);
            Assert.That(PlateComposition.StateFor(info, 1), Is.EqualTo("2p_shougou_dani"));
            Assert.That(preview.Plate.PlayLayerClipAt(NameplateSlot.Icon, "14dan/rankup_start", 0));
            Assert.That(preview.Plate.PlayLayerClipAt(NameplateSlot.Icon, "14dan/rankup_end", 1));
            info.plateState = "";
            preview.Show(info, 0);
            Assert.That(preview.Plate.transform.Find("Complete nameplate").gameObject.activeSelf, Is.True);
            Assert.That(preview.Plate.playerName.enabled);
        }

        [Test]
        public void AnIconIsDrawnOverTheOutlineAndOutOfTheBand()
        {
            TestData.RequireBakedNameplates();
            Item(NameplateSlot.Icon, "badge", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var info = Plain();
            info.iconItem = "icon/badge";
            preview.Show(info, 0);
            Assert.That(preview.Plate.Layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, WoodBand)), "An icon leaves the title's band alone.");
            Assert.That(preview.Plate.Layers.EffectView.gameObject.activeSelf, Is.False);
            Assert.That(IsRed(Pixel(preview, 275, 20)) && IsRed(Pixel(preview, 275, 80)), "Over the band and below it.");
        }

        // Some layer is on show (none are made before the plate first wears one).
        static bool Worn(NameplateLayers layers) =>
            layers?.BandMask != null && (layers.BandView.gameObject.activeInHierarchy || layers.EffectView.gameObject.activeInHierarchy || layers.IconView.gameObject.activeSelf);

        [Test]
        public void TheCoinPlateHidesCustomItems()
        {
            Item(NameplateSlot.Icon, "badge", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var coin = new PlayerInfo { plateState = "custom", name = "", title = "", iconItem = "icon/badge" };
            Assert.That(coin.IsCoin);
            preview.Show(coin, 0);
            Assert.That(Worn(preview.Plate.Layers), Is.False);
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False);

        }

        [Test]
        public void AllBuiltInItemsLoadWithAnEmptyUserFolder()
        {
            TestData.RequireBakedNameplates();
            var catalog = PlateComposition.ReadCatalog();
            foreach (var slot in NameplateSlots.All)
            {
                foreach (string id in catalog.Ids(slot))
                    Assert.That(NameplateItems.Load(slot, id)?.File.Find("idle"), Is.Not.Null, id);
                Assert.That(NameplateItems.Names(slot).Count, Is.EqualTo(catalog.Ids(slot).Count() + (slot == NameplateSlot.Icon ? 1 : 0)), slot.ToString());
            }
            Assert.That(NameplateItems.Load(NameplateSlot.Icon, "icon/lumen-rankup")?.File.Clips.Length, Is.EqualTo(58));
            Assert.That(Directory.Exists(folder), Is.False);
        }

        // The rank-up effect is the plate's own, played on a 段位 promotion: the game carries it and
        // needs no set for it. It is the one the set was baked with.
        [Test]
        public void TheRankUpEffectComesWithTheGame()
        {
            var set = NameplateItems.Set;
            NameplateItems.Use(null);
            try
            {
                var carried = NameplateItems.Load(NameplateSlot.Icon, PlateComposition.RankUpItem);
                Assert.That(carried, Is.Not.Null);
                Assert.That(NameplateItems.Fits(carried.File));
                Assert.That(carried.File.Clips.Length, Is.EqualTo(58), "An empty idle, and a start, a wait and an end for each of the 19 ranks.");
                Assert.That(carried.File.Find("idle")?.Frames.Length, Is.EqualTo(1));
                foreach (string phase in new[] { "start", "wait", "end" })
                {
                    Assert.That(carried.File.Find("5kyuu/rankup_" + phase), Is.Not.Null, phase);
                    Assert.That(carried.File.Find("14dan/rankup_" + phase), Is.Not.Null, phase);
                }
            }
            finally { NameplateItems.Use(set); }

            string baked = Path.Combine(NameplateSet.BakedFolder, PlateComposition.RankUpItem + NameplateItems.Extension);
            if (!File.Exists(baked)) Assert.Ignore("No baked set to compare it with.");
            string own = Path.Combine(Application.dataPath, "OurTaiko/Resources", NameplateItems.ResourceFolder + PlateComposition.RankUpItem + NameplateItems.Extension);
            Assert.That(File.ReadAllBytes(own), Is.EqualTo(File.ReadAllBytes(baked)), "Baked anew, the game's copy is to be replaced.");
        }

        [Test]
        public void APreviewLeavesHiddenLayersOutOfThePictureOnly()
        {
            TestData.RequireBakedNameplates();
            Item(NameplateSlot.Icon, "badge", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var info = Plain();
            info.iconItem = "icon/badge";
            preview.Show(info, 0);
            var icon = preview.Plate.Layers.IconView;
            Assert.That(IsRed(Pixel(preview, 275, 80)));
            preview.Hidden.Add(NameplateSlot.Icon);
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False, "The icon is left out of the picture.");
            Assert.That(icon.gameObject.activeSelf, "But the plate still wears it.");
            preview.Hidden.Clear();
            Assert.That(IsRed(Pixel(preview, 275, 80)));
        }

        [Test]
        public void APreviewCanShowATitlesLayersWithoutTheRestOfThePlate()
        {
            TestData.RequireBakedNameplates();
            using var preview = new NameplatePreview { Background = Color.black };
            preview.Show(new PlayerInfo { name = "Player", title = "Title", plateTitle = "toho_y22_1" }, 0);
            // (79, 72) is the 1P badge, (200, 25) the band.
            Assert.That(Pixel(preview, 79, 72).maxColorComponent, Is.GreaterThan(0.5f));
            preview.LayersOnly = true;
            Assert.That(Pixel(preview, 79, 72).maxColorComponent, Is.LessThan(0.1f), "The badge is left out.");
            Assert.That(Pixel(preview, 200, 25).maxColorComponent, Is.GreaterThan(0.3f), "The band stays.");
            Assert.That(preview.Plate.title.gameObject.activeSelf && preview.Plate.Layers.IconView.gameObject.activeSelf, "Nothing is taken off the plate itself.");
        }

        [Test]
        public void TitleSheetsShowEachTitleAsAPlayerWearsItWithItsNameInTheTitle()
        {
            TestData.RequireBakedNameplates();
            var catalog = PlateComposition.ReadCatalog();
            using var preview = new NameplatePreview();
            preview.Show(NameplatePreview.TitleInfo("crown_gold", false), 0);
            Assert.That(preview.Plate.title.text, Is.EqualTo("crown_gold"));
            Assert.That(Wearing(preview.Plate.Layers).icon, Is.SameAs(NameplateItems.Load(NameplateSlot.Icon, catalog.Title("crown_gold").Icon)));

            var ai = NameplatePreview.TitleInfo("ai_3", true);
            preview.Show(ai, 0);
            Assert.That(PlateComposition.StateFor(ai, 0), Does.StartWith("ai_data"));
            Assert.That(preview.Plate.title.text, Is.EqualTo("AI ai_3"));
            var (band, effect, _) = Wearing(preview.Plate.Layers);
            Assert.That(band, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, catalog.AiTitle("ai_3").Band)));
            Assert.That(effect, Is.SameAs(NameplateItems.Load(NameplateSlot.Effect, catalog.AiTitle("ai_3").Effect)));
        }

        [Test]
        public void ThePreviewSaysWhichTitlesAnItemComesFrom()
        {
            TestData.RequireBakedNameplates();
            var catalog = PlateComposition.ReadCatalog();
            Assert.That(NameplatePreview.UsedBy(catalog, NameplateSlot.Band, "band/lumen-title-toho_y22"),
                Is.EqualTo(new[] { "toho_y22_1", "toho_y22_2", "toho_y22_3", "toho_y22_4", "toho_y22_5" }));
            Assert.That(NameplatePreview.UsedBy(catalog, NameplateSlot.Band, "band/lumen-ai-title-wood"), Is.EqualTo(new[] { "AI wood" }));
            Assert.That(NameplatePreview.UsedBy(catalog, NameplateSlot.Band, "band/loose"), Is.Empty);
            Assert.That(NameplatePreview.InSet(catalog, NameplateSlot.Icon, PlateComposition.RankUpItem));
            Assert.That(NameplatePreview.InSet(catalog, NameplateSlot.Band, "band/loose"), Is.False);
            Assert.That((NameplatePreview.Series("toho_y22_3"), NameplatePreview.Series("crown_gold_ura"), NameplatePreview.Series("pjsekai")),
                Is.EqualTo(("toho", "crown", "pjsekai")));
        }

        [Test]
        public void ThePreviewDescribesWhatThePlateWears()
        {
            TestData.RequireBakedNameplates();
            string auto = NameplatePreviewWindow.Describe(new PlayerInfo { name = "Player", title = "Title", plateTitle = "gold" }, 0);
            Assert.That(auto, Does.Contain("Title: gold").And.Contain("effect: effect/lumen-title-gold").And.Contain("runs outside the band").And.Contain("icon: none"));

            Item(NameplateSlot.Effect, "still", 250);
            var custom = Plain();
            custom.effectItem = "effect/still";
            string own = NameplatePreviewWindow.Describe(custom, 0);
            Assert.That(own, Does.Contain("empty band slot wears the band of wood").And.Contain("band: " + WoodBand).And.Contain("from the items folder"));

            var catalog = PlateComposition.ReadCatalog();
            var rows = (Newtonsoft.Json.Linq.JArray)catalog.States[PlateComposition.StateFor(custom, 0)];
            Assert.That(PlateComposition.Worn(catalog, rows, custom), Is.EqualTo(new TitleParts(WoodBand, "effect/still", "")));
            var coin = new PlayerInfo { plateState = "custom", name = "", title = "", effectItem = "effect/still" };
            Assert.That(PlateComposition.Worn(catalog, (Newtonsoft.Json.Linq.JArray)catalog.States[PlateComposition.StateFor(coin, 0)], coin), Is.EqualTo(default(TitleParts)));
        }

        [Test]
        public void APlateFollowingTheSiteWearsItsTitlesOwnLayers()
        {
            TestData.RequireBakedNameplates();
            using var preview = new NameplatePreview();
            var info = new PlayerInfo { name = "Player", title = "Title", profileBand = "wood" };
            info.SetProfileLayers(new TitleParts("band/lumen-title-toho_y22", "effect/lumen-title-ai_3", "icon/lumen-title-crown_gold"));
            preview.Show(info, 0);
            var (band, effect, icon) = Wearing(preview.Plate.Layers);
            Assert.That((band, effect, icon), Is.EqualTo((
                NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-toho_y22"),
                NameplateItems.Load(NameplateSlot.Effect, "effect/lumen-title-ai_3"),
                NameplateItems.Load(NameplateSlot.Icon, "icon/lumen-title-crown_gold"))));
            var back = PlayerInfo.FromJson(info.ToJson());
            Assert.That(back.ProfileLayers, Is.EqualTo(info.ProfileLayers), "player.json keeps them.");

            // Whether the effect runs over the plate is the title's own choice, whatever the catalog's
            // titles do with that effect: ai_3's is cut to the band there, and gold's is not.
            var root = preview.Plate.transform.Find(PlateComposition.RootName);
            Assert.That(preview.Plate.Layers.EffectView.transform.parent, Is.EqualTo(preview.Plate.Layers.EffectMask));
            info.SetProfileLayers(new TitleParts("band/lumen-title-toho_y22", "effect/lumen-title-ai_3", null, effectOverflow: true));
            preview.Show(info, 0);
            Assert.That(preview.Plate.Layers.EffectView.transform.parent, Is.EqualTo(root));
            Assert.That(PlayerInfo.FromJson(info.ToJson()).ProfileLayers?.EffectOverflow, Is.True);
            info.SetProfileLayers(new TitleParts("band/lumen-title-gold", "effect/lumen-title-gold", null));
            preview.Show(info, 0);
            Assert.That(preview.Plate.Layers.EffectView.transform.parent, Is.EqualTo(preview.Plate.Layers.EffectMask));

            // A title picked on the plate itself is the catalog's.
            info.plateTitle = "gold";
            preview.Show(info, 0);
            Assert.That(Wearing(preview.Plate.Layers).band, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold")));
            // A site without layers: the title's style.
            info.plateTitle = "auto";
            info.SetProfileLayers(null);
            info.profileBand = "purple";
            preview.Show(info, 0);
            Assert.That(Wearing(preview.Plate.Layers).effect, Is.SameAs(NameplateItems.Load(NameplateSlot.Effect, "effect/lumen-title-purple")));
        }

        [Test]
        public void EveryTitleIsMadeOfBuiltInItemsAndNeverLosesItsBand()
        {
            TestData.RequireBakedNameplates();
            var catalog = PlateComposition.ReadCatalog();
            foreach (var titles in new[] { catalog.Titles, catalog.AiTitles })
                foreach (var pair in titles)
                {
                    string label = pair.Key;
                    var parts = pair.Value;
                    Assert.That(parts.Band, Is.Not.Null, label);
                    foreach (var slot in NameplateSlots.All)
                    {
                        string id = parts.In(slot);
                        if (id == null) continue;
                        Assert.That(catalog.Contains(slot, id), label + " " + id);
                        Assert.That(NameplateItems.Load(slot, id), Is.Not.Null, label + " " + id);
                    }
                }
            // Whether an effect runs over the plate is the title's to say.
            Assert.That(catalog.Title("gold").EffectOverflow, Is.True, "Gold's sparkles burst out of the band.");
            Assert.That(catalog.Title("ai_3").EffectOverflow, Is.False, "ai_3's sweep stays inside it.");
            Assert.That(catalog.Title("wood").EffectOverflow, Is.False, "Nothing overflows without an effect.");
            // Titles sharing a picture share its item.
            Assert.That(catalog.Title("toho_y22_3").Band, Is.EqualTo(catalog.Title("toho_y22_5").Band));
            Assert.That(catalog.Title("toho_y22_3").Icon, Is.Not.EqualTo(catalog.Title("toho_y22_5").Icon));
            Assert.That(catalog.Title("missing"), Is.EqualTo(catalog.Title("wood")));
        }

        // The layers a plate on show wears (the title's own, as long as the player picks none).
        static (OurTaiko.Chara.OtanimAsset band, OurTaiko.Chara.OtanimAsset effect, OurTaiko.Chara.OtanimAsset icon) Wearing(NameplateLayers layers) => (
            layers.BandView.gameObject.activeSelf ? layers.BandView.asset : null,
            layers.EffectView.gameObject.activeSelf ? layers.EffectView.asset : null,
            layers.IconView.gameObject.activeSelf ? layers.IconView.asset : null);

        [TestCase("wood", false, false)]
        [TestCase("crown_gold", false, true)]
        [TestCase("gold", true, false)]
        [TestCase("sekaitaikai_05", true, true)]
        [TestCase("ai_3", true, false)]
        public void ATitleIsDrawnAsItsBandEffectAndIcon(string title, bool effect, bool icon)
        {
            TestData.RequireBakedNameplates();
            using var preview = new NameplatePreview();
            var info = new PlayerInfo { name = "Player", title = "Title", plateTitle = title };
            preview.Show(info, 0);
            var parts = PlateComposition.ReadCatalog().Title(title);
            var layers = preview.Plate.Layers;
            var (band, effectAsset, iconAsset) = Wearing(layers);
            Assert.That(band, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, parts.Band)));
            Assert.That(effectAsset != null, Is.EqualTo(effect));
            Assert.That(iconAsset != null, Is.EqualTo(icon));
            if (effect) Assert.That(effectAsset, Is.SameAs(NameplateItems.Load(NameplateSlot.Effect, parts.Effect)));
            if (icon) Assert.That(iconAsset, Is.SameAs(NameplateItems.Load(NameplateSlot.Icon, parts.Icon)));

            // The band is the plate's own picture of it, so a title's decoration may leave it: only an
            // effect that stays inside the band is cut to it.
            var root = preview.Plate.transform.Find("Complete nameplate");
            Assert.That(layers.BandView.transform.parent, Is.EqualTo(root));
            if (effect)
            {
                bool inside = !parts.EffectOverflow;
                Assert.That(layers.EffectView.transform.parent, Is.EqualTo(inside ? layers.EffectMask : root));
                Assert.That(layers.EffectMask.gameObject.activeInHierarchy, Is.EqualTo(inside), "The band's shape is only there to cut an effect that stays inside the band.");
                Assert.That(layers.BandMask.gameObject.activeInHierarchy, Is.False, "A title's own band is not cut.");
                // The title's text is printed on the band and the effect sparkles over it.
                Assert.That(Order(preview, layers.BandView), Is.LessThan(Order(preview, preview.Plate.title)));
                Assert.That(Order(preview, layers.EffectView), Is.GreaterThan(Order(preview, preview.Plate.title)));
            }
            // The icon is over the outline and under the badge; the band under both.
            if (icon) Assert.That(layers.IconView.transform.GetSiblingIndex(), Is.GreaterThan(layers.BandView.transform.GetSiblingIndex()));
        }

        [Test]
        public void ATitleWithoutABadgePlateStillShowsItsIcon()
        {
            TestData.RequireBakedNameplates();
            using var preview = new NameplatePreview();
            // newentry_shougou has no 1P/2P badge: the icon goes right after the title (band, text, effect).
            var info = new PlayerInfo { name = "Player", title = "Title", plateTitle = "crown_gold", plateState = "newentry_shougou" };
            preview.Show(info, 0);
            var layers = preview.Plate.Layers;
            Assert.That(layers.IconView.gameObject.activeSelf);
            Assert.That(layers.IconView.transform.GetSiblingIndex(), Is.GreaterThan(layers.BandView.transform.GetSiblingIndex()));
            Assert.That(Order(preview, layers.IconView), Is.GreaterThan(Order(preview, layers.EffectView)), "Over the effect, and so over the title's text.");
        }

        [Test]
        public void ARankUpIsPutBackByIdleEvenWhenTheTitleWearsAnIcon()
        {
            TestData.RequireBakedNameplates();
            using var preview = new NameplatePreview();
            var info = new PlayerInfo { name = "Player", title = "Title", plateTitle = "crown_gold", dan = 24 };
            preview.Show(info, 0);
            var rankup = NameplateItems.Load(NameplateSlot.Icon, "icon/lumen-rankup");
            var view = System.Array.Find(preview.Plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>(), v => v.asset == rankup);
            Assert.That(view, Is.Not.Null);
            Assert.That(preview.Plate.PlayLayerClipAt(NameplateSlot.Icon, "14dan/rankup_start", 0));
            Assert.That(view.clip, Is.EqualTo("14dan/rankup_start"));
            Assert.That(preview.Plate.PlayLayerClipAt(NameplateSlot.Icon, "idle", 1));
            Assert.That(view.clip, Is.EqualTo("idle"), "The crown's own idle does not take the call.");
        }
    }
}
