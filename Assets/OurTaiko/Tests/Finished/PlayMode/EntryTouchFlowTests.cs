using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class EntryTouchFlowTests
    {
        [UnityTest]
        public IEnumerator TapsJoinThenMoveToOrPickBoardsLikeSongSelect()
        {
            if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            yield return null;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.EntryScene);
                yield return null;
                var entry = Object.FindFirstObjectByType<EntryScene>();
                yield return new WaitForSecondsRealtime(0.2f);

                // Credit screen: the boards are not up, so a tap anywhere lands on the touch area and joins.
                Assert.That(TopHit(entry, 960, 535 - entry.Board.Root.anchoredPosition.y), Is.SameAs(entry.TouchArea.gameObject));
                entry.TouchArea.GetComponent<PointerRelay>().Clicked();
                Assert.That(entry.Flow.State, Is.EqualTo(EntryFlow.Phase.SelectMode));
                yield return WaitUntil(() => entry.Flow.IsModeReady(entry.Now), 3);
                yield return new WaitForSecondsRealtime(1f);

                // Each board takes the taps on its visible plate; empty space falls to the touch area.
                var boards = entry.Board.Boards;
                float shiftY = -entry.Board.Root.anchoredPosition.y;
                Assert.That(TopHit(entry, 960, 535 - entry.Board.Root.anchoredPosition.y), Is.SameAs(boards[0].Hit.gameObject), "The open 演奏ゲーム board.");
                Assert.That(TopHit(entry, 960 + 50, 535 + 305 + shiftY), Is.SameAs(boards[1].Hit.gameObject), "The closed 練習モード board.");
                Assert.That(TopHit(entry, 960, 535 + 205 + shiftY), Is.SameAs(boards[0].Hit.gameObject), "The open plate wins over the closed board's margin.");
                Assert.That(TopHit(entry, 200, 600), Is.SameAs(entry.TouchArea.gameObject));

                // A tap on empty space does nothing; a tap on the closed board moves to it.
                entry.TouchArea.GetComponent<PointerRelay>().Clicked();
                Assert.That(entry.Flow.SelectedMode, Is.Zero);
                Assert.That(entry.Flow.IsSelected, Is.False);
                boards[1].Hit.GetComponent<PointerRelay>().Clicked();
                Assert.That(entry.Flow.SelectedMode, Is.EqualTo(1));
                Assert.That(entry.Flow.IsSelected, Is.False, "Moving to a board does not pick it.");

                // Swipes move through the list both ways.
                entry.Board.Root.GetComponent<SwipeRelay>().Swiped(-1);
                Assert.That(entry.Flow.SelectedMode, Is.Zero);
                entry.TouchArea.GetComponent<SwipeRelay>().Swiped(1);
                Assert.That(entry.Flow.SelectedMode, Is.EqualTo(1));
                yield return new WaitForSecondsRealtime(0.8f);

                // The practice board is now open at the centre; tapping it picks it.
                Assert.That(TopHit(entry, 960, 535 - entry.Board.Root.anchoredPosition.y), Is.SameAs(boards[1].Hit.gameObject));
                boards[1].Hit.GetComponent<PointerRelay>().Clicked();
                Assert.That(entry.Flow.IsSelected, Is.True);
                float deadline = Time.realtimeSinceStartup + 20;
                do { yield return null; Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline)); }
                while (SceneSwitcher.Instance.IsSwitching || SceneManager.GetActiveScene().name != SceneSwitcher.SongSelectScene);
            }
            finally
            {
                if (SceneSwitcher.Instance != null) Object.Destroy(SceneSwitcher.Instance.gameObject);
            }
        }

        // The top UI object under a stage point (1920x1080, y down), as the event system sees it.
        static GameObject TopHit(EntryScene entry, float x, float y)
        {
            var world = entry.stage.TransformPoint(new Vector3(x, -y, 0) + (Vector3)StageTopLeftOffset(entry.stage));
            var screen = RectTransformUtility.WorldToScreenPoint(null, world);
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }

        // Local position of the stage's top-left corner relative to its pivot.
        static Vector2 StageTopLeftOffset(RectTransform stage) => new Vector2(stage.rect.xMin, stage.rect.yMax);

        static IEnumerator WaitUntil(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                yield return null;
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            }
        }
    }
}
