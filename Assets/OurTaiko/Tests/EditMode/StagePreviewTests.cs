using System.Linq;
using NUnit.Framework;
using OurTaiko.Editor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The preset viewer's backdrop: each mode's own play scene, driven outside play.
    public sealed class StagePreviewTests
    {
        static Color Pixel(StagePreview preview, float x, float y)
        {
            var picture = preview.Capture(480, 270);
            try { return picture.GetPixel((int)(x * 480), (int)((1 - y) * 270)); }
            finally { Object.DestroyImmediate(picture); }
        }

        [TestCase(StageMode.Single, 1, true)]
        [TestCase(StageMode.Double, 2, false)]
        [TestCase(StageMode.Dan, 1, true)]
        [TestCase(StageMode.Practice, 1, true)]
        [TestCase(StageMode.Tutorial, 1, true)]
        public void EveryModeDrawsItsBackdrop(StageMode mode, int players, bool scene)
        {
            using var preview = new StagePreview(mode);
            Assert.That((preview.Players, preview.Stage.scene != null), Is.EqualTo((players, scene)));
            // What the game carries is enough for every mode.
            preview.Use(preview.Choose(null, 0));
            preview.Run(StagePreview.Shots[0], 1);
            Assert.That(preview.Stage.headers.Select(h => h.Layers.Count), Has.All.GreaterThan(0));
            Assert.That(Pixel(preview, 0.5f, 0.1f).maxColorComponent, Is.GreaterThan(0.1f), "1P's header is drawn.");
            Assert.That(Pixel(preview, 0.5f, 0.9f).maxColorComponent, Is.GreaterThan(0.1f), scene ? "The scene is drawn." : "2P's header is drawn.");
            var lane = Pixel(preview, 0.5f, 0.4f);
            Assert.That(lane.r, Is.InRange(0.1f, 0.25f), "The strip stands where the lanes go.");
            preview.LaneShown = false;
            Assert.That(Pixel(preview, 0.5f, 0.4f).maxColorComponent, Is.LessThan(0.05f));
        }

        // The states a play puts the backdrop in, set by hand.
        [Test]
        public void TheGaugeAndMissesChangeTheBackdrop()
        {
            using var preview = new StagePreview(StageMode.Single);
            var stage = preview.Stage;
            TestStage.Require("TOHO");
            preview.Use(preview.Choose("TOHO", 0));
            Assert.That(stage.Choice.Preset, Is.EqualTo("TOHO"));

            preview.Run(new StagePreview.Shot("normal", 0.3), 4);
            Assert.That((stage.headers[0].ClearAlpha, stage.scene.ClearAlpha, stage.crowd.ClearAlpha, stage.headers[0].Shade), Is.EqualTo((0f, 0f, 0f, 1f)));
            Assert.That(stage.dancers.Troupe.Active, Is.EqualTo(2), "A collab's lead and one more below half the clear line.");

            preview.Run(new StagePreview.Shot("miss", 0.3, missing: true), 1);
            Assert.That(stage.headers[0].Shade, Is.EqualTo(stage.headers[0].darkShade));

            preview.Run(new StagePreview.Shot("clear", StagePreview.ClearLine), 4);
            Assert.That((stage.headers[0].ClearAlpha, stage.scene.ClearAlpha, stage.crowd.ClearAlpha), Is.EqualTo((1f, 1f, 0f)));
            Assert.That(stage.dancers.Troupe.Active, Is.EqualTo(5));

            preview.Run(new StagePreview.Shot("full", 1, hits: true), 1);
            Assert.That(stage.crowd.ClearAlpha, Is.EqualTo(1f), "The crowd comes with a full gauge.");
            Assert.That(stage.renda[0].Out, Is.GreaterThan(0), "A drumroll throws its effect over the header.");
        }

        // A pack for solo play brings its hit characters; another backdrop takes them away.
        [Test]
        public void APacksHitCharactersComeOutOnJudgements()
        {
            using var preview = new StagePreview(StageMode.Single);
            TestStage.Require("ANIMAL");
            TestStage.Require("TOHO");
            preview.Use(preview.Choose("ANIMAL", 0));
            preview.Run(new StagePreview.Shot("full", 1, hits: true), 1);
            Assert.That(preview.Stage.Characters.Out, Is.GreaterThan(0));
            preview.Use(preview.Choose("TOHO", 0));
            preview.Run(new StagePreview.Shot("full", 1, hits: true), 1);
            Assert.That(preview.Stage.Characters.Out, Is.EqualTo(0));
        }
    }
}
