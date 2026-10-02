using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class NameplateFlowTests
    {
        static PlayerInfoController Controller => PlayerInfoController.EnsureInstance();

        [TearDown]
        public void RestoreDefaultPlayer() => Controller.UseUnsaved(new PlayerInfo());

        [UnityTest]
        public IEnumerator PlaySceneShowsNameplateScoreCounterAndAutoBadge()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Plate\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1111,\n1111,\n#END");
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Taiko Master", titleBackground = 3, dan = 24, gold = true });
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
                var lane = (RectTransform)play.noteLayer.parent.parent;
                Assert.That(lane.parent.Find("PlayerName"), Is.Null, "The debug player label is gone.");
                Assert.That(lane.parent.Find("PlayState"), Is.Null, "No AUTO PLAY / READY debug label.");

                // game_nameplate_1p: (-44, 161) from the lane's top, after the drum and before the balloon counter.
                var plate = lane.GetComponentInChildren<NameplateView>();
                Assert.That(plate, Is.Not.Null);
                Assert.That(plate.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(-44, -161)));
                Assert.That(plate.RectTransform.sizeDelta, Is.EqualTo(new Vector2(408, 96)));
                Assert.That(plate.transform.GetSiblingIndex(), Is.GreaterThan(lane.Find("Drum").GetSiblingIndex()));
                Assert.That(plate.transform.GetSiblingIndex(), Is.LessThan(play.balloonCounter.transform.GetSiblingIndex()));

                // Title and dan: the band family with the gold 達人 chip and the *_dani name box.
                Assert.That(plate.band.enabled && plate.outline.enabled && plate.badge.enabled, Is.True);
                Assert.That(plate.band.sprite, Is.SameAs(plate.titleBackgrounds[3]));
                Assert.That(plate.dan.enabled && plate.danBackground.enabled, Is.True);
                Assert.That(plate.dan.sprite, Is.SameAs(plate.goldDanEmblems[24]));
                Assert.That(plate.title.gameObject.activeSelf, Is.True);
                Assert.That(plate.title.text, Is.EqualTo("Taiko Master"));
                Assert.That(plate.title.color, Is.EqualTo(Color.black));
                Assert.That(plate.playerName.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(261, -67.5f)));
                Assert.That(plate.playerName.fontSize, Is.EqualTo(24));
                AssertUiFont(plate.title, 0);
                AssertUiFont(plate.playerName, 3);

                // AUTO is the first option-dock badge; the nameplate stays.
                var dock = play.modifierBadges;
                Assert.That(dock.Count, Is.EqualTo(1));
                Assert.That(dock.GetComponentsInChildren<UnityEngine.UI.Image>().First().sprite, Is.SameAs(dock.auto));
                Assert.That(plate.gameObject.activeInHierarchy, Is.True);

                // ScoreCounter: the grey cover, then right-aligned digits that start at a plain 0.
                var counter = play.scoreCounter;
                Assert.That(counter.transform.GetSiblingIndex(), Is.EqualTo(lane.childCount - 1));
                Assert.That(counter.cover.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(0, -12)));
                Assert.That(counter.Text, Is.EqualTo("0"));
                Assert.That(counter.Stretch, Is.Zero);
                var session = play.Session;
                for (int i = 0; i < 4; i++) session.Hit(false, session.Chart.Notes[i].Time);
                string expected = session.Score.ToString();
                Assert.That(counter.Text, Is.EqualTo(expected));
                Assert.That(counter.Stretch, Is.GreaterThan(0), "A score change restarts the stretch.");
                var last = counter.Digit(expected.Length - 1).rectTransform;
                Assert.That(last.anchoredPosition.x - last.sizeDelta.x / 2, Is.EqualTo(225).Within(0.01f));
                Assert.That(last.sizeDelta.y, Is.EqualTo(64 + counter.Stretch));
                Assert.That(last.anchoredPosition.y + last.sizeDelta.y / 2, Is.EqualTo(-(5.5f - counter.Stretch)).Within(0.01f));
                yield return new WaitForSecondsRealtime(0.25f);
                Assert.That(counter.Stretch, Is.Zero);
                Assert.That(last.sizeDelta.y, Is.EqualTo(64));
                TestCapture.Capture("NameplatePlay.png");

                // An info change reaches the plate already on screen: back to the coin plate.
                Controller.UseUnsaved(new PlayerInfo { name = "Katsu-chan" });
                Assert.That(plate.band.enabled || plate.outline.enabled || plate.dan.enabled, Is.False);
                Assert.That(plate.title.gameObject.activeSelf, Is.False);
                Assert.That(plate.playerName.text, Is.EqualTo("Katsu-chan"));
                Assert.That(plate.playerName.fontSize, Is.EqualTo(30));
                AssertUiFont(plate.playerName, 3);
                Assert.That(plate.playerName.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(226, -53)));
                yield return null;
                TestCapture.Capture("NameplatePlayCoin.png");

                // A long name is squeezed into the 190-px box, not shrunk.
                Controller.UseUnsaved(new PlayerInfo { name = "OurTaikoPlayerUnityLongName" });
                Assert.That(plate.playerName.fontSize, Is.EqualTo(30));
                Assert.That(plate.playerName.rectTransform.localScale.x, Is.LessThan(1));
                Assert.That(plate.playerName.preferredWidth * plate.playerName.rectTransform.localScale.x, Is.EqualTo(190).Within(0.5f));

                // The rainbow band cycles its six frames, each over the previous one.
                Controller.UseUnsaved(new PlayerInfo { title = "Rainbow", rainbow = true });
                var frames = new System.Collections.Generic.HashSet<int>();
                float deadline = Time.realtimeSinceStartup + 1;
                while (Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    frames.Add(plate.RainbowFrame);
                    Assert.That(plate.band.sprite, Is.SameAs(plate.rainbowBackgrounds[plate.RainbowFrame]));
                    Assert.That(plate.bandUnder.enabled, Is.EqualTo(plate.RainbowFrame > 0));
                }
                Assert.That(frames.Count, Is.GreaterThanOrEqualTo(4));
                play.Back();
                yield return WaitForScene(SceneSwitcher.MenuScene);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        [UnityTest]
        public IEnumerator SongSelectAndResultShowNameplate()
        {
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Taiko Master", titleBackground = 1, dan = 10 });
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.SwitchScene(SceneSwitcher.SongSelectScene);
                yield return WaitForScene(SceneSwitcher.SongSelectScene);
                var select = Object.FindFirstObjectByType<SongSelectScene>();
                var plate = Object.FindFirstObjectByType<NameplateView>();
                Assert.That(plate, Is.Not.Null);
                // song_select_nameplate_1p (14, 908): over the wheel, under the course panel and its option panel.
                Assert.That(plate.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(14, -908)));
                Assert.That(plate.transform.parent, Is.SameAs(select.coursePanel.parent));
                Assert.That(plate.transform.GetSiblingIndex(), Is.EqualTo(select.wheel.GetSiblingIndex() + 1));
                Assert.That(plate.transform.GetSiblingIndex(), Is.EqualTo(select.coursePanel.GetSiblingIndex() - 1));
                Assert.That(plate.dan.sprite, Is.SameAs(plate.danEmblems[10]));
                yield return new WaitForSecondsRealtime(0.5f);
                TestCapture.Capture("NameplateSongSelect.png");

                var run = new PlayResult
                {
                    ChartKey = "Calibration", Title = "Input Calibration", Course = "Hard", Difficulty = Difficulty.Hard, Level = 1,
                    Score = 123450, Good = 40, Ok = 12, Bad = 30, MaxCombo = 17, Rolls = 3, GaugePoints = 2500,
                };
                SceneSwitcher.Instance.ShowResult(run);
                yield return WaitForScene(SceneSwitcher.ResultScene);
                var result = Object.FindFirstObjectByType<ResultScene>();
                plate = Object.FindFirstObjectByType<NameplateView>();
                // result_player.lua nameplate_pos (2, 922), drawn after the board and before the fade-in wipe.
                Assert.That(plate.transform.parent, Is.SameAs(result.stage));
                Assert.That(plate.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(2, -922)));
                Assert.That(plate.transform.GetSiblingIndex(), Is.GreaterThan(result.stage.Find("SoulSheen").GetSiblingIndex()));
                Assert.That(plate.transform.GetSiblingIndex(), Is.LessThan(result.stage.Find("FadeIn").GetSiblingIndex()));
                yield return new WaitForSecondsRealtime(1.5f);
                TestCapture.Capture("NameplateResult.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        static void AssertUiFont(TMP_Text text, float borderPixels)
        {
            Assert.That(text.font, Is.SameAs(Resources.Load<TMP_FontAsset>("Nijiiro UI SDF")),
                "The play scene name and title must use the shared UI font.");
            var material = text.fontSharedMaterial;
            ShaderUtilities.UpdateShaderRatios(material);
            float width = material.GetFloat(ShaderUtilities.ID_OutlineWidth);
            float pixels = 2 * material.GetFloat(ShaderUtilities.ID_GradientScale)
                * material.GetFloat(ShaderUtilities.ID_ScaleRatio_A) * width
                * text.fontSize / text.font.faceInfo.pointSize;
            Assert.That(pixels, Is.EqualTo(borderPixels).Within(0.01f));
            Assert.That(material.GetFloat(ShaderUtilities.ID_FaceDilate), Is.EqualTo(width));
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
