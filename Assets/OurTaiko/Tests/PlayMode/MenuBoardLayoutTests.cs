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
        public IEnumerator AllThreeEntryModesStayVisibleAndClickable()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene); yield return null;
            var entry = Object.FindFirstObjectByType<EntryScene>();
            entry.Don(); yield return new WaitForSecondsRealtime(2);
            Assert.That(entry.Board.Boards.Count, Is.EqualTo(3));
            for (int selected = 0; selected < 3; selected++)
            {
                if (selected > 0) entry.Ka(1);
                yield return new WaitForSecondsRealtime(.7f);
                Canvas.ForceUpdateCanvases();
                foreach (var board in entry.Board.Boards)
                {
                    Assert.That(board.Root.gameObject.activeInHierarchy, Is.True);
                    Assert.That(board.Fade, Is.GreaterThan(.99));
                    var rect = board.Hit.rectTransform;
                    var corners = new Vector3[4]; rect.GetWorldCorners(corners);
                    foreach (var corner in corners)
                    {
                        var point = entry.stage.InverseTransformPoint(corner);
                        float y = entry.stage.rect.yMax - point.y;
                        Assert.That(y, Is.InRange(entry.stage.rect.height * entry.view.modeSafeArea.x - .1f,
                            entry.stage.rect.height * entry.view.modeSafeArea.y + .1f));
                    }
                    AssertTopHit(board.Hit.gameObject, rect);
                }
                TestCapture.Capture("EntryThreeModes" + selected + ".png");
                TestCapture.Capture("EntryThreeModes720-" + selected + ".png", 1280, 720);
            }
            // Clicking the first board moves focus; clicking settings twice opens Settings.
            entry.view.boards[0].hitRelay.Clicked();
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(entry.Flow.SelectedMode, Is.Zero);
            entry.view.boards[2].hitRelay.Clicked();
            yield return new WaitForSecondsRealtime(.7f);
            entry.view.boards[2].hitRelay.Clicked();
            float end = Time.realtimeSinceStartup + 15;
            while (SceneManager.GetActiveScene().name != SceneSwitcher.SettingScene || SceneSwitcher.Instance.IsInputBlocked)
            { Assert.That(Time.realtimeSinceStartup, Is.LessThan(end)); yield return null; }
        }

        [UnityTest]
        public IEnumerator ReturnBoardMatchesSongBoardCapsAndOpenClosedSize()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            yield return SceneManager.LoadSceneAsync(SceneSwitcher.SongSelectScene);
            yield return new WaitForSecondsRealtime(2);
            var select = Object.FindFirstObjectByType<SongSelectScene>();
            var back = select.wheel.GetComponentsInChildren<FolderBoardView>().Single(b => b.title.text == SongSelectScene.BackLabel);
            var song = select.view.songBoards[0];
            Assert.That(back.panelClosed.type, Is.EqualTo(UnityEngine.UI.Image.Type.Sliced));
            Assert.That(back.panelClosed.sprite.rect, Is.EqualTo(song.panel.sprite.rect));
            Assert.That(back.panelClosed.sprite.border, Is.EqualTo(song.panel.sprite.border));
            Assert.That(back.panelClosed.rectTransform.rect.size, Is.EqualTo(select.view.boardPrefab.panel.rectTransform.rect.size));
            Vector2 openSongSize = song.panel.rectTransform.rect.size;
            TestCapture.Capture("ReturnBoardClosed.png");
            back.click.Clicked(); yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(select.FocusedKind, Is.EqualTo(SongSelectScene.BoardKind.Back));
            Assert.That(back.panelClosed.rectTransform.rect.size, Is.EqualTo(openSongSize));
            AssertTopHit(back.panelClosed.gameObject, back.panelClosed.rectTransform);
            TestCapture.Capture("ReturnBoardOpen.png");
            TestCapture.Capture("ReturnBoardOpen720.png", 1280, 720);
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
