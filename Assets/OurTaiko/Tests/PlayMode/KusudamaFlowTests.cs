using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Kusudama (issue #29): the shared ball over the screen, YataiDON's KusudamaCounter with Nijiiro art.
    public sealed class KusudamaFlowTests
    {
        PlayOptions originalFirst, originalSecond;

        [SetUp]
        public void SetUp()
        {
            originalFirst = PlayOptions.Shared; originalSecond = PlayOptions.Second;
            PlayOptions.Shared = new PlayOptions(); PlayOptions.Second = new PlayOptions();
        }

        [TearDown]
        public void TearDown()
        {
            PlayOptions.Shared = originalFirst; PlayOptions.Second = originalSecond;
            if (SceneSwitcher.Instance != null) SceneSwitcher.Instance.TwoPlayers = false;
        }

        static IEnumerator Play(SongDefinition song, bool twoPlayers)
        {
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
            yield return null;
            while (SceneSwitcher.Instance.IsInputBlocked) yield return null;
            SceneSwitcher.Instance.TwoPlayers = twoPlayers;
            if (twoPlayers) SceneSwitcher.Instance.Play(song, "Oni", false, "Oni", false);
            else SceneSwitcher.Instance.Play(song);
            string target = twoPlayers ? SceneSwitcher.DoublePlayScene : SceneSwitcher.GameScene;
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != target);
            yield return null;
            // Judge by hand on a paused song.
            var play = Object.FindAnyObjectByType<PlayScene>();
            while (!play.IsPaused) { play.TogglePause(); yield return null; }
            play.pausePanel.SetActive(false);
        }

        static int ShownDigits(KusudamaView view) => view.number.GetComponentsInChildren<UnityEngine.UI.Image>().Count(i => i.enabled);

        [UnityTest]
        public IEnumerator BallDropsCountsDownAndOpens()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Kusudama\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:3\n#START\n9008,\n1,\n#END");
            yield return Play(song, false);
            var play = Object.FindAnyObjectByType<PlayScene>();
            var view = play.kusudama; var session = play.Session;
            Assert.That(view, Is.Not.Null);
            Assert.That(play.kusudamaPop, Is.Not.Null);
            Assert.That(view.IsShowing, Is.False);
            // Over the score and its additions, under the pause panel.
            int order = view.GetComponent<Canvas>().sortingOrder;
            Assert.That(order, Is.GreaterThan(play.scoreCounter.scoreRank.GetComponentInParent<Canvas>().sortingOrder));
            Assert.That(order, Is.LessThan(play.pausePanel.GetComponent<Canvas>().sortingOrder));
            play.Lane1.RenderNotes(0.05, false, false);
            Assert.That(play.NoteRoot(0), Is.Not.Null, "The kusudama waits on the judge circle.");

            // The first hit brings the ball in; its count shows once it has settled.
            session.Hit(false, 0.1);
            Assert.That(view.IsShowing && view.Ball == session.KusudamaOf(0), Is.True);
            Assert.That(ShownDigits(view), Is.Zero);
            play.Lane1.RenderNotes(0.1, false, false);
            Assert.That(play.NoteRoot(0), Is.Null, "skip_note: the ball replaces the note.");
            view.ShowTime(0.6);
            Assert.That(ShownDigits(view), Is.EqualTo(1));
            Assert.That(view.number.GetComponentsInChildren<UnityEngine.UI.Image>().First(i => i.enabled).sprite, Is.SameAs(view.digits[2]));
            // Settled 930 below its texture point: top-left (-60, -597) of the 2040x1872 frame.
            Assert.That(view.ball.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(960, 597 - 936)));
            Assert.That(view.ball.sprite, Is.SameAs(view.ballFrames[0]));
            Assert.That(view.renda.color.a, Is.EqualTo(1).Within(1e-4));

            session.Hit(false, 0.7); session.Hit(false, 0.8);
            Assert.That(session.KusudamaOf(0).IsBroken && session.Resolved[0], Is.True);
            view.ShowTime(1.05);
            Assert.That(view.ball.sprite, Is.SameAs(view.ballFrames[3]), "Opening, 100 ms a frame.");
            Assert.That(ShownDigits(view), Is.Zero);
            Assert.That(view.renda.enabled, Is.False, "連打!! has faded in 200 ms.");
            view.ShowTime(0.8 + KusudamaMotion.PoppedMs / 1000 + 0.01);
            Assert.That(view.IsShowing, Is.False);
            Assert.That(session.Rolls, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator UnbrokenBallLeavesWhenTheRollEnds()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Kusudama\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:30\n#START\n9008,\n1,\n#END");
            yield return Play(song, false);
            var play = Object.FindAnyObjectByType<PlayScene>();
            play.Session.Hit(false, 0.1);
            play.kusudama.ShowTime(1.4);
            Assert.That(play.kusudama.IsShowing, Is.True);
            play.Session.Advance(1.6, false);
            play.kusudama.ShowTime(1.6);
            Assert.That(play.kusudama.IsShowing, Is.False, "YataiDON drops an unbroken counter at once.");
        }

        [UnityTest]
        public IEnumerator BothSeatsBreakOneSharedBall()
        {
            // Both blocks have a kusudama 0–1.5 s; only 1P's block has the second one, so it is a balloon.
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Kusudama Two\nBPM:120\nCOURSE:Oni\nLEVEL:1\nBALLOON:2,2\n"
                + "#START P1\n9008,\n9008,\n#END\n#START P2\n9008,\n0,\n#END");
            yield return Play(song, true);
            var play = Object.FindAnyObjectByType<PlayScene>();
            var first = play.Lanes[0].Session; var second = play.Lanes[1].Session;
            var ball = first.KusudamaOf(0);
            Assert.That(second.KusudamaOf(0), Is.SameAs(ball));
            Assert.That(ball.Required, Is.EqualTo(4));
            Assert.That(first.Chart.Notes[1].Kind, Is.EqualTo(NoteKind.Balloon), "No partner: OpenTaiko downgrades it.");

            first.Hit(false, 0.1); first.Hit(false, 0.2); second.Hit(false, 0.2);
            Assert.That(play.kusudama.Ball, Is.SameAs(ball));
            play.kusudama.ShowTime(0.6);
            Assert.That(play.kusudama.Remaining, Is.EqualTo(1));
            foreach (var lane in play.Lanes) lane.RenderNotes(0.6, false, false);
            Assert.That(play.Lanes.All(lane => lane.NoteRoot(0) == null), Is.True, "Both lanes hide the struck kusudama.");
            second.Hit(false, 0.7);
            Assert.That(ball.IsBroken && first.Resolved[0] && second.Resolved[0], Is.True);
            Assert.That(new[] { first.Score, second.Score }, Is.EqualTo(new[] { 200, 200 }));
            play.kusudama.ShowTime(0.75);
            Assert.That(play.kusudama.IsShowing && play.kusudama.ball.sprite != play.kusudama.ballFrames[0], Is.True);
        }
    }
}
