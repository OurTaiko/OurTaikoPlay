using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Two-player play (issue #5): DoublePlayScene drives one lane per seat, each reading its own
    // block of the course and its own drum with its own once-per-frame input lock.
    public sealed class DoublePlayFlowTests
    {
        Keyboard keyboard;
        InputSettings.BackgroundBehavior originalBackground;
        InputSettings.EditorInputBehaviorInPlayMode originalEditorBehavior;
        PlayOptions originalFirst, originalSecond;
        double clock;

        [SetUp]
        public void SetUp()
        {
            var settings = InputSystem.settings;
            originalBackground = settings.backgroundBehavior;
            originalEditorBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard = InputSystem.AddDevice<Keyboard>("DoublePlayKeyboard");
            keyboard.MakeCurrent();
            clock = InputState.currentTime;
            originalFirst = PlayOptions.Shared; originalSecond = PlayOptions.Second;
            PlayOptions.Shared = new PlayOptions(); PlayOptions.Second = new PlayOptions();
        }

        [TearDown]
        public void TearDown()
        {
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = originalBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = originalEditorBehavior;
            PlayOptions.Shared = originalFirst; PlayOptions.Second = originalSecond;
            if (SceneSwitcher.Instance != null) SceneSwitcher.Instance.TwoPlayers = false;
        }

        void Press(params Key[] order)
        {
            for (int i = 1; i <= order.Length; i++)
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(order.Take(i).ToArray()), clock += 0.001);
        }
        void ReleaseAll() => InputSystem.QueueStateEvent(keyboard, new KeyboardState(), clock += 0.001);

        // A rect's box in screen-space world coordinates.
        static Rect Corners(Transform target)
        {
            var corners = new Vector3[4];
            ((RectTransform)target).GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        static IEnumerator PlayBoth(SongDefinition song, bool auto1P = false)
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            while (SceneSwitcher.Instance.IsInputBlocked) yield return null;
            SceneSwitcher.Instance.TwoPlayers = true;
            SceneSwitcher.Instance.Play(song, "Oni", auto1P, "Oni", false);
            float deadline = Time.realtimeSinceStartup + 20;
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.DoublePlayScene)
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Still in " + SceneManager.GetActiveScene().name);
            }
        }

        [UnityTest]
        public IEnumerator EachSeatJudgesItsOwnDrumAndItsOwnBlock()
        {
            // 1P's block is one drumroll 0–4 s; 2P's block adds a don after it, so the seats read different blocks.
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Two Players\nBPM:120\nCOURSE:Oni\nLEVEL:1\n"
                + "#START P1\n5,\n0,\n8,\n#END\n#START P2\n5,\n0,\n8,\n1,\n#END");
            yield return PlayBoth(song);
            var play = Object.FindAnyObjectByType<PlayScene>();
            Assert.That(play.IsTwoPlayer, Is.True);
            Assert.That(play.Lanes.Count, Is.EqualTo(2));
            var first = play.Lanes[0].Session; var second = play.Lanes[1].Session;
            Assert.That(first.Chart.Notes.Count(n => n.Kind == NoteKind.Don), Is.Zero, "1P reads the P1 block.");
            Assert.That(second.Chart.Notes.Count(n => n.Kind == NoteKind.Don), Is.EqualTo(1), "2P reads the P2 block.");
            Assert.That((SceneSwitcher.Instance.SideOf(0), SceneSwitcher.Instance.SideOf(1)), Is.EqualTo(("p1", "p2")), "decided before the play");
            Assert.That(play.dancers, Is.Null, "Two players have no dancers.");
            // The play option badges sit where 1P's do on its own lane, flipped inside the mirrored score cover.
            Assert.That(play.secondLane.modifierBadges.flipped && !play.modifierBadges.flipped, Is.True);
            Assert.That(Corners(play.secondLane.modifierBadges.transform).center.y - Corners(play.secondLane.transform).yMax,
                Is.EqualTo(Corners(play.modifierBadges.transform).center.y - Corners(play.noteLayer.parent.parent).yMax).Within(0.01f));
            // SoulGaugeView places the gold zone every frame: 2P's mirrored gauge keeps its own heights.
            var gauge2P = play.secondLane.soulGauge;
            Assert.That(gauge2P.goldTopY, Is.Not.EqualTo(play.soulGauge.goldTopY));
            Assert.That(gauge2P.goldTopY, Is.EqualTo(((RectTransform)gauge2P.transform.Find("GoldTop")).anchoredPosition.y));
            // 2P's timing feedback and judge counter mirror 1P's across the line between the lanes.
            float split = (Corners(play.noteLayer.parent.parent).yMin + Corners(play.secondLane.transform).yMax) / 2;
            // So does the play ScoreRank, which would otherwise cover 2P's nameplate.
            foreach (var (one, two) in new[] { ((Component)play.timingFeedback, (Component)play.secondLane.timingFeedback), (play.judgeCounter, play.secondLane.judgeCounter),
                (play.scoreCounter.scoreRank.transform.Find("ScoreRank"), play.secondLane.scoreCounter.scoreRank.transform.Find("ScoreRank")) })
            {
                Assert.That(two, Is.Not.Null);
                var a = Corners(one.transform); var b = Corners(two.transform);
                Assert.That(b.center.y, Is.EqualTo(2 * split - a.center.y).Within(0.5f), two.name + " mirrors " + one.name);
                Assert.That(b.center.x, Is.EqualTo(a.center.x).Within(0.5f));
            }
            // 2P's score additions rise below its lane: the row's digit line mirrors 1P's (DigitTop - 58.5 below the row's top).
            float below = ScoreCounterLayout.DigitTop - 58.5f + ScoreCounterLayout.DigitHeight / 2;
            float scale = Corners(play.noteLayer.parent.parent).height / 264;
            float line1 = Corners(play.scoreCounter.additionTemplate.transform).yMax - below * scale;
            float line2 = Corners(play.secondLane.scoreCounter.additionTemplate.transform).yMax - below * scale;
            Assert.That(line2, Is.EqualTo(2 * split - line1).Within(0.5f));

            float deadline = Time.realtimeSinceStartup + 10;
            while (play.SongTime < 0.5)
            {
                if (play.IsPaused) play.TogglePause();
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
            TestCapture.Capture("DoublePlay.png");

            // Both drums in one frame: each seat judges its own earliest press.
            Press(Key.D, Key.F, Key.Z, Key.X, Key.J, Key.C);
            yield return null;
            Assert.That(InputManager.P1.PressesThisFrame.Select(p => p.Key), Is.EqualTo(new[] { InputKey.LeftKa, InputKey.LeftDon, InputKey.RightDon }));
            Assert.That(InputManager.P2.PressesThisFrame.Select(p => p.Key), Is.EqualTo(new[] { InputKey.LeftKa, InputKey.LeftDon, InputKey.RightDon }),
                "2P's presses are named as drum faces.");
            Assert.That(first.Rolls, Is.EqualTo(1));
            Assert.That(second.Rolls, Is.EqualTo(1), "1P's lock does not take 2P's press.");
            Assert.That(play.Lanes[0].Views.drumFlashes.Select(f => f.enabled), Is.EqualTo(new[] { false, false, true, false }));
            Assert.That(play.Lanes[1].Views.drumFlashes.Select(f => f.enabled), Is.EqualTo(new[] { false, false, true, false }));

            // Only 2P strikes: 1P's lane does not move.
            ReleaseAll(); yield return null;
            Press(Key.V); yield return null;
            Assert.That(first.Rolls, Is.EqualTo(1));
            Assert.That(second.Rolls, Is.EqualTo(2));
            Assert.That(play.Lanes[1].Record.Inputs.Count, Is.EqualTo(2));
            Assert.That(play.Lanes[0].Record.Inputs.Count, Is.EqualTo(1));
            Assert.That(play.secondLane.judgeCounter.gameObject.activeInHierarchy, Is.True);
            ReleaseAll(); yield return null;
            Object.Destroy(song);
        }

        [UnityTest]
        public IEnumerator BothResultsReachTheResultScene()
        {
            // A one-measure chart at 240 BPM ends a second after its last note (no audio).
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.name = "Two Players End " + System.Guid.NewGuid();
            song.chart = new TextAsset("TITLE:Two Players End\nBPM:240\nCOURSE:Oni\nLEVEL:1\n#START\n1,\n#END");
            var scores = ScoreStore.Shared;
            ScoreStore.Shared = new ScoreStore(System.IO.Path.Combine(Application.temporaryCachePath, "two-players-" + System.Guid.NewGuid() + ".sqlite3"));
            try
            {
                yield return PlayBoth(song, auto1P: true);
                float deadline = Time.realtimeSinceStartup + 30;
                while (SceneManager.GetActiveScene().name != SceneSwitcher.ResultScene)
                {
                    var play = Object.FindAnyObjectByType<PlayScene>();
                    if (play != null && play.IsPaused) play.TogglePause();
                    yield return null;
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                }
                Assert.That(SceneSwitcher.Instance.LastResult, Is.Not.Null);
                Assert.That(SceneSwitcher.Instance.LastResult2P, Is.Not.Null);
                Assert.That(SceneSwitcher.Instance.LastResult.Good, Is.EqualTo(1), "1P played automatically.");
                Assert.That(SceneSwitcher.Instance.LastResult2P.Bad, Is.EqualTo(1), "2P did not hit the don.");
                Assert.That((SceneSwitcher.Instance.LastResult.Side, SceneSwitcher.Instance.LastResult2P.Side), Is.EqualTo(("single", "single")),
                    "each result keeps the side its seat played");

                // result_2p: 2P's board on the right with its nameplate; both reveal and skip together.
                var result = Object.FindAnyObjectByType<ResultScene>();
                Assert.That(result.Boards.Count, Is.EqualTo(2));
                Assert.That(result.view.secondBoard.gameObject.activeSelf, Is.True);
                Assert.That(result.view.secondBoard.nameplate.seat, Is.EqualTo(1));
                Assert.That(((RectTransform)result.view.secondBoard.transform).position.x, Is.GreaterThan(((RectTransform)result.view.crown.transform.parent).position.x));
                // Local charts keep every seat's score; 1P played automatically and is not saved.
                Assert.That(ScoreStore.Shared.Get(song.ScoreKey, Difficulty.Oni)?.bad, Is.EqualTo(1));
                deadline = Time.realtimeSinceStartup + 10;
                while (!result.Sequence.FadeInFinished) { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                result.Don();
                Assert.That(result.Boards.All(b => b.Sequence.RevealEndMs > 0), Is.True, "One don skips both boards.");
                yield return new WaitForSecondsRealtime(0.5f);
                TestCapture.Capture("ResultTwoPlayers.png");

                // Each seat's ka turns only its own board's pages.
                yield return null;
                result.ChangePage(1, 1);
                Assert.That(result.view.secondAnalysis.Details, Is.True);
                Assert.That(result.view.analysis.Details, Is.False);
                yield return new WaitForSecondsRealtime(0.3f);
                TestCapture.Capture("ResultTwoPlayersDetail2P.png");
                result.ChangePage(0, 1);
                Assert.That(result.view.analysis.Details && result.view.secondAnalysis.Details, Is.True);
                result.ChangePage(1, -1);
                Assert.That(result.view.secondAnalysis.Details, Is.False);
                Assert.That(result.view.analysis.Details, Is.True);
            }
            finally { ScoreStore.Shared = scores; }
            Object.Destroy(song);
        }
    }
}
