using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class SongSelectSavedLayoutTests
    {
        [UnityTest]
        public IEnumerator EditedLayoutSurvivesAwakeAnimationAndRepeatedOptionMenus()
        {
            var previousOptions = PlayOptions.Shared;
            PlayOptions.Shared = new PlayOptions();
            GameObject original = null, copy = null;
            try
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                yield return null;
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
                yield return null;
                SceneSwitcher.Instance.LastDifficulty = -1;

                // Clone all ordinary scene roots together under an inactive parent. Unity remaps the
                // controller's serialized references, and Awake cannot run until the edited copy is enabled.
                // This exercises Inspector changes before startup without saving or modifying the scene asset.
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                original = new GameObject("Original layout for cloning");
                original.SetActive(false);
                foreach (var root in roots) root.transform.SetParent(original.transform, false);
                copy = Object.Instantiate(original);
                copy.name = "Edited layout under test";
                Object.Destroy(original);
                original = null;
                yield return null;

                var select = copy.GetComponentInChildren<SongSelectScene>(true);
                var view = select.view;
                var board = view.songBoards[0];
                Assert.That(view.transform.IsChildOf(copy.transform), Is.True, "Cloning remaps the saved view.");
                Assert.That(view.songBoards.Length, Is.EqualTo(LocalSongLibrary.Instance.Songs.Count));
                Assert.That(select.wheel.GetComponentsInChildren<SongBoardView>(true).Length, Is.EqualTo(3));

                var titlePosition = new Vector2(23, -17);
                var subtitlePosition = new Vector2(19, -39);
                var coursePosition = view.cards[0].board.rectTransform.anchoredPosition + new Vector2(31, -11);
                var headerPosition = view.header.rectTransform.anchoredPosition + new Vector2(13, -7);
                var optionsPosition = view.options.board.anchoredPosition + new Vector2(17, -9);
                var arrowPosition = view.options.rows[0].rightArrow.rectTransform.anchoredPosition + new Vector2(8, -3);
                board.title.rectTransform.anchoredPosition = titlePosition;
                board.subtitle.rectTransform.anchoredPosition = subtitlePosition;
                var panelSize = new Vector2(978, 176);
                var glowSize = new Vector2(1042, 224);
                board.panel.rectTransform.sizeDelta = panelSize;
                board.glow.rectTransform.sizeDelta = glowSize;
                board.title.fontSize = 37;
                view.cards[0].board.rectTransform.anchoredPosition = coursePosition;
                view.header.rectTransform.anchoredPosition = headerPosition;
                view.options.board.anchoredPosition = optionsPosition;
                view.options.rows[0].rightArrow.rectTransform.anchoredPosition = arrowPosition;
                view.options.title.fontSize = 29;
                var title = board.title;
                var options = view.options;
                var cards = view.cards.Select(card => card.board).ToArray();
                // Rewrite the one-course song on disk as a song with all four columns: the rescan keeps
                // its SongDefinition and updates the chart under it, and previously hidden plates must
                // return to their correct content layout.
                var library = LocalSongLibrary.Instance;
                var calibration = TestSongs.Load(TestSongs.Calibration);
                System.IO.File.Copy(TestSongs.ChartPath(TestSongs.TripleHelix), TestSongs.ChartPath(TestSongs.Calibration), true);
                library.Refresh();
                Assert.That(TestSongs.Load(TestSongs.Calibration), Is.SameAs(calibration));
                Assert.That(calibration.ReadInfo().Has(Difficulty.Easy), Is.True);

                copy.SetActive(true);
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(select.view, Is.SameAs(view));
                Assert.That(view.songBoards[0].title, Is.SameAs(title));
                Assert.That(title.fontSize, Is.EqualTo(37));
                var openTitle = titlePosition + board.titleOpenOffset;
                Assert.That(title.rectTransform.anchoredPosition.x, Is.EqualTo(openTitle.x).Within(0.01f));
                Assert.That(title.rectTransform.anchoredPosition.y, Is.EqualTo(openTitle.y).Within(0.5f));
                Assert.That(board.panel.rectTransform.sizeDelta, Is.EqualTo(panelSize + Vector2.up * board.expansionHeight));
                Assert.That(board.glow.rectTransform.sizeDelta, Is.EqualTo(glowSize + Vector2.up * board.expansionHeight));
                Assert.That(board.subtitle.rectTransform.anchoredPosition, Is.EqualTo(subtitlePosition));
                Assert.That(view.header.rectTransform.anchoredPosition, Is.EqualTo(headerPosition));
                Assert.That(view.cards[0].board.rectTransform.anchoredPosition, Is.EqualTo(coursePosition));
                var addedEasy = view.songBoards[1].plates.Single(plate => plate.difficulty == Difficulty.Easy);
                Assert.That(addedEasy.group.gameObject.activeSelf, Is.True);
                Assert.That(((RectTransform)addedEasy.group.transform).anchoredPosition.x, Is.EqualTo(-273));

                select.Confirm();
                yield return WaitUntil(() => select.CourseFade >= 1);
                select.Right();
                Assert.That(select.Cursor.Selected, Is.EqualTo(Difficulty.Modifier));
                select.Confirm();
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(options.board.anchoredPosition, Is.EqualTo(optionsPosition));
                Assert.That(options.title.fontSize, Is.EqualTo(29));
                Assert.That(options.rows[0].rightArrow.rectTransform.anchoredPosition, Is.EqualTo(arrowPosition));
                // Click the saved touch zone, then close through the saved outside hit area.
                options.rows[0].next.Clicked.Invoke();
                Assert.That(select.AutoPlay, Is.True);
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.That(Vector2.Distance(options.rows[0].rightArrow.rectTransform.anchoredPosition, arrowPosition), Is.LessThan(0.01f));
                int descendants = copy.GetComponentsInChildren<Transform>(true).Length;
                options.outsideClick.Clicked.Invoke();
                yield return WaitUntil(() => !select.IsOptionPanelOpen);

                select.Confirm();
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.That(select.IsOptionPanelOpen, Is.True);
                Assert.That(view.options, Is.SameAs(options));
                Assert.That(select.AutoPlay, Is.True, "Reopening binds the same saved option state.");
                Assert.That(options.board.anchoredPosition, Is.EqualTo(optionsPosition));
                Assert.That(copy.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(descendants), "Reopening must not rebuild the hierarchy.");
                Assert.That(select.wheel.GetComponentsInChildren<SongBoardView>(true).Length, Is.EqualTo(3));
                Assert.That(view.cards.Select(card => card.board), Is.EqualTo(cards));
                options.outsideClick.Clicked.Invoke();
                yield return WaitUntil(() => !select.IsOptionPanelOpen);
            }
            finally
            {
                PlayOptions.Shared = previousOptions;
                if (copy != null) Object.Destroy(copy);
                if (original != null) Object.Destroy(original);
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
                TestSongs.Install();  // Restores the rewritten chart for later tests.
            }
        }

        static IEnumerator WaitUntil(System.Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 10;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }
    }
}
