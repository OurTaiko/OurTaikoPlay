using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // Stage 3 of the BM/HB-scroll plan (T9): the play and practice scenes draw notes and bar
    // lines where NoteScroll.Distance puts them, through a BPM change that moves every BM/HB
    // note on the lane together.
    public sealed class ScrollModeFlowTests
    {
        const float JudgeX = 120, JudgeY = -110, BarDrop = 4;
        const string Chart = "TITLE:Scroll Modes\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#BMSCROLL\n#START\n" +
            "#SCROLL 3\n1111,\n#BPMCHANGE 240\n1111,\n#HBSCROLL\n#SCROLL 2+0.5i\n1111,\n1111,\n#NMSCROLL\n#SCROLL 1\n1111,\n#END";

        // #SUDDEN: notes appear 1.5 s ahead and stand until 0.8 s ahead; HB ones on the clock.
        const string SuddenChart = "TITLE:Sudden\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n" +
            "#SUDDEN 1.5 0.8\n#SCROLL 1+0.5i\n1111,\n#HBSCROLL\n#BPMCHANGE 180\n1111,\n1111,\n#END";

        [UnityTest] public IEnumerator SinglePlayDrawsEveryModeWhereOpenTaikoDoes() => Verify(false, Chart);
        [UnityTest] public IEnumerator PracticeDrawsEveryModeWhereOpenTaikoDoes() => Verify(true, Chart);
        [UnityTest] public IEnumerator SinglePlayDrawsSuddenNotesWhereOpenTaikoDoes() => Verify(false, SuddenChart);

        // OpenTaiko's note speed: a measure is 75% of the 1920 design width in canvas units, so a
        // wider lane (a wider window) shows more of it without moving the notes faster.
        [UnityTest] public IEnumerator AMeasureKeepsItsLengthOnAWiderLane()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Speed\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1111,\n1111,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song);
                float deadline = Time.realtimeSinceStartup + 40;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                while (play.Session == null) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var render = typeof(PlayScene).GetMethod("RenderNotes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                // At 0 s the note at 1.5 s is three quarters of a measure (1080 units) right of the
                // judge; the note at 2 s, a measure (1440) away, is past the 16:9 lane's edge.
                render.Invoke(play, new object[] { 0.0 });
                float before = play.NoteRoot(3).anchoredPosition.x;
                Assert.That(before, Is.EqualTo(JudgeX + 1080).Within(0.01));
                Assert.That(play.NoteRoot(4), Is.Null);
                play.noteLayer.sizeDelta += new Vector2(800, 0);
                Canvas.ForceUpdateCanvases();
                render.Invoke(play, new object[] { 0.001 });
                render.Invoke(play, new object[] { 0.0 });
                Assert.That(play.NoteRoot(3).anchoredPosition.x, Is.EqualTo(before).Within(0.01), "Same speed on a wider lane.");
                Assert.That(play.NoteRoot(4), Is.Not.Null, "The wider lane shows more of the measure.");
                Assert.That(play.NoteRoot(4).anchoredPosition.x, Is.EqualTo(JudgeX + 1440).Within(0.01));
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        // OpenTaiko DisplayRoll: a roll with a vertical SCROLL (here 0.5+0.5i, coming up from the
        // lower right) turns its body and tail toward the tail, keeps its head upright, and its
        // text follows the head.
        [UnityTest] public IEnumerator AVerticalRollTurnsTowardItsTail()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Turn\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n#SCROLL 0.5+0.5i\n5008,\n#END");
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song);
                float deadline = Time.realtimeSinceStartup + 40;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.GameScene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                while (play.Session == null) { Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); yield return null; }
                var render = typeof(PlayScene).GetMethod("RenderNotes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                render.Invoke(play, new object[] { -0.5 });
                var note = play.Session.Chart.Notes[0];
                var clocks = new ScrollClockTimeline(play.Session.Chart);
                var head = NoteScroll.Distance(note, -0.5, clocks, NoteScroll.MeasureWidth);
                var tailAt = NoteScroll.TailDistance(note, -0.5, clocks, NoteScroll.MeasureWidth);
                float length = (float)(tailAt.X - head.X), rise = (float)(head.Y - tailAt.Y);
                Assert.That(rise, Is.LessThan(0), "A positive imaginary part comes from below: the tail is lower.");
                var root = play.NoteRoot(0);
                var body = (RectTransform)root.Find("RollBody"); var tail = (RectTransform)root.Find("RollTail");
                float angle = Mathf.Atan2(rise, length) * Mathf.Rad2Deg;
                Assert.That(Mathf.DeltaAngle(body.localEulerAngles.z, angle), Is.EqualTo(0).Within(0.01));
                Assert.That(Mathf.DeltaAngle(tail.localEulerAngles.z, angle), Is.EqualTo(0).Within(0.01));
                Assert.That(root.Find("Head").localEulerAngles.z, Is.EqualTo(0), "The head stays upright.");
                Assert.That(tail.anchoredPosition, Is.EqualTo(new Vector2(length, rise)));
                Assert.That(body.sizeDelta.x, Is.EqualTo(Mathf.Sqrt(length * length + rise * rise) + root.rect.height / 128f).Within(0.01));
                var moji = play.MojiRoot(0);
                Assert.That(moji.anchoredPosition.y, Is.EqualTo(root.anchoredPosition.y - 123).Within(0.01), "Roll text follows its head.");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        IEnumerator Verify(bool practice, string text)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset(text);
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.PracticeMode = practice;
                SceneSwitcher.Instance.Play(song, !practice);
                float deadline = Time.realtimeSinceStartup + 40;
                string scene = practice ? SceneSwitcher.PracticeScene : SceneSwitcher.GameScene;
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (practice)
                {
                    // A paused practice draws its preview at the cursor through the same path.
                    while (play.Session == null || Check(play, "practice preview") == 0)
                    {
                        Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                        yield return null;
                    }
                    yield break;
                }
                int checkedFrames = 0;
                // Before and after the change to 240 BPM (2 s), in HB (3–5 s) and back in NM (5–6 s).
                foreach (double at in new[] { 0.5, 1.8, 2.6, 3.3, 4.3 })
                {
                    while (play.RenderedTime < at)
                    {
                        if (play.IsPaused) play.TogglePause();
                        Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                        yield return null;
                    }
                    checkedFrames += Check(play, "at " + play.RenderedTime.ToString("F3")) > 0 ? 1 : 0;
                }
                Assert.That(checkedFrames, Is.EqualTo(5), "Every sampled frame had notes on the lane.");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        // Every drawn note and bar line against the formula; returns how many were drawn.
        static int Check(PlayScene play, string when)
        {
            var chart = play.Session.Chart;
            var clocks = new ScrollClockTimeline(chart);
            double time = play.RenderedTime, travel = NoteScroll.MeasureWidth;
            Assert.That(time * 1000, Is.EqualTo(System.Math.Round(time * 1000)).Within(1e-6), "Drawn at a whole millisecond " + when);
            int drawn = 0;
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                var root = play.NoteRoot(i);
                if (!NoteScroll.Appeared(chart.Notes[i], time))
                    Assert.That(root, Is.Null, $"note {i} drawn before #SUDDEN shows it {when}");
                if (root == null) continue;
                drawn++;
                var (x, y) = NoteScroll.Distance(chart.Notes[i], time, clocks, travel);
                Assert.That(root.anchoredPosition.x, Is.EqualTo(JudgeX + (float)x).Within(0.01), $"note {i} x {when}");
                Assert.That(root.anchoredPosition.y, Is.EqualTo(JudgeY - (float)y).Within(0.01), $"note {i} y {when}");
            }
            for (int i = 0; i < chart.Bars.Count; i++)
            {
                var root = play.BarRoot(i);
                if (root == null) continue;
                var (x, y) = NoteScroll.Distance(chart.Bars[i], time, clocks, travel);
                Assert.That(root.anchoredPosition.x, Is.EqualTo(JudgeX + (float)x).Within(0.01), $"bar {i} x {when}");
                Assert.That(root.anchoredPosition.y, Is.EqualTo(JudgeY - (float)y - BarDrop).Within(0.01), $"bar {i} y {when}");
            }
            return drawn;
        }
    }
}
