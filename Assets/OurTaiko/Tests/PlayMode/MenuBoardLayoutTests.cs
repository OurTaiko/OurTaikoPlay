using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class MenuBoardLayoutTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheOpenEntryModeStaysAtTheCentreAndEveryBoardShows()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
            var entry = Object.FindAnyObjectByType<EntryScene>();
            // The credit screen takes no join for its first 120 ms.
            yield return new WaitForSecondsRealtime(.3f);
            entry.Don();
            Assert.That(entry.Flow.JoinedAt.HasValue, Is.True);
            yield return new WaitForSecondsRealtime(2);
            int count = entry.Board.Boards.Count;
            Assert.That(count, Is.EqualTo(4));
            float? openY = null;
            for (int selected = 0; selected < count; selected++)
            {
                if (selected > 0) entry.Ka(1);
                yield return new WaitForSecondsRealtime(.7f);
                Canvas.ForceUpdateCanvases();
                // As on the arcade, the list never shifts as a group: the open board keeps the
                // centre slot at either end, and every board stays shown in its slot.
                var open = entry.Board.Boards[selected];
                float y = StageY(entry, open.Hit.rectTransform);
                openY ??= y;
                Assert.That(y, Is.EqualTo(openY.Value).Within(.5f), "The open board keeps its place.");
                Assert.That(y, Is.EqualTo(entry.stage.rect.height / 2).Within(60), "The open board sits at the stage's middle.");
                AssertTopHit(open.Hit.gameObject, open.Hit.rectTransform);
                foreach (var board in entry.Board.Boards)
                {
                    Assert.That(board.Root.gameObject.activeInHierarchy, Is.True);
                    Assert.That(board.Fade, Is.GreaterThan(.99));
                }
                TestCapture.Capture("EntryModes" + selected + ".png");
                TestCapture.Capture("EntryModes720-" + selected + ".png", 1280, 720);
            }
            // Clicking the first board moves focus; clicking settings twice opens the settings overlay.
            entry.view.boards[0].hitRelay.Clicked();
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(entry.Flow.SelectedMode, Is.Zero);
            entry.view.boards[3].hitRelay.Clicked();
            yield return new WaitForSecondsRealtime(.7f);
            entry.view.boards[3].hitRelay.Clicked();
            Assert.That(SettingsOverlay.IsOpen, Is.True);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneSwitcher.EntryScene));
            SettingsOverlay.Instance.Close();
        }

        [UnityTest]
        public IEnumerator ReturnBoardMatchesSongBoardCapsAndOpenClosedSize()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return null;
            var select = Object.FindAnyObjectByType<SongSelectScene>();
            // The local songs are in the Local folder: open it on the first one.
            select.Manager.OpenFolderAt(0); select.Manager.Navigate(2);
            yield return new WaitForSecondsRealtime(2);
            var back = select.wheel.GetComponentsInChildren<FolderBoardView>().Single(b => b.title.text == SongSelectManager.BackLabel);
            var song = select.view.songBoards[0];
            Assert.That(back.panelClosed.type, Is.EqualTo(UnityEngine.UI.Image.Type.Sliced));
            Assert.That(back.panelClosed.sprite.rect, Is.EqualTo(song.panel.sprite.rect));
            Assert.That(back.panelClosed.sprite.border, Is.EqualTo(song.panel.sprite.border));
            Assert.That(back.panelClosed.rectTransform.rect.size, Is.EqualTo(select.view.boardPrefab.panel.rectTransform.rect.size));
            Vector2 openSongSize = song.panel.rectTransform.rect.size;
            TestCapture.Capture("ReturnBoardClosed.png");
            back.click.Clicked(); yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(select.Manager.FocusedKind, Is.EqualTo(SongSelectManager.ItemKind.Back));
            Assert.That(back.panelClosed.rectTransform.rect.size, Is.EqualTo(openSongSize));
            AssertTopHit(back.panelClosed.gameObject, back.panelClosed.rectTransform);
            TestCapture.Capture("ReturnBoardOpen.png");
            TestCapture.Capture("ReturnBoardOpen720.png", 1280, 720);
        }

        // A rect's centre, in design pixels from the stage's top.
        static float StageY(EntryScene entry, RectTransform rect)
        {
            var point = entry.stage.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return entry.stage.rect.yMax - point.y;
        }

        static void AssertTopHit(GameObject expected, RectTransform rect)
        {
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new System.Collections.Generic.List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.SameAs(expected));
        }
    }
}
