using System.Linq;
using NUnit.Framework;
using OurTaiko.Chara;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The backdrop dancers: the troupe's shared playhead (Nijiiro ArcadeDancerGroup's rules) and
    // each dancer's timeline baked into one .otanim clip.
    public sealed class DancerTests
    {
        const string Art = "Assets/OurTaiko/Resources/Stage/dancers/";
        const int Dancers = 5;

        // 150 BPM is 100 rig frames a second, so the half-second way in is 50 of them and the
        // playhead starts at 30, landing the opening troupe on 80.
        static DancerTroupe TroupeAt(double frame, out double time)
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray());
            troupe.Advance(0, 150);
            time = (frame - 30) / 100;
            troupe.Advance(time, 150);
            return troupe;
        }

        static int[] Frames(DancerTroupe troupe) => Enumerable.Range(0, troupe.Slots).Select(troupe.FrameOf).ToArray();

        [Test]
        public void ThreeHopInTogetherAndLoopTheDance()
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray());
            troupe.Advance(10, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 0, 0, 0, -1, -1 }), "They are on stage from the start.");
            troupe.Advance(10.495, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 79, 79, 79, -1, -1 }));
            troupe.Advance(10.505, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 80, 80, 80, -1, -1 }), "Landed on the phrase half a second in.");
            troupe.Advance(16.895, 150);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(719));
            troupe.Advance(17.005, 150);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(90), "The dance loops without replaying the hop in.");
        }

        // Measured on the arcade: the way in is half a second at any tempo, and lands on the beat.
        [TestCase(75.0)]
        [TestCase(150.0)]
        [TestCase(300.0)]
        public void TheWayInTakesHalfASecondAtAnyTempo(double bpm)
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray());
            troupe.Advance(0, bpm);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(0));
            troupe.Advance(0.25, bpm);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(40));
            troupe.Advance(0.499, bpm);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(79));
            troupe.Advance(0.501, bpm);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(80));
            Assert.That(troupe.Frame, Is.EqualTo(80 + 0.001 * bpm / 60 * DancerTroupe.BeatFrames).Within(1e-6), "The dance starts on its phrase.");
        }

        // The way out mirrors the way in: it starts on a phrase and takes half a second at any tempo.
        [TestCase(75.0)]
        [TestCase(150.0)]
        [TestCase(300.0)]
        public void TheWayOutStartsOnAPhraseAndTakesHalfASecond(double bpm)
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray(), leadFirst: true);
            troupe.SetCount(2);
            troupe.Advance(0, bpm);
            double time = 1;
            troupe.Advance(time, bpm);
            Assert.That(troupe.FrameOf(1), Is.GreaterThanOrEqualTo(80), "Both have landed.");
            troupe.SetCount(1);
            const double step = 0.001;
            while (troupe.FrameOf(1) < 720) troupe.Advance(time += step, bpm);
            Assert.That(troupe.FrameOf(1), Is.EqualTo(720));
            double rate = bpm / 60 * DancerTroupe.BeatFrames;
            Assert.That((troupe.Frame - 80) % DancerTroupe.Phrase, Is.LessThanOrEqualTo(step * rate + 1e-6), "It set off on a phrase.");
            troupe.Advance(time + 0.25, bpm);
            Assert.That(troupe.FrameOf(1), Is.EqualTo(760));
            troupe.Advance(time + 0.498, bpm);
            Assert.That(troupe.FrameOf(1), Is.EqualTo(799));
            troupe.Advance(time + 0.501, bpm);
            Assert.That(troupe.FrameOf(1), Is.EqualTo(-1));
            Assert.That(troupe.FrameOf(0), Is.GreaterThanOrEqualTo(80), "The lead stays.");
        }

        // A play never goes below the fewest; Assemble does, so the ones who always stay can be seen
        // leaving and coming back by their own ways.
        [Test]
        public void AssembleTakesEvenTheOnesWhoAlwaysStayOffAndBack()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(0);
            Assert.That(troupe.Active, Is.EqualTo(3));
            troupe.Assemble(0);
            Assert.That(troupe.Active, Is.EqualTo(0));
            troupe.Advance(time + 0.705, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 736, 736, 736, -1, -1 }), "They set off on the phrase at 160.");
            troupe.Advance(time + 1.105, 150);
            Assert.That(Frames(troupe), Is.All.EqualTo(-1));
            // At 210.5, so half a second on is 260.5: they land on the phrase at 320, setting off at 270.
            troupe.Assemble(5);
            Assert.That(troupe.Active, Is.EqualTo(5));
            troupe.Advance(time + 2.0, 150);
            Assert.That(Frames(troupe), Is.All.EqualTo(48), "All five hop in together.");
            troupe.Advance(time + 2.205, 150);
            Assert.That(Frames(troupe), Is.All.EqualTo(320), "Landed on it, on the shared frame.");
        }

        // A rig whose dance starts at 0 has no way in: the opening troupe is dancing from the start.
        [Test]
        public void ARigWithNoWayInDancesFromTheStart()
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(380, 0, 380), 5).ToArray(), leadFirst: true);
            troupe.Advance(0, 150);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(0));
            troupe.Advance(1.005, 150);
            troupe.SetCount(2);
            Assert.That(troupe.FrameOf(1), Is.EqualTo(-1), "Earned at 100, it appears on the phrase at 160.");
            troupe.Advance(1.605, 150);
            Assert.That((troupe.FrameOf(0), troupe.FrameOf(1)), Is.EqualTo((160, 160)));
        }

        [Test]
        public void TheFourthWaitsForThePhraseAndJoinsOnTheSharedFrame()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(4);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(-1));
            troupe.Advance(time + 0.095, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(-1), "Earned at 100, it hops in from 110 to land on 160.");
            troupe.Advance(time + 0.105, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(0));
            troupe.Advance(time + 0.595, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(79));
            troupe.Advance(time + 0.605, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 160, 160, 160, 160, -1 }));
            troupe.Advance(time + 1.505, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 250, 250, 250, 250, -1 }));
        }

        [Test]
        public void LeavingFinishesThePhraseThenPlaysTheWayOut()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(4);
            troupe.Advance(time + 1.505, 150);
            troupe.SetCount(3);
            Assert.That(troupe.Active, Is.EqualTo(3));
            Assert.That(troupe.FrameOf(3), Is.EqualTo(250));
            troupe.Advance(time + 2.195, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(319));
            troupe.Advance(time + 2.305, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(736), "The way out starts on the phrase at 320, half a second for its 80 frames.");
            troupe.Advance(time + 2.695, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(799));
            troupe.Advance(time + 2.705, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(-1));
            Assert.That(troupe.FrameOf(0), Is.EqualTo(370));
        }

        [Test]
        public void ADancerLostBeforeItLandsNeverArrives()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(5);
            troupe.Advance(time + 0.3, 150);
            Assert.That(troupe.FrameOf(4), Is.EqualTo(32));
            troupe.SetCount(3);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 130, 130, 130, -1, -1 }));
            troupe.SetCount(1);
            Assert.That(troupe.Active, Is.EqualTo(3), "Three always stay.");
        }

        [Test]
        public void ThePlayheadFollowsTheTempoAndNeverRunsBackwards()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.Advance(time - 5, 150);
            Assert.That(troupe.Frame, Is.EqualTo(100).Within(1e-6));
            troupe.Advance(time - 4, 300);
            Assert.That(troupe.Frame, Is.EqualTo(300).Within(1e-6));
            troupe.Advance(time - 3, -75);
            Assert.That(troupe.Frame, Is.EqualTo(350).Within(1e-6));
            troupe.Advance(time - 2, 0);
            Assert.That(troupe.Frame, Is.EqualTo(430).Within(1e-6), "No tempo plays at 120.");
        }

        [Test]
        public void TheGaugeEarnsTheFourthAtHalfAndTheFifthWithTheClear()
        {
            Assert.That(DancerTroupe.CountFor(0, false), Is.EqualTo(3));
            Assert.That(DancerTroupe.CountFor(0.499, false), Is.EqualTo(3));
            Assert.That(DancerTroupe.CountFor(0.5, false), Is.EqualTo(4));
            Assert.That(DancerTroupe.CountFor(0.99, false), Is.EqualTo(4));
            Assert.That(DancerTroupe.CountFor(1, false), Is.EqualTo(5));
            Assert.That(DancerTroupe.CountFor(0.7, true), Is.EqualTo(5));
        }

        static OtanimAsset Dancer(int which) => TestStage.Asset($"dancers/toho_{which}");

        // The middle of what a frame draws, in stage pixels from the left.
        static float Middle(Otanim file, Otanim.Clip rig, int frame)
        {
            var xs = rig.Frames[frame].Draws.SelectMany(d => Enumerable.Range(0, file.Parts[d.Part].UVs.Length).Select(i => rig.Positions[d.Offset + 2 * i])).ToArray();
            return (xs.Min() + xs.Max()) / 2;
        }

        // Each dancer's timeline is baked whole at 40 frames a beat: two beats in, eight of dance,
        // two out, drawn where that dancer stands on the stage.
        [Test]
        public void EachDancerIsItsWholeTimelineInItsOwnPlace()
        {
            var middles = new float[Dancers];
            for (int k = 0; k < Dancers; k++)
            {
                var asset = Dancer(k);
                var rig = asset.File.Find(DancerView.RigClip);
                Assert.That((rig.Fps, rig.Loop, rig.Frames.Length), Is.EqualTo((40f, false, 480)));
                Assert.That(Enumerable.Range(80, 320).All(f => rig.Frames[f].Draws.Length >= 1), Is.True, "It is on stage for the whole dance.");
                Assert.That(asset.File.View, Is.EqualTo(new Rect(0, 0, 1920, 1080)), "The clip is the whole stage.");
                Assert.That(GraphicsFormatUtility.IsCompressedFormat(asset.texture.graphicsFormat), Is.True, "The pictures are block-compressed.");
                middles[k] = Middle(asset.File, rig, 80);
            }
            // In the order they come: centre, left, right, far left, far right.
            Assert.That(new[] { 3, 1, 0, 2, 4 }.Select(k => middles[k]), Is.Ordered);
            Assert.That(middles[0], Is.EqualTo(960).Within(40));
        }

        [Test]
        public void BothPlayScenesStandFiveDancersOnTheLowerBand()
        {
            foreach (string path in new[] { "Assets/Scenes/Play/Play.Single.unity", "Assets/Scenes/Play/Play.Practice.unity" })
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single();
                    var troupe = play.stage.dancers;
                    Assert.That(troupe.dancers.Select(d => d.View.asset), Has.All.Null, "The stage gives them their troupe when a play starts.");
                    // Each clip is the whole stage, so every dancer's rect is the troupe's, which is the stage's.
                    Assert.That(((RectTransform)troupe.transform).anchoredPosition, Is.EqualTo(Vector2.zero));
                    Assert.That(((RectTransform)troupe.transform).sizeDelta, Is.EqualTo(new Vector2(1920, 1080)));
                    foreach (var dancer in troupe.dancers)
                    {
                        var rect = (RectTransform)dancer.transform;
                        Assert.That((rect.parent, rect.anchorMin, rect.anchorMax, rect.offsetMin, rect.offsetMax),
                            Is.EqualTo((troupe.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero)));
                    }
                    // Drawn over the scene they stand in.
                    var lower = troupe.transform.parent.Find("StageLower");
                    Assert.That(troupe.transform.GetSiblingIndex(), Is.GreaterThan(lower.GetSiblingIndex()));
                    // The centre dancer is drawn last.
                    Assert.That(troupe.dancers.Select(d => d.transform.GetSiblingIndex()), Is.EqualTo(new[] { 4, 3, 2, 1, 0 }));

                    TestStage.Require("TOHO");
                    play.stage.Setup("TOHO", new System.Random(1));
                    if (play.stage.mode == StageMode.Practice)
                    {
                        Assert.That(play.stage.Choice.Preset, Is.EqualTo("PRACTICE"));
                        Assert.That(troupe.Troupe, Is.Null);
                        continue;
                    }
                    Assert.That(play.stage.Choice.Troupe, Is.EqualTo("toho"));
                    Assert.That(troupe.dancers.Select(d => d.View.asset), Is.EqualTo(Enumerable.Range(0, Dancers).Select(Dancer)));
                    Assert.That((troupe.Troupe.Slots, troupe.Troupe.Active, troupe.IsMovie), Is.EqualTo((5, 1, false)), "The lead of a collab troupe comes alone.");
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }

        // A collab troupe's lead comes alone; the others join as the gauge fills, all by the clear.
        [Test]
        public void ATroupeWhoseLeadComesFirstGrowsToFiveByTheClear()
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray(), leadFirst: true);
            Assert.That(troupe.Active, Is.EqualTo(1));
            Assert.That(new[] { 0, 0.24, 0.25, 0.5, 0.75, 0.99 }.Select(g => DancerTroupe.CountFor(g, false, true)), Is.EqualTo(new[] { 1, 1, 2, 3, 4, 4 }));
            Assert.That(DancerTroupe.CountFor(0.7, true, true), Is.EqualTo(5));
            troupe.SetCount(0);
            Assert.That(troupe.Active, Is.EqualTo(1), "The lead stays.");
            // The usual troupe is unchanged: three, the fourth from half a gauge.
            Assert.That(new[] { 0, 0.49, 0.5, 0.99 }.Select(g => DancerTroupe.CountFor(g, false)), Is.EqualTo(new[] { 3, 3, 4, 4 }));
        }

        // A troupe that is one movie: all of it on stage from the start, replayed on the beat.
        [Test]
        public void AMovieTroupeIsOneClipReplayedOnTheBeat()
        {
            var root = new GameObject("Dancers", typeof(RectTransform), typeof(DancerTroupeView));
            try
            {
                var view = root.GetComponent<DancerTroupeView>();
                view.dancers = Enumerable.Range(0, 2).Select(i =>
                {
                    var go = new GameObject("Dancer" + i, typeof(RectTransform), typeof(OtanimView), typeof(DancerView));
                    go.transform.SetParent(root.transform, false);
                    return go.GetComponent<DancerView>();
                }).ToArray();
                view.Use(new[] { Dancer(1) }, new StageTroupe { name = "movie", movie = true });
                int frames = Dancer(1).File.Find(DancerView.RigClip).Frames.Length;
                Assert.That((view.IsMovie, view.Troupe.Slots), Is.EqualTo((true, 1)));
                // 150 BPM is 100 rig frames a second.
                view.ShowTime(0, 150, 0, false);
                view.ShowTime(1.005, 150, 0, false);
                Assert.That((view.dancers[0].Frame, view.dancers[1].Frame), Is.EqualTo((100, -1)));
                view.ShowTime(1.005 + frames / 100.0, 150, 0, false);
                Assert.That(view.dancers[0].Frame, Is.EqualTo(100), "It comes round.");
                view.Use(null, null);
                Assert.That((view.Troupe, view.dancers[0].Frame), Is.EqualTo(((DancerTroupe)null, -1)));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
