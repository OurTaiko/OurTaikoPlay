using System.Collections;
using System.IO;
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

        [UnityTest, Category("SpriteRendering")]
        public IEnumerator SavedPlayNameplateKeepsItsSdfScaleOnFirstRenderAndResize()
        {
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan" });
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                TestSongs.Select();
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.GameScene);
                yield return null;
                yield return null;
                var plate = Object.FindAnyObjectByType<NameplateView>();
                var text = plate.playerName;
                AssertRenderedSdfScale(text);

                // Inspect the live mesh before capture can trigger another mesh rebuild.
                var scaler = text.canvas.rootCanvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                foreach (float scale in new[] { 0.5f, 1.25f })
                {
                    scaler.scaleFactor = scale;
                    yield return null;
                    yield return null;
                    Assert.That(text.canvas.scaleFactor, Is.EqualTo(scale));
                    AssertRenderedSdfScale(text);
                }
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest, Category("SpriteRendering")]
        public IEnumerator PlaySceneShowsNameplateScoreCounterAndAutoBadge()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Plate\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1111,\n1111,\n#END");
            // Exercise the set's art independently of the items in this device's Nameplate folder.
            NameplateItems.UseFolder(Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N")));
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Taiko Master", plateTitle = "purple", dan = 24, gold = true, danCombo = 2 });
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
                var lane = (RectTransform)play.firstLane.scoreCounter.transform.parent;
                Assert.That(lane.parent.Find("PlayerName"), Is.Null, "The debug player label is gone.");
                Assert.That(lane.parent.Find("PlayState"), Is.Null, "No AUTO PLAY / READY debug label.");

                // game_nameplate_1p: (-44, 161) from the lane's top, after the drum and before the balloon counter.
                var plate = lane.GetComponentInChildren<NameplateView>();
                Assert.That(plate, Is.Not.Null);
                Assert.That(plate.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(-44, -161)));
                Assert.That(plate.RectTransform.sizeDelta, Is.EqualTo(new Vector2(408, 96)));
                Assert.That(plate.transform.GetSiblingIndex(), Is.GreaterThan(lane.Find("Drum").GetSiblingIndex()));
                Assert.That(lane.GetComponent<Canvas>().sortingOrder, Is.LessThan(play.firstLane.balloonCounter.GetComponentInParent<Canvas>().sortingOrder),
                    "The balloon overlay draws above the lane containing the nameplate.");

                // Title and dan: the band family with the gold 達人 on the rainbow ring and the *_dani name box.
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Any(v => v.clip == "14dan_gold"), Is.True);
                Assert.That(plate.title.gameObject.activeSelf, Is.True);
                Assert.That(plate.title.text, Is.EqualTo("Taiko Master"));
                Assert.That(plate.playerName.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(261, -64.5f)));
                Assert.That(plate.playerName.fontSize, Is.EqualTo(24));
                AssertUiFont(plate.title);
                AssertUiFont(plate.playerName);
                AssertRenderedSdfScale(plate.playerName);

                // AUTO is the first option-dock badge; the nameplate stays.
                var dock = play.firstLane.modifierBadges;
                Assert.That(dock.Count, Is.EqualTo(1));
                Assert.That(dock.GetComponentsInChildren<UnityEngine.UI.Image>().First().sprite, Is.SameAs(dock.auto));
                Assert.That(plate.gameObject.activeInHierarchy, Is.True);

                // ScoreCounter: the grey cover, then right-aligned digits that start at a plain 0.
                var counter = play.firstLane.scoreCounter;
                Assert.That(counter.transform.GetSiblingIndex(), Is.GreaterThan(plate.transform.GetSiblingIndex()));
                Assert.That(counter.transform.GetSiblingIndex(), Is.LessThan(lane.Find("Ending").GetSiblingIndex()),
                    "The ending overlay remains above the lane's score counter.");
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
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Any(v => v.clip == "14dan_gold"), Is.False);
                Assert.That(plate.title.text, Is.Empty);
                Assert.That(plate.playerName.text, Is.EqualTo("Katsu-chan"));
                Assert.That(plate.playerName.fontSize, Is.EqualTo(30));
                AssertUiFont(plate.playerName);
                Assert.That(plate.playerName.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(226, -46)));
                yield return null;
                TestCapture.Capture("NameplatePlayCoin.png");

                // A long name is squeezed into the 190-px box, not shrunk.
                Controller.UseUnsaved(new PlayerInfo { name = "OurTaikoPlayerUnityLongName" });
                Assert.That(plate.playerName.fontSize, Is.EqualTo(30));
                Assert.That(plate.playerName.rectTransform.localScale.x, Is.LessThan(1));
                Assert.That(plate.playerName.preferredWidth * plate.playerName.rectTransform.localScale.x, Is.EqualTo(190).Within(0.5f));

                Controller.UseUnsaved(new PlayerInfo { title = "Rainbow", plateTitle = "rainbow" });
                var rainbow = NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-rainbow");
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Any(v => v.asset == rainbow));
                play.Back();
                yield return WaitForScene(SceneSwitcher.SongSelectScene);
            }
            finally
            {
                NameplateItems.UseFolder(null);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
            }
        }

        // A plate on screen is drawn whole when the set comes (a sign-in check downloads it); until
        // then it is its text alone.
        [UnityTest]
        public IEnumerator APlateOnScreenIsDrawnWhenTheSetComes()
        {
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Taiko Master", plateTitle = "gold", dan = 10 });
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                NameplateItems.Use(null);
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.SwitchScene(SceneSwitcher.SongSelectScene);
                yield return WaitForScene(SceneSwitcher.SongSelectScene);
                var plate = Object.FindAnyObjectByType<SongSelectScene>().view.nameplate;
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Where(v => v.RunCount > 0), Is.Empty);
                Assert.That(plate.playerName.text, Is.EqualTo("Don-chan"));
                Assert.That(plate.title.text, Is.EqualTo("Taiko Master"));

                TestData.UseBakedNameplates();
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Any(v => v.clip == "1kyuu_red"), Is.True);
                Assert.That(plate.Layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-gold")));
                Assert.That(plate.playerName.text, Is.EqualTo("Don-chan"));
            }
            finally
            {
                TestData.UseBakedNameplates();
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator SongSelectAndResultShowNameplate()
        {
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Taiko Master", plateTitle = "wood", dan = 10 });
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                SceneSwitcher.Instance.SwitchScene(SceneSwitcher.SongSelectScene);
                yield return WaitForScene(SceneSwitcher.SongSelectScene);
                var select = Object.FindAnyObjectByType<SongSelectScene>();
                var plate = select.view.nameplate;
                Assert.That(plate, Is.Not.Null);
                // song_select_nameplate_1p (14, 908): over the wheel, under the course panel and its option panel
                // (2P's plate, when it joins, sits on the same layer).
                Assert.That(plate.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(14, -908)));
                Assert.That(plate.transform.parent, Is.SameAs(select.coursePanel.parent));
                Assert.That(plate.transform.GetSiblingIndex(), Is.EqualTo(select.wheel.GetSiblingIndex() + 1));
                Assert.That(plate.transform.GetSiblingIndex(), Is.LessThan(select.coursePanel.GetSiblingIndex()));
                Assert.That(select.view.nameplate2P.transform.GetSiblingIndex(), Is.LessThan(select.coursePanel.GetSiblingIndex()));
                Assert.That(plate.GetComponentsInChildren<OurTaiko.Chara.OtanimView>().Any(v => v.clip == "1kyuu_red"));
                yield return new WaitForSecondsRealtime(0.5f);
                TestCapture.Capture("NameplateSongSelect.png");

                var run = new PlayResult
                {
                    ChartKey = TestSongs.Calibration, Title = "Input Calibration", Course = "Hard", Difficulty = Difficulty.Hard, Level = 1,
                    Score = 123450, Good = 40, Ok = 12, Bad = 30, MaxCombo = 17, Rolls = 3, GaugePoints = 2500,
                };
                SceneSwitcher.Instance.ShowResult(run);
                yield return WaitForScene(SceneSwitcher.ResultScene);
                var result = Object.FindAnyObjectByType<ResultScene>();
                plate = result.view.nameplate;
                // result_player.lua nameplate_pos (2, 922), drawn after the board and before the fade-in wipe.
                Assert.That(plate.transform.parent, Is.SameAs(result.view.analysis.scorePage.transform));
                Assert.That(plate.RectTransform.anchoredPosition, Is.EqualTo(new Vector2(2, -922)));
                Assert.That(plate.transform.GetSiblingIndex(), Is.GreaterThan(plate.transform.parent.Find("SoulSheen").GetSiblingIndex()));
                Assert.That(plate.transform.parent.GetSiblingIndex(), Is.LessThan(result.stage.Find("FadeIn").GetSiblingIndex()));
                yield return new WaitForSecondsRealtime(1.5f);
                TestCapture.Capture("NameplateResult.png");
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        // The layers a player wears (NameplateItems' files) on a play's plate: the band item in the
        // band's place, the icon over the outline, and gone again with an info that wears none.
        [UnityTest, Category("SpriteRendering")]
        public IEnumerator APlayPlateWearsThePlayersLayers()
        {
            string folder = Path.Combine(Path.GetTempPath(), "OurTaikoNameplateFlow", System.Guid.NewGuid().ToString("N"));
            NameplateItems.UseFolder(folder);
            var view = new Rect(0, 0, NameplateLayout.Width, NameplateLayout.Height);
            foreach (var (slot, name, left) in new[] { (NameplateSlot.Band, "moving", 0f), (NameplateSlot.Icon, "still", 250f) })
            {
                string path = NameplateItems.PathOf(slot, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, OtanimFixture.Build(view, OtanimFixture.Make(NameplateItems.ClipName, true, 1, left, left + 100)));
            }
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Plate\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#START\n1111,\n1111,\n#END");
            Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Layers", plateState = "custom", bandItem = "band/moving", iconItem = "icon/still" });
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.MenuScene);
                yield return null;
                yield return TestPlay.Open(song, autoPlay: true);
                yield return WaitForScene(SceneSwitcher.GameScene);
                yield return null;
                var play = Object.FindAnyObjectByType<PlayScene>();
                var plate = ((RectTransform)play.firstLane.scoreCounter.transform.parent).GetComponentInChildren<NameplateView>();
                var layers = plate.Layers;
                Assert.That(layers?.Mask, Is.Not.Null);
                Assert.That(layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, "band/moving")), "The worn band is drawn instead of the title's.");
                Assert.That(layers.BandView.gameObject.activeInHierarchy && layers.IconView.gameObject.activeInHierarchy && !layers.EffectView.gameObject.activeSelf);
                Assert.That((layers.BandView.RunCount, layers.IconView.RunCount), Is.EqualTo((1, 1)), "The plate draws them by itself every frame.");

                Controller.UseUnsaved(new PlayerInfo { name = "Don-chan", title = "Layers" });
                Assert.That(layers.BandView.asset, Is.SameAs(NameplateItems.Load(NameplateSlot.Band, "band/lumen-title-wood")), "Wearing none brings the title's own band back.");
                Assert.That(layers.Mask.gameObject.activeInHierarchy || layers.IconView.gameObject.activeSelf, Is.False);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song);
                NameplateItems.UseFolder(null);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }

        static void AssertRenderedSdfScale(TMP_Text text)
        {
            var character = text.textInfo.characterInfo.First(c => c.isVisible);
            // TMP packs world scale for camera canvases; only Overlay cancels Canvas scale.
            var canvas = text.canvas;
            float expected = character.scale * Mathf.Abs(text.rectTransform.lossyScale.y);
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) expected /= canvas.scaleFactor;
            else if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                expected = character.scale;
            var uv = new System.Collections.Generic.List<Vector4>();
            text.canvasRenderer.GetMesh().GetUVs(0, uv);
            Assert.That(uv[character.vertexIndex].w, Is.EqualTo(expected).Within(0.0001f),
                "SDF scale must match the Canvas render mode on first render and after resizing.");
        }

        // Compare properties, not identity: TMP swaps in a per-page copy of the shared material when a
        // text's glyphs all sit on a later page of the dynamic atlas.
        static void AssertUiFont(TMP_Text text)
        {
            Assert.That(UiFonts.IsUiFont(text.font), Is.True, "The play scene name and title must use the UI faces.");
            var material = text.fontSharedMaterial;
            float width = SkinUi.IsDark(text.color) ? 0 : SkinUi.OutlineWidth;
            Assert.That(material.GetFloat(ShaderUtilities.ID_OutlineWidth), Is.EqualTo(width),
                "Black text has no outline; light text has the shared black outline.");
            Assert.That(material.GetFloat(ShaderUtilities.ID_FaceDilate), Is.EqualTo(width));
            if (width > 0) Assert.That(material.GetColor(ShaderUtilities.ID_OutlineColor), Is.EqualTo(Color.black));
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
