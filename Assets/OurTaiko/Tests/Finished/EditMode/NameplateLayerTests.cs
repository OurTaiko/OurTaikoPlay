using System.Collections.Generic;
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
            NameplateArt.Forget();
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

        // The plate and the bands opaque white (the band's shape is its whole rect), every other
        // piece (outline, 段位 chip, badge) see-through, so what the layers draw is what shows.
        static void WhiteArt()
        {
            var images = new Dictionary<string, byte[]>();
            foreach (var (key, width, height) in NameplateArt.Files)
            {
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                var pixels = new Color32[width * height];
                byte alpha = key == "shadow" || key == "bands" || key == "rainbow" ? (byte)255 : (byte)0;
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, alpha);
                texture.SetPixels32(pixels);
                images[key] = texture.EncodeToPNG();
                Object.DestroyImmediate(texture);
            }
            NameplateArt.Use(images);
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
            Assert.That((old.bandItem, old.effectItem, old.iconItem, old.titleBackground), Is.EqualTo(("", "", "", 2)));
            var nulls = PlayerInfo.FromJson("{\"bandItem\":null}");
            Assert.That(nulls.bandItem, Is.EqualTo(""));
        }

        [Test]
        public void AnItemIsReadFromItsSlotsFolderOnlyWhenDrawnOnThePlate()
        {
            Item(NameplateSlot.Band, "b", 0);
            Item(NameplateSlot.Band, "a", 0);
            Item(NameplateSlot.Effect, "wide", new Rect(0, 0, 500, 96), 0);
            File.WriteAllText(Path.Combine(folder, "band", "notes.txt"), "not an item");
            Directory.CreateDirectory(Path.Combine(folder, "icon"));
            File.WriteAllBytes(NameplateItems.PathOf(NameplateSlot.Icon, "broken"), new byte[] { 1, 2, 3 });

            Assert.That(NameplateItems.Names(NameplateSlot.Band), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(NameplateItems.Names(NameplateSlot.Effect), Is.EqualTo(new[] { "wide" }));

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

        static bool IsRed(Color c) => c.r > 0.8f && c.g < 0.2f && c.b < 0.2f;
        static bool IsBlue(Color c) => c.b > 0.8f && c.r < 0.2f && c.g < 0.2f;

        // A plate with a 段位 and no title or name: its band shows, and nothing is written over it.
        static PlayerInfo Plain() => new PlayerInfo { name = "", title = "", dan = 10 };

        [Test]
        public void AWornBandTakesTheBandsPlaceAndItAndItsEffectStayInsideTheBand()
        {
            WhiteArt();
            Item(NameplateSlot.Band, "moving", 250, 350);
            Item(NameplateSlot.Effect, "still", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var plate = preview.Plate;
            var info = Plain();
            info.bandItem = "band/moving";
            preview.Show(info, 0);

            Assert.That(plate.band.enabled, Is.False, "The worn band is drawn instead of the plate's own.");
            var layers = plate.Layers;
            Assert.That(layers.BandView.gameObject.activeSelf && !layers.EffectView.gameObject.activeSelf && !layers.IconView.gameObject.activeSelf);
            Assert.That(layers.Mask.GetSiblingIndex(), Is.EqualTo(plate.band.transform.GetSiblingIndex() + 1));
            Assert.That(layers.IconView.transform.GetSiblingIndex(), Is.EqualTo(plate.outline.transform.GetSiblingIndex() + 1));

            // The square's left half is red, its right half blue; the band is the plate's top 56.
            Assert.That(IsRed(Pixel(preview, 275, 20)) && IsBlue(Pixel(preview, 325, 20)), "The band item is drawn where the band is.");
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False, "Below the band it is cut away.");
            preview.Seek(1);
            Assert.That(IsRed(Pixel(preview, 375, 20)) && !IsRed(Pixel(preview, 275, 20)), "It moves on with the time.");

            info.bandItem = "";
            info.effectItem = "effect/still";
            preview.Show(info, 0);
            Assert.That(plate.band.enabled, "Without a band item the plate's own band is back, the effect over it.");
            Assert.That(IsRed(Pixel(preview, 275, 20)), "The effect is drawn over the band.");
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False, "And cut to it.");
        }

        [Test]
        public void ATriggeredClipStartsAtItsOwnFirstFrameAndHoldsItsEnd()
        {
            WhiteArt();
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
            Assert.That(layers.Play(NameplateSlot.Icon, "start", 7));
            Assert.That(IsRed(Pixel(preview, 145, 80)));
            Assert.That(layers.Play(NameplateSlot.Icon, "missing", 7), Is.False);
            preview.Seek(12);
            Assert.That(IsRed(Pixel(preview, 285, 80)));
        }

        [Test]
        public void AnIconIsDrawnOverTheOutlineAndOutOfTheBand()
        {
            WhiteArt();
            Item(NameplateSlot.Icon, "badge", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var info = Plain();
            info.iconItem = "icon/badge";
            preview.Show(info, 0);
            Assert.That(preview.Plate.band.enabled, "An icon leaves the band alone.");
            Assert.That(IsRed(Pixel(preview, 275, 20)) && IsRed(Pixel(preview, 275, 80)), "Over the band and below it.");
        }

        // Some layer is on show (none are made before the plate first wears one).
        static bool Worn(NameplateLayers layers) =>
            layers?.Mask != null && (layers.BandView.gameObject.activeInHierarchy || layers.EffectView.gameObject.activeInHierarchy || layers.IconView.gameObject.activeSelf);

        [Test]
        public void TheCoinPlateAndAPlateWithoutArtWearNothing()
        {
            WhiteArt();
            Item(NameplateSlot.Icon, "badge", 250);
            using var preview = new NameplatePreview { Background = Color.black };
            var coin = new PlayerInfo { name = "", title = "", iconItem = "icon/badge" };
            Assert.That(coin.IsCoin);
            preview.Show(coin, 0);
            Assert.That(Worn(preview.Plate.Layers), Is.False);
            Assert.That(IsRed(Pixel(preview, 275, 80)), Is.False);

            NameplateArt.Forget();
            var plain = Plain();
            plain.iconItem = "icon/badge";
            preview.Show(plain, 0);
            Assert.That(Worn(preview.Plate.Layers), Is.False, "Without the art a plate is its text alone.");
        }

        [Test]
        public void RenderAllWritesASheetOfItemsAndOneOfCombinations()
        {
            WhiteArt();
            Item(NameplateSlot.Band, "a", 0, 200);
            Item(NameplateSlot.Icon, "b", 250);
            string dir = Path.Combine(folder, "out");
            NameplatePreview.RenderAll(dir);
            var items = new Texture2D(2, 2);
            var combos = new Texture2D(2, 2);
            try
            {
                Assert.That(items.LoadImage(File.ReadAllBytes(Path.Combine(dir, "items.png"))));
                Assert.That((items.width, items.height), Is.EqualTo((NameplatePreview.Width * NameplatePreview.Frames, NameplatePreview.Height * 2)));
                Assert.That(combos.LoadImage(File.ReadAllBytes(Path.Combine(dir, "combinations.png"))));
                // (none, a) bands × (none) effects × (none, b) icons, four to a row.
                Assert.That((combos.width, combos.height), Is.EqualTo((NameplatePreview.Width * 4, NameplatePreview.Height)));
            }
            finally
            {
                Object.DestroyImmediate(items);
                Object.DestroyImmediate(combos);
            }
        }
    }
}
