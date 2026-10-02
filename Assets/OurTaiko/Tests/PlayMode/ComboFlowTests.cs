using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class ComboFlowTests
    {
        [UnityTest]
        public IEnumerator ComboShowsFromTenWithWhiteSilverAndGoldDigitsOnTheDrum()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Combo\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n"
                + string.Concat(Enumerable.Repeat("1111111111111111,\n", 8)) + "#END");
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.Play(song, autoPlay: true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                if (!play.IsPaused) play.TogglePause();
                play.pausePanel.SetActive(false);
                var combo = play.combo;
                var lane = combo.transform.parent;
                Assert.That(combo.gameObject.activeSelf, Is.False, "The saved preview is hidden at start.");

                // Centred on the drum: combo_ja at (320, 136) and the digit row's centre at x 401.
                var drum = (RectTransform)lane.Find("Drum");
                float drumCentre = drum.anchoredPosition.x + drum.sizeDelta.x / 2;
                var caption = combo.captionImage.rectTransform;
                Assert.That(((RectTransform)combo.transform).anchoredPosition + caption.anchoredPosition - new Vector2(80, -20),
                    Is.EqualTo(new Vector2(320, -136)));
                Assert.That(Mathf.Abs(320 + 80 - drumCentre), Is.LessThan(1));

                var session = play.Session;
                int hit = 0;
                void HitTo(int count) { while (hit < count) session.Hit(false, session.Chart.Notes[hit++].Time); }

                HitTo(9);
                Assert.That(combo.gameObject.activeSelf, Is.False, "9 combo stays hidden.");
                HitTo(10);
                Assert.That(combo.gameObject.activeSelf, Is.True);
                Assert.That(combo.Text, Is.EqualTo("10"));
                Assert.That(combo.Digit(0).sprite, Is.SameAs(combo.whiteDigits[1]));
                Assert.That(combo.captionImage.sprite, Is.SameAs(combo.caption));
                Assert.That(combo.glimmer.gameObject.activeSelf, Is.False);
                Assert.That(combo.Stretch, Is.GreaterThan(0), "A combo change restarts the stretch.");
                // Two digits 52 apart, centred on the row: lefts at -58 and -6, tops raised by the stretch.
                Assert.That(combo.Digit(0).rectTransform.anchoredPosition.x, Is.EqualTo(-58).Within(0.01f));
                Assert.That(combo.Digit(1).rectTransform.anchoredPosition.x, Is.EqualTo(-6).Within(0.01f));
                Assert.That(combo.Digit(0).rectTransform.sizeDelta.y, Is.EqualTo(80 + combo.Stretch));
                TestCapture.Capture("Combo10.png");

                HitTo(49);
                Assert.That(combo.Digit(0).sprite, Is.SameAs(combo.whiteDigits[4]));
                HitTo(50);
                Assert.That(combo.Digit(0).sprite, Is.SameAs(combo.silverDigits[5]));
                Assert.That(combo.captionImage.sprite, Is.SameAs(combo.caption));
                Assert.That(combo.glimmer.gameObject.activeSelf, Is.False);
                TestCapture.Capture("Combo50.png");

                HitTo(101);
                Assert.That(combo.Text, Is.EqualTo("101"));
                Assert.That(Enumerable.Range(0, 3).Select(i => combo.Digit(i).sprite),
                    Is.EqualTo(new[] { combo.goldDigits[1], combo.goldDigits[0], combo.goldDigits[1] }));
                Assert.That(combo.captionImage.sprite, Is.SameAs(combo.goldCaption));
                Assert.That(combo.glimmer.gameObject.activeSelf, Is.True);
                combo.ShowTime(0.05);
                TestCapture.Capture("Combo101.png");

                // A missed note breaks the combo and hides it again.
                session.Advance(session.Chart.Notes[hit].Time + 0.5, false);
                Assert.That(session.Combo, Is.Zero);
                Assert.That(combo.gameObject.activeSelf, Is.False);
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
