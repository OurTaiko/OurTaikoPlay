using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class JudgeCounterFlowTests
    {
        [UnityTest]
        public IEnumerator JudgeCounterCountsEachJudgementInScoreDigits()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            // 12 dons, then a drumroll over the next measure.
            song.chart = new TextAsset("TITLE:Judge\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1111111111110000,\n5000000000000008,\n#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                yield return TestPlay.Open(song, autoPlay: true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindAnyObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var counter = play.firstLane.judgeCounter;

                // Over the 1P cover, after ComboAnnounce (judge_counter follows combo_announce).
                Assert.That(counter.transform.parent, Is.SameAs(play.firstLane.comboAnnounce.transform.parent));
                Assert.That(counter.transform.GetSiblingIndex(), Is.EqualTo(play.firstLane.comboAnnounce.transform.GetSiblingIndex() + 1));
                Assert.That(counter.digits, Is.EqualTo(play.firstLane.scoreCounter.digits), "Counts use the score counter's digits.");
                // Player::draw_overlays draws the drumroll counter after the judge counter: its fan, which
                // opens beside the Don too, goes over the panel.
                Assert.That(play.firstLane.drumrollCounter.GetComponentInParent<Canvas>(true).sortingOrder,
                    Is.GreaterThan(counter.GetComponentInParent<Canvas>().sortingOrder));
                for (int row = 0; row < 4; row++) Assert.That(counter.Text(row), Is.EqualTo("0"));
                Assert.That(counter.RateText, Is.EqualTo("100.0"), "Nothing judged yet reads 100.0%.");

                var session = play.Session;
                var notes = session.Chart.Notes;
                for (int i = 0; i < 10; i++) session.Hit(false, notes[i].Time);
                session.Hit(false, notes[10].Time + 0.07);   // outside 良, inside 可
                session.Advance(notes[11].Time + 0.5, false); // the 12th times out
                for (int i = 0; i < 3; i++) session.Hit(false, notes[12].Time + 0.1 + i * 0.1);
                Assert.That((session.Good, session.Ok, session.Bad, session.Rolls), Is.EqualTo((10, 1, 1, 3)));
                Assert.That(new[] { counter.Text(0), counter.Text(1), counter.Text(2), counter.Text(3) },
                    Is.EqualTo(new[] { "10", "1", "1", "3" }));
                Assert.That(counter.Digit(0, 0).sprite, Is.SameAs(counter.digits[1]));
                Assert.That(counter.Digit(0, 1).sprite, Is.SameAs(counter.digits[0]));
                // Right-aligned: the last digit's right edge is the count rect's right edge.
                var last = counter.Digit(0, 1).rectTransform;
                Assert.That(last.anchoredPosition.x, Is.Zero);
                Assert.That(counter.Digit(0, 0).rectTransform.anchoredPosition.x,
                    Is.EqualTo(-counter.pitch * counter.counts[0].rect.height).Within(0.01f));
                Assert.That(last.sizeDelta.y, Is.EqualTo(counter.counts[0].rect.height));

                // 良率 on top: (10 + 1) / 12 = 91.66…%, cut to 91.6, the tenth and % smaller on the same baseline.
                Assert.That(counter.RateText, Is.EqualTo("91.6"));
                Assert.That(counter.rate.parent.GetSiblingIndex(), Is.LessThan(counter.counts[0].parent.GetSiblingIndex()));
                Assert.That(counter.RateGlyph(0).gameObject.activeSelf, Is.False, "Two whole digits use the two right slots.");
                Assert.That(new[] { counter.RateGlyph(1).sprite, counter.RateGlyph(2).sprite, counter.RateGlyph(3).sprite,
                    counter.RateGlyph(4).sprite, counter.RateGlyph(5).sprite },
                    Is.EqualTo(new[] { counter.digits[9], counter.digits[1], counter.rateDot, counter.digits[6], counter.ratePercent }));
                var whole = counter.RateGlyph(2).rectTransform; var tenth = counter.RateGlyph(4).rectTransform;
                Assert.That(whole.sizeDelta.y, Is.EqualTo(counter.rateHeight).Within(0.01f));
                Assert.That(tenth.sizeDelta.y, Is.EqualTo(counter.rateHeight * counter.rateSmall).Within(0.01f));
                float Baseline(RectTransform glyph) => glyph.anchoredPosition.y
                    + glyph.sizeDelta.y * (0.5f - JudgeCounterView.Baseline / JudgeCounterView.CellHeight);
                Assert.That(Baseline(tenth), Is.EqualTo(Baseline(whole)).Within(0.01f));
                Assert.That(counter.RateGlyph(5).rectTransform.anchoredPosition.x, Is.LessThanOrEqualTo(0.01f), "The % ends at the rect's right edge.");
                TestCapture.Capture("JudgeCounter.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        static IEnumerator WaitForScene(string scene)
        {
            float deadline = Time.realtimeSinceStartup + 20;
            do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
            while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != scene);
            yield return null;
        }
    }
}
